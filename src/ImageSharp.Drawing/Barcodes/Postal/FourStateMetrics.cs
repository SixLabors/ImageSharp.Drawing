// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The dimensions of a height modulated postal symbology, in modules of its bar width.
/// </summary>
internal readonly struct FourStateMetrics
{
    /// <summary>
    /// Initializes a new instance of the <see cref="FourStateMetrics"/> struct.
    /// </summary>
    /// <param name="xDimension">The bar width in millimetres, which is one module.</param>
    /// <param name="space">The width of the space between bars, in modules.</param>
    /// <param name="ascender">The height of the ascender part above the tracker, in modules.</param>
    /// <param name="tracker">The height of the tracker, in modules.</param>
    /// <param name="descender">The height of the descender part below the tracker, in modules.</param>
    /// <param name="quietZone">The clear zone at each end of the symbol, in modules.</param>
    public FourStateMetrics(double xDimension, double space, float ascender, float tracker, float descender, float quietZone)
        : this(xDimension, space, ascender, tracker, descender, quietZone, BarcodeTextSide.BelowBars, 0F)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="FourStateMetrics"/> struct.
    /// </summary>
    /// <param name="xDimension">The bar width in millimetres, which is one module.</param>
    /// <param name="space">The width of the space between bars, in modules.</param>
    /// <param name="ascender">The height of the ascender part above the tracker, in modules.</param>
    /// <param name="tracker">The height of the tracker, in modules.</param>
    /// <param name="descender">The height of the descender part below the tracker, in modules.</param>
    /// <param name="quietZone">The clear zone at each end of the symbol, in modules.</param>
    /// <param name="textSide">The side of the bars the human readable interpretation stands on.</param>
    /// <param name="textClearance">The clear space between the bars and the human readable interpretation, in modules.</param>
    public FourStateMetrics(
        double xDimension,
        double space,
        float ascender,
        float tracker,
        float descender,
        float quietZone,
        BarcodeTextSide textSide,
        float textClearance)
        : this(xDimension, space, ascender, tracker, descender, quietZone, textSide, textClearance, BarcodeTextAlignment.Centered)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="FourStateMetrics"/> struct.
    /// </summary>
    /// <param name="xDimension">The bar width in millimetres, which is one module.</param>
    /// <param name="space">The width of the space between bars, in modules.</param>
    /// <param name="ascender">The height of the ascender part above the tracker, in modules.</param>
    /// <param name="tracker">The height of the tracker, in modules.</param>
    /// <param name="descender">The height of the descender part below the tracker, in modules.</param>
    /// <param name="quietZone">The clear zone at each end of the symbol, in modules.</param>
    /// <param name="textSide">The side of the bars the human readable interpretation stands on.</param>
    /// <param name="textClearance">The clear space between the bars and the human readable interpretation, in modules.</param>
    /// <param name="textAlignment">Where the human readable interpretation stands within the symbol width.</param>
    public FourStateMetrics(
        double xDimension,
        double space,
        float ascender,
        float tracker,
        float descender,
        float quietZone,
        BarcodeTextSide textSide,
        float textClearance,
        BarcodeTextAlignment textAlignment)
        : this(xDimension, space, ascender, tracker, descender, quietZone, 0F, textSide, textClearance, textAlignment)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="FourStateMetrics"/> struct.
    /// </summary>
    /// <param name="xDimension">The bar width in millimetres, which is one module.</param>
    /// <param name="space">The width of the space between bars, in modules.</param>
    /// <param name="ascender">The height of the ascender part above the tracker, in modules.</param>
    /// <param name="tracker">The height of the tracker, in modules.</param>
    /// <param name="descender">The height of the descender part below the tracker, in modules.</param>
    /// <param name="quietZone">The clear zone at each end of the symbol, in modules.</param>
    /// <param name="verticalQuietZone">The clear zone above and below the symbol, in modules.</param>
    /// <param name="textSide">The side of the bars the human readable interpretation stands on.</param>
    /// <param name="textClearance">The clear space between the bars and the human readable interpretation, in modules.</param>
    /// <param name="textAlignment">Where the human readable interpretation stands within the symbol width.</param>
    public FourStateMetrics(
        double xDimension,
        double space,
        float ascender,
        float tracker,
        float descender,
        float quietZone,
        float verticalQuietZone,
        BarcodeTextSide textSide,
        float textClearance,
        BarcodeTextAlignment textAlignment)
        : this(xDimension, space, ascender, tracker, descender, quietZone, verticalQuietZone, textSide, textClearance, textAlignment, null, false)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="FourStateMetrics"/> struct.
    /// </summary>
    /// <param name="xDimension">The bar width in millimetres, which is one module.</param>
    /// <param name="space">The width of the space between bars, in modules at <paramref name="xDimension"/>.</param>
    /// <param name="ascender">The height of the ascender part above the tracker, in modules.</param>
    /// <param name="tracker">The height of the tracker, in modules.</param>
    /// <param name="descender">The height of the descender part below the tracker, in modules.</param>
    /// <param name="quietZone">The clear zone at each end of the symbol, in modules.</param>
    /// <param name="verticalQuietZone">The clear zone above and below the symbol, in modules.</param>
    /// <param name="textSide">The side of the bars the human readable interpretation stands on.</param>
    /// <param name="textClearance">The clear space between the bars and the human readable interpretation, in modules.</param>
    /// <param name="textAlignment">Where the human readable interpretation stands within the symbol width.</param>
    /// <param name="pitch">
    /// The pitch in millimetres that the specification fixes at every bar width, or <see langword="null"/> where the space scales with the bar width.
    /// </param>
    /// <param name="proportionalBars">
    /// Whether the specification makes the bar heights proportional to the bar width, rather than fixing them in millimetres or inches.
    /// </param>
    public FourStateMetrics(
        double xDimension,
        double space,
        float ascender,
        float tracker,
        float descender,
        float quietZone,
        float verticalQuietZone,
        BarcodeTextSide textSide,
        float textClearance,
        BarcodeTextAlignment textAlignment,
        double? pitch,
        bool proportionalBars)
    {
        this.ProportionalBars = proportionalBars;
        this.VerticalQuietZone = verticalQuietZone;
        this.XDimension = xDimension;
        this.Space = space;
        this.Pitch = pitch;
        this.Ascender = ascender;
        this.Tracker = tracker;
        this.Descender = descender;
        this.QuietZone = quietZone;
        this.TextSide = textSide;
        this.TextClearance = textClearance;
        this.TextAlignment = textAlignment;
    }

    /// <summary>
    /// Gets the bar width in millimetres, which is one module.
    /// </summary>
    public double XDimension { get; }

    /// <summary>
    /// Gets the smallest bar width in millimetres that the specification permits, or zero where it gives none. The code clamps the drawn bar width to
    /// this limit.
    /// </summary>
    public double MinimumXDimension { get; init; }

    /// <summary>
    /// Gets the largest bar width in millimetres that the specification permits, or <see cref="double.PositiveInfinity"/> where it gives none. The code clamps
    /// the drawn bar width to this limit.
    /// </summary>
    public double MaximumXDimension { get; init; } = double.PositiveInfinity;

    /// <summary>
    /// Gets the fewest bars per inch that the specification permits, or zero where it gives no limit. It sets the largest pitch, a bar and a space, that the
    /// symbol draws at in whole pixels.
    /// </summary>
    public double MinimumBarsPerInch { get; init; }

    /// <summary>
    /// Gets the most bars per inch that the specification permits, or <see cref="double.PositiveInfinity"/> where it gives no limit. It sets the smallest
    /// pitch, a bar and a space, that the symbol draws at in whole pixels.
    /// </summary>
    public double MaximumBarsPerInch { get; init; } = double.PositiveInfinity;

    /// <summary>
    /// Gets the smallest full bar in modules that the specification permits, or zero where it gives none. All bar parts scale with the full bar, so this is
    /// the smallest full bar that keeps every part at or over its own minimum.
    /// </summary>
    public float MinimumFullBar { get; init; }

    /// <summary>
    /// Gets the largest full bar in modules that the specification permits, or <see cref="float.PositiveInfinity"/> where it gives none. All bar parts scale
    /// with the full bar, so this is the largest full bar that keeps every part at or under its own maximum.
    /// </summary>
    public float MaximumFullBar { get; init; } = float.PositiveInfinity;

    /// <summary>
    /// Gets the width of the space between bars, in modules at <see cref="XDimension"/>.
    /// </summary>
    public double Space { get; }

    /// <summary>
    /// Gets the pitch, a bar and a space, in millimetres that the specification fixes at every bar width, or <see langword="null"/> where the space scales
    /// with the bar width.
    /// </summary>
    public double? Pitch { get; }

    /// <summary>
    /// Gets a value indicating whether the specification makes the bar heights proportional to the bar width. Where it does not, the heights keep their size
    /// in millimetres or inches when the bar width rounds to whole pixels.
    /// </summary>
    public bool ProportionalBars { get; }

    /// <summary>
    /// Gets the height of the ascender part above the tracker, in modules.
    /// </summary>
    public float Ascender { get; }

    /// <summary>
    /// Gets the height of the tracker, in modules.
    /// </summary>
    public float Tracker { get; }

    /// <summary>
    /// Gets the height of the descender part below the tracker, in modules.
    /// </summary>
    public float Descender { get; }

    /// <summary>
    /// Gets the clear zone at each end of the symbol, in modules.
    /// </summary>
    public float QuietZone { get; }

    /// <summary>
    /// Gets the side of the bars the human readable interpretation stands on.
    /// </summary>
    public BarcodeTextSide TextSide { get; }

    /// <summary>
    /// Gets the clear space between the bars and the human readable interpretation, in modules.
    /// </summary>
    public float TextClearance { get; }

    /// <summary>
    /// Gets where the human readable interpretation stands within the symbol width.
    /// </summary>
    public BarcodeTextAlignment TextAlignment { get; }

    /// <summary>
    /// Gets the clear zone above and below the symbol, in modules.
    /// </summary>
    public float VerticalQuietZone { get; }

    /// <summary>
    /// Gets the height of a full bar in modules.
    /// </summary>
    public float FullBar => this.Ascender + this.Tracker + this.Descender;

    /// <summary>
    /// Returns the width of the space between bars, in modules of the bar width the symbol draws at. Where <see cref="Pitch"/> is set, the bar width can
    /// differ from <see cref="XDimension"/> after rounding to whole pixels. The space is then the rest of the pitch, so the pitch does not move with the bar.
    /// </summary>
    /// <param name="xDimension">The bar width in millimetres the symbol draws at.</param>
    /// <returns>The space in modules.</returns>
    public double SpaceAt(double xDimension) => this.Pitch is double pitch ? (pitch / xDimension) - 1 : this.Space;
}
