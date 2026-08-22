// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Platform;
using SixLabors.Fonts;
using SixLabors.ImageSharp.Drawing.Processing;
using AvaloniaTextHintingMode = Avalonia.Media.TextHintingMode;
using AvaloniaTextOptions = Avalonia.Media.TextOptions;

namespace SixLabors.ImageSharp.Drawing.Renderers.Avalonia;

/// <summary>
/// Avalonia glyph run implementation backed by SixLabors.Fonts glyph ids and positions.
/// </summary>
internal sealed class GlyphRunImpl : IGlyphRunImpl
{
    /// <summary>
    /// Positioned per-glyph metrics measured once at construction. Bounds derive from the
    /// entries and <see cref="GetIntersections"/> scans them for decoration skipping.
    /// </summary>
    private readonly ReadOnlyMemory<SixLabors.Fonts.GlyphMetrics> glyphMetrics;

    /// <summary>
    /// Memoized <see cref="GetIntersections"/> result. Avalonia retains glyph runs and redraws
    /// decorations with the same limits every paint, so one slot covers the realistic pattern.
    /// Reference assignment keeps the read-check-publish sequence safe without locking.
    /// </summary>
    private IntersectionsCache? intersectionsCache;

    /// <summary>
    /// The hinting mode the run was most recently drawn with; intersections are computed from
    /// the same hinted geometry so decoration gaps align with the rendered outlines.
    /// </summary>
    private HintingMode hintingMode = HintingMode.Standard;

    /// <summary>
    /// Initializes a new glyph run wrapper.
    /// </summary>
    /// <param name="glyphTypeface">The Avalonia glyph typeface.</param>
    /// <param name="fontRenderingEmSize">The font rendering em size.</param>
    /// <param name="glyphInfos">The positioned glyph information from Avalonia shaping.</param>
    /// <param name="baselineOrigin">The glyph-run baseline origin.</param>
    /// <param name="font">The SixLabors font face used to render the run.</param>
    public GlyphRunImpl(GlyphTypeface glyphTypeface, double fontRenderingEmSize, IReadOnlyList<GlyphInfo> glyphInfos, AvaloniaPoint baselineOrigin, Font font)
    {
        this.GlyphTypeface = glyphTypeface;
        this.FontRenderingEmSize = fontRenderingEmSize;
        this.BaselineOrigin = baselineOrigin;
        this.Font = font;

        ushort[] glyphIds = new ushort[glyphInfos.Count];
        Vector2[] origins = new Vector2[glyphInfos.Count];

        double penX = 0;
        for (int i = 0; i < glyphInfos.Count; i++)
        {
            GlyphInfo glyph = glyphInfos[i];
            AvaloniaVector offset = glyph.GlyphOffset;

            glyphIds[i] = glyph.GlyphIndex;
            origins[i] = new Vector2((float)(baselineOrigin.X + penX + offset.X), (float)(baselineOrigin.Y + offset.Y));

            penX += glyph.GlyphAdvance;
        }

        this.GlyphIds = glyphIds;
        this.GlyphOrigins = origins;

        // Measure directly from the per-glyph metrics cache; the entries match the boxes
        // ImageSharp reports when rendering the same positioned run with the same options.
        this.glyphMetrics = TextMeasurer.GetGlyphMetrics(
            this.GlyphIds.Span,
            this.GlyphOrigins.Span,
            new GlyphOptions
            {
                Font = font,
                TextBaseline = TextBaseline.Alphabetic,
                ColorFontSupport = ColorFontSupport.ColrV0 | ColorFontSupport.ColrV1 | ColorFontSupport.Svg
            });

        FontRectangle measured = FontRectangle.Empty;
        bool hasBounds = false;
        foreach (SixLabors.Fonts.GlyphMetrics entry in this.glyphMetrics.Span)
        {
            if (entry.Bounds == FontRectangle.Empty)
            {
                continue;
            }

            measured = hasBounds ? FontRectangle.Union(measured, entry.Bounds) : entry.Bounds;
            hasBounds = true;
        }

        this.Bounds = !hasBounds
            ? new Rect(baselineOrigin.X, baselineOrigin.Y, penX, 0)
            : new Rect(measured.X, measured.Y, measured.Width, measured.Height).Inflate(1);
    }

    /// <inheritdoc />
    public GlyphTypeface GlyphTypeface { get; }

    /// <summary>
    /// Gets the exact SixLabors font face the run was shaped against.
    /// </summary>
    public Font Font { get; }

    /// <inheritdoc />
    public double FontRenderingEmSize { get; }

    /// <inheritdoc />
    public AvaloniaPoint BaselineOrigin { get; }

