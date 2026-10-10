// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Diagnostics.CodeAnalysis;
using Avalonia.Media;
using Avalonia.Media.Fonts;
using Avalonia.Platform;
using SixLabors.Fonts.Unicode;
using SixLaborsTag = SixLabors.Fonts.Tables.AdvancedTypographic.Tag;
using SixLaborsColorFontSupport = SixLabors.Fonts.ColorFontSupport;
using SixLaborsFontCollection = SixLabors.Fonts.FontCollection;
using SixLaborsFontDescription = SixLabors.Fonts.FontDescription;
using SixLaborsFontFamily = SixLabors.Fonts.FontFamily;
using SixLaborsFontMatch = SixLabors.Fonts.FontMatch;
using SixLaborsFontMetrics = SixLabors.Fonts.FontMetrics;
using SixLaborsFontStyle = SixLabors.Fonts.FontStyle;
using SixLaborsSystemFonts = SixLabors.Fonts.SystemFonts;

namespace SixLabors.ImageSharp.Drawing.Renderers.Avalonia;

/// <summary>
/// Avalonia font manager implementation backed by SixLabors.Fonts.
/// </summary>
internal sealed class FontManagerImpl : IFontManagerImpl
{
    private const SixLaborsColorFontSupport FallbackColorFontSupport =
        SixLaborsColorFontSupport.ColrV0 | SixLaborsColorFontSupport.ColrV1 | SixLaborsColorFontSupport.Svg;

    /// <summary>
    /// Initializes a new instance of the <see cref="FontManagerImpl"/> class.
    /// </summary>
    public FontManagerImpl()
    {
    }

    /// <inheritdoc />
    public string GetDefaultFontFamilyName()
        => SixLaborsSystemFonts.GetDefaultFamilyName();

    /// <inheritdoc />
    public string[] GetInstalledFontFamilyNames(bool checkForUpdates = false)
        => SixLaborsSystemFonts.GetFamilyNames(checkForUpdates);

    /// <inheritdoc />
    public bool TryMatchCharacter(int codepoint, FontStyle style, FontWeight weight, FontStretch stretch, string? familyName, System.Globalization.CultureInfo? culture, [NotNullWhen(true)] out IPlatformTypeface? platformTypeface)
    {
        CodePoint codePoint = new(codepoint);
        SixLaborsFontStyle imageSharpStyle = ToImageSharpStyle(style, weight);

        if (SixLaborsSystemFonts.TryMatchCharacter(codePoint, imageSharpStyle, familyName, culture, out SixLaborsFontMatch match)
            && this.TryCreatePlatformTypeface(match, out platformTypeface))
        {
            return true;
        }

        platformTypeface = null;
        return false;
    }

    /// <inheritdoc />
    public bool TryCreateGlyphTypeface(string familyName, FontStyle style, FontWeight weight, FontStretch stretch, [NotNullWhen(true)] out IPlatformTypeface? platformTypeface)
    {
        if (!SixLaborsSystemFonts.TryGet(familyName, out SixLaborsFontFamily family))
        {
            platformTypeface = null;
            return false;
        }

        SixLaborsFontStyle requestedStyle = ToImageSharpStyle(style, weight);
        SixLaborsFontStyle resolvedStyle = requestedStyle;

        if (!family.TryGetMetrics(resolvedStyle, out _))
        {
            resolvedStyle = family.TryGetMetrics(SixLaborsFontStyle.Regular, out _)
                ? SixLaborsFontStyle.Regular
                : family.GetAvailableStyles().Span[0];
        }

        FontSimulations fontSimulations = FontSimulations.None;

        // Avalonia records only the styles missing from the resolved face. SixLabors.Fonts then
        // synthesizes those styles while retaining the metrics and tables of that resolved face.
        if ((requestedStyle & SixLaborsFontStyle.Bold) != 0 && (resolvedStyle & SixLaborsFontStyle.Bold) == 0)
        {
            fontSimulations |= FontSimulations.Bold;
        }

        if ((requestedStyle & SixLaborsFontStyle.Italic) != 0 && (resolvedStyle & SixLaborsFontStyle.Italic) == 0)
        {
            fontSimulations |= FontSimulations.Oblique;
        }

        return this.TryCreatePlatformTypeface(family, resolvedStyle, fontSimulations, out platformTypeface);
    }

    /// <inheritdoc />
    public bool TryCreateGlyphTypeface(Stream stream, FontSimulations fontSimulations, [NotNullWhen(true)] out IPlatformTypeface? platformTypeface)
    {
        try
        {
            SixLaborsFontCollection collection = new();
            SixLaborsFontDescription description;
            SixLaborsFontFamily family = collection.Add(stream, out description);
            family.TryGetMetrics(description.Style, out SixLaborsFontMetrics? metrics);

            platformTypeface = new PlatformTypeface(
                description.FontFamilyInvariantCulture,
                ToAvaloniaStyle(description.Style),
                ToAvaloniaWeight(description.Style),
                FontStretch.Normal,
                metrics!,
                family,
                description.Style,
                fontSimulations);

            return true;
        }
        catch
        {
            platformTypeface = null;
            return false;
        }
    }

    /// <inheritdoc />
    public bool TryGetFamilyTypefaces(string familyName, [NotNullWhen(true)] out IReadOnlyList<Typeface>? familyTypefaces)
    {
        if (!SixLaborsSystemFonts.TryGet(familyName, out SixLaborsFontFamily family))
        {
            familyTypefaces = null;
            return false;
        }

        ReadOnlySpan<SixLaborsFontStyle> styles = family.GetAvailableStyles().Span;
        Typeface[] typefaces = new Typeface[styles.Length];

        for (int i = 0; i < styles.Length; i++)
        {
            SixLaborsFontStyle faceStyle = styles[i];
            typefaces[i] = new Typeface(familyName, ToAvaloniaStyle(faceStyle), ToAvaloniaWeight(faceStyle), FontStretch.Normal);
        }

        familyTypefaces = typefaces;
        return true;
    }

