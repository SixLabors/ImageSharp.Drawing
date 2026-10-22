// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Collections.Concurrent;
using System.Globalization;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Media.TextFormatting.Unicode;
using Avalonia.Platform;
using SixLaborsFont = SixLabors.Fonts.Font;
using SixLaborsShapedGlyph = SixLabors.Fonts.ShapedGlyph;
using SixLaborsTag = SixLabors.Fonts.Tables.AdvancedTypographic.Tag;
using SixLaborsTextDirection = SixLabors.Fonts.TextDirection;
using SixLaborsTextShaper = SixLabors.Fonts.TextShaper;
using SixLaborsTextShapingBuffer = SixLabors.Fonts.TextShapingBuffer;

namespace SixLabors.ImageSharp.Drawing.Renderers.Avalonia;

/// <summary>
/// Avalonia text shaper implementation backed by SixLabors.Fonts.
/// </summary>
internal sealed class TextShaperImpl : ITextShaperImpl
{
    [ThreadStatic]
    private static SixLaborsTextShapingBuffer? shapingBuffer;

    /// <inheritdoc />
    public ShapedBuffer ShapeText(ReadOnlyMemory<char> text, TextShaperOptions options)
    {
        if (text.Length == 0)
        {
            return new ShapedBuffer(text, 0, options.GlyphTypeface, options.FontRenderingEmSize, options.BidiLevel);
        }

        GlyphTypeface glyphTypeface = options.GlyphTypeface;

        if (glyphTypeface.TextShaperTypeface is not TextShaperTypeface shaperTypeface)
        {
            throw new NotSupportedException("The provided GlyphTypeface is not supported by this text shaper.");
        }

        ReadOnlySpan<char> textSpan = text.Span;

        // A trailing break character must keep its cluster for caret navigation while
        // rendering as nothing, and a trailing carriage return and line feed pair
        // shares one cluster anchored at the carriage return.
        int trailingBreakStart = -1;
        if (new Codepoint(textSpan[^1]).IsBreakChar)
        {
            trailingBreakStart = textSpan.Length > 1 && textSpan[^2] == '\r' && textSpan[^1] == '\n'
                ? textSpan.Length - 2
                : textSpan.Length - 1;
        }

        SixLaborsTextShapingBuffer buffer = shapingBuffer ??= new SixLaborsTextShapingBuffer();
        buffer.Add(textSpan);
        buffer.TextDirection = (options.BidiLevel & 1) == 0
            ? SixLaborsTextDirection.LeftToRight
            : SixLaborsTextDirection.RightToLeft;
        buffer.Language = options.Culture ?? CultureInfo.CurrentCulture;

        SixLaborsFont font = shaperTypeface.GetFont((float)options.FontRenderingEmSize);
        SixLaborsTag[] features = GetFeatures(options);

        if (features.Length > 0)
        {
            SixLaborsTextShaper.ShapeRun(font, buffer, features);
        }
        else
        {
            SixLaborsTextShaper.ShapeRun(font, buffer);
        }

        int bufferLength = buffer.Count;
        ShapedBuffer shapedBuffer = new(text, bufferLength, glyphTypeface, options.FontRenderingEmSize, options.BidiLevel);

        ushort invisibleGlyph = trailingBreakStart >= 0 ? glyphTypeface.CharacterToGlyphMap[' '] : (ushort)0;

        for (int i = 0; i < bufferLength; i++)
        {
            ref readonly SixLaborsShapedGlyph shapedGlyph = ref buffer[i];

            ushort glyphIndex = shapedGlyph.GlyphId;
            int glyphCluster = shapedGlyph.StringIndex;
            double glyphAdvance = shapedGlyph.AdvanceWidth + options.LetterSpacing;

            // Shaping offsets are Y-up while Avalonia positions glyphs Y-down.
            Vector glyphOffset = new(shapedGlyph.Offset.X, -shapedGlyph.Offset.Y);

            if (trailingBreakStart >= 0 && glyphCluster >= trailingBreakStart)
            {
                glyphIndex = invisibleGlyph;
                glyphCluster = trailingBreakStart;
                glyphAdvance = options.LetterSpacing;
                glyphOffset = default;
            }

            shapedBuffer[i] = new global::Avalonia.Media.TextFormatting.GlyphInfo(glyphIndex, glyphCluster, glyphAdvance, glyphOffset);
        }

        return shapedBuffer;
    }

    /// <inheritdoc />
    public ITextShaperTypeface CreateTypeface(GlyphTypeface glyphTypeface)
    {
        if (glyphTypeface.PlatformTypeface is not PlatformTypeface platformTypeface)
        {
            throw new NotSupportedException("The provided GlyphTypeface is not supported by this text shaper.");
        }

        return new TextShaperTypeface(platformTypeface);
    }

    /// <summary>
    /// Maps the requested font features to shaping feature tags.
    /// </summary>
    /// <param name="options">The text shaper options.</param>
    /// <returns>The feature tags to turn on.</returns>
    private static SixLaborsTag[] GetFeatures(TextShaperOptions options)
    {
        if (options.FontFeatures is null || options.FontFeatures.Count == 0)
        {
            return [];
        }

        // Shaping accepts whole-run feature enablement only, so disabled features and
        // sub-run feature ranges cannot be expressed and are not forwarded.
        int enabled = 0;
        for (int i = 0; i < options.FontFeatures.Count; i++)
        {
            if (options.FontFeatures[i].Value != 0)
            {
                enabled++;
            }
        }

        if (enabled == 0)
        {
            return [];
        }

        SixLaborsTag[] features = new SixLaborsTag[enabled];
        int index = 0;
        for (int i = 0; i < options.FontFeatures.Count; i++)
        {
            FontFeature fontFeature = options.FontFeatures[i];
            if (fontFeature.Value != 0)
            {
                features[index++] = SixLaborsTag.Parse(fontFeature.Tag);
            }
        }

        return features;
    }
}

/// <summary>
/// Text shaper typeface caching the SixLabors fonts created for a platform typeface.
/// </summary>
internal sealed class TextShaperTypeface : ITextShaperTypeface
{
    private readonly PlatformTypeface platformTypeface;
    private readonly ConcurrentDictionary<float, SixLaborsFont> fonts = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="TextShaperTypeface"/> class.
    /// </summary>
    /// <param name="platformTypeface">The platform typeface to shape with.</param>
    public TextShaperTypeface(PlatformTypeface platformTypeface) => this.platformTypeface = platformTypeface;

    /// <summary>
    /// Gets the SixLabors font for this typeface at the given size.
    /// </summary>
    /// <param name="size">The requested font size.</param>
    /// <returns>The font.</returns>
    public SixLaborsFont GetFont(float size)
        => this.fonts.GetOrAdd(size, static (requestedSize, typeface) => typeface.CreateFont(requestedSize), this.platformTypeface);

    /// <inheritdoc />
    public void Dispose()
    {
    }
}