    /// <inheritdoc />
    public Rect Bounds { get; }

    /// <summary>
    /// Gets the glyph identifiers used by ImageSharp.
    /// </summary>
    public ReadOnlyMemory<ushort> GlyphIds { get; }

    /// <summary>
    /// Gets the absolute glyph origins used by ImageSharp.
    /// </summary>
    public ReadOnlyMemory<Vector2> GlyphOrigins { get; }

    /// <summary>
    /// Creates ImageSharp rich glyph options from the current Avalonia text options.
    /// </summary>
    /// <param name="textOptions">The effective Avalonia text options.</param>
    /// <returns>The ImageSharp glyph options.</returns>
    public RichGlyphOptions CreateGlyphOptions(AvaloniaTextOptions textOptions)
    {
        // Track the hinting mode the run is drawn with so outline-derived measurements
        // (decoration intersections) use the same hinted geometry.
        this.hintingMode = textOptions.TextHintingMode switch
        {
            AvaloniaTextHintingMode.None => HintingMode.None,
            AvaloniaTextHintingMode.Light => HintingMode.Standard,
            AvaloniaTextHintingMode.Strong => HintingMode.Standard,
            _ => HintingMode.Standard
        };

        return new RichGlyphOptions
        {
            Font = this.Font,
            HintingMode = this.hintingMode,

            // Avalonia positions glyph runs by their baseline origins, so the run must be
            // anchored by the alphabetic baseline rather than the line-box default.
            TextBaseline = TextBaseline.Alphabetic,
            ColorFontSupport = ColorFontSupport.ColrV0 | ColorFontSupport.ColrV1 | ColorFontSupport.Svg
        };
    }

    /// <inheritdoc />
    /// <remarks>
    /// Computed from the exact outline geometry via the Fonts intersection API, matching the
    /// precision of the Skia backend's <c>SKTextBlob.GetIntercepts</c>, so text decorations
    /// break tightly around descender strokes.
    /// </remarks>
    public IReadOnlyList<float> GetIntersections(float lowerLimit, float upperLimit)
    {
        HintingMode currentHintingMode = this.hintingMode;
        IntersectionsCache? cache = this.intersectionsCache;
        if (cache is not null
            && cache.LowerLimit == lowerLimit
            && cache.UpperLimit == upperLimit
            && cache.HintingMode == currentHintingMode)
        {
            return cache.Intersections;
        }

        // The limits arrive relative to the baseline; the run origins are absolute.
        float bandTop = (float)this.BaselineOrigin.Y + lowerLimit;
        float bandBottom = (float)this.BaselineOrigin.Y + upperLimit;

        IReadOnlyList<float> intersections = TextMeasurer.GetIntersections(
            this.GlyphIds.Span,
            this.GlyphOrigins.Span,
            new GlyphOptions
            {
                Font = this.Font,
                HintingMode = currentHintingMode,
                TextBaseline = TextBaseline.Alphabetic,
                ColorFontSupport = ColorFontSupport.ColrV0 | ColorFontSupport.ColrV1 | ColorFontSupport.Svg
            },
            bandTop,
            bandBottom).ToArray();

        this.intersectionsCache = new IntersectionsCache(lowerLimit, upperLimit, currentHintingMode, intersections);
        return intersections;
    }

    /// <inheritdoc />
    public void Dispose()
    {
    }

    /// <summary>
    /// One memoized intersections result for a limit band.
    /// </summary>
    private sealed class IntersectionsCache
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="IntersectionsCache"/> class.
        /// </summary>
        /// <param name="lowerLimit">The band's lower limit relative to the baseline.</param>
        /// <param name="upperLimit">The band's upper limit relative to the baseline.</param>
        /// <param name="hintingMode">The hinting mode the intersections were computed with.</param>
        /// <param name="intersections">The merged, x-sorted interval pairs.</param>
        public IntersectionsCache(float lowerLimit, float upperLimit, HintingMode hintingMode, IReadOnlyList<float> intersections)
        {
            this.LowerLimit = lowerLimit;
            this.UpperLimit = upperLimit;
            this.HintingMode = hintingMode;
            this.Intersections = intersections;
        }

        /// <summary>
        /// Gets the hinting mode the intersections were computed with.
        /// </summary>
        public HintingMode HintingMode { get; }

        /// <summary>
        /// Gets the band's lower limit relative to the baseline.
        /// </summary>
        public float LowerLimit { get; }

        /// <summary>
        /// Gets the band's upper limit relative to the baseline.
        /// </summary>
        public float UpperLimit { get; }

        /// <summary>
        /// Gets the merged, x-sorted interval pairs.
        /// </summary>
        public IReadOnlyList<float> Intersections { get; }
    }
}