    private static FontStyle ToAvaloniaStyle(SixLaborsFontStyle style)
        => (style & SixLaborsFontStyle.Italic) == SixLaborsFontStyle.Italic ? FontStyle.Italic : FontStyle.Normal;

    private static FontWeight ToAvaloniaWeight(SixLaborsFontStyle style)
        => (style & SixLaborsFontStyle.Bold) == SixLaborsFontStyle.Bold ? FontWeight.Bold : FontWeight.Normal;

    private static SixLaborsFontStyle ToImageSharpStyle(FontStyle style, FontWeight weight)
    {
        SixLaborsFontStyle result = (int)weight >= 600 ? SixLaborsFontStyle.Bold : SixLaborsFontStyle.Regular;

        if (style == FontStyle.Italic || style == FontStyle.Oblique)
        {
            result |= SixLaborsFontStyle.Italic;
        }

        return result;
    }

    private bool TryCreatePlatformTypeface(SixLaborsFontMatch match, [NotNullWhen(true)] out IPlatformTypeface? platformTypeface)
        => this.TryCreatePlatformTypeface(match.Family, match.Style, FontSimulations.None, out platformTypeface);

    private bool TryCreatePlatformTypeface(
        SixLaborsFontFamily family,
        SixLaborsFontStyle style,
        FontSimulations fontSimulations,
        [NotNullWhen(true)] out IPlatformTypeface? platformTypeface)
    {
        if (!family.TryGetMetrics(style, out SixLabors.Fonts.FontMetrics? metrics))
        {
            platformTypeface = null;
            return false;
        }

        platformTypeface = new PlatformTypeface(
            metrics.Description.FontFamilyInvariantCulture,
            ToAvaloniaStyle(metrics.Description.Style),
            ToAvaloniaWeight(metrics.Description.Style),
            FontStretch.Normal,
            metrics,
            family,
            metrics.Description.Style,
            fontSimulations);

        return true;
    }
}

/// <summary>
/// Avalonia platform typeface wrapper for a SixLabors.Fonts face.
/// </summary>
internal sealed class PlatformTypeface : IPlatformTypeface
{
    private readonly SixLaborsFontMetrics fontMetrics;
    private readonly SixLaborsFontFamily fontFamily;
    private readonly SixLaborsFontStyle fontStyle;

    /// <summary>
    /// Initializes a new platform typeface wrapper.
    /// </summary>
    /// <param name="familyName">The Avalonia family name.</param>
    /// <param name="style">The Avalonia font style.</param>
    /// <param name="weight">The Avalonia font weight.</param>
    /// <param name="stretch">The Avalonia font stretch.</param>
    /// <param name="fontMetrics">The SixLabors font metrics.</param>
    /// <param name="fontFamily">The SixLabors font family.</param>
    /// <param name="fontStyle">The SixLabors font style.</param>
    /// <param name="fontSimulations">The requested font simulations.</param>
    public PlatformTypeface(
        string familyName,
        FontStyle style,
        FontWeight weight,
        FontStretch stretch,
        SixLaborsFontMetrics fontMetrics,
        SixLaborsFontFamily fontFamily,
        SixLaborsFontStyle fontStyle,
        FontSimulations fontSimulations = FontSimulations.None)
    {
        this.FamilyName = familyName;
        this.Style = style;
        this.Weight = weight;
        this.Stretch = stretch;
        this.FontSimulations = fontSimulations;
        this.fontMetrics = fontMetrics;
        this.fontStyle = fontStyle;
        this.fontFamily = fontFamily;
    }

    /// <inheritdoc />
    public string FamilyName { get; }

    /// <inheritdoc />
    public FontWeight Weight { get; }

    /// <inheritdoc />
    public FontStyle Style { get; }

    /// <inheritdoc />
    public FontStretch Stretch { get; }

    /// <inheritdoc />
    public FontSimulations FontSimulations { get; }

    /// <summary>
    /// Creates a SixLabors font for the exact face this typeface wraps, at the given size.
    /// </summary>
    /// <param name="size">The requested font size.</param>
    /// <returns>The created SixLabors font.</returns>
    public SixLabors.Fonts.Font CreateFont(float size)
    {
        SixLaborsFontStyle requestedStyle = this.fontStyle;

        // Avalonia resolves a real face first and records any missing bold or oblique style as a
        // simulation. SixLabors.Fonts performs those simulations from the requested style while
        // retaining the resolved face metrics, so include the simulation flags in that request.
        if ((this.FontSimulations & FontSimulations.Bold) != 0)
        {
            requestedStyle |= SixLaborsFontStyle.Bold;
        }

        if ((this.FontSimulations & FontSimulations.Oblique) != 0)
        {
            requestedStyle |= SixLaborsFontStyle.Italic;
        }

        return this.fontFamily.CreateFont(size, requestedStyle);
    }

    /// <inheritdoc />
    public bool TryGetStream([NotNullWhen(true)] out Stream? stream)
    {
        try
        {
            stream = this.fontMetrics.OpenStream();
            return true;
        }
        catch
        {
            stream = null;
            return false;
        }
    }

    /// <inheritdoc />
    public bool TryGetTable(OpenTypeTag tag, out ReadOnlyMemory<byte> table)
        => this.fontMetrics.TryGetTableData(new SixLaborsTag((uint)tag), out table);

    /// <inheritdoc />
    public void Dispose()
    {
    }
}
