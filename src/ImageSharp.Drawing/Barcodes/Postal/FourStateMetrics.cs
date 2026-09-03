// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The dimensions of a height modulated postal symbology, in modules of its bar width and in run units
/// for the pitch, which is not a whole number of bar widths in any of them.
/// </summary>
internal readonly struct FourStateMetrics
{
    /// <summary>
    /// Initializes a new instance of the <see cref="FourStateMetrics"/> struct.
    /// </summary>
    /// <param name="xDimension">The bar width in millimetres, which is one module.</param>
    /// <param name="runUnit">The width of one run unit in modules.</param>
    /// <param name="barUnits">The width of a bar in run units.</param>
    /// <param name="spaceUnits">The width of the space between bars in run units.</param>
    /// <param name="ascender">The height of the ascender part above the tracker, in modules.</param>
    /// <param name="tracker">The height of the tracker, in modules.</param>
    /// <param name="descender">The height of the descender part below the tracker, in modules.</param>
    /// <param name="quietZone">The clear zone at each end of the symbol, in modules.</param>
    public FourStateMetrics(float xDimension, float runUnit, int barUnits, int spaceUnits, float ascender, float tracker, float descender, float quietZone)
        : this(xDimension, runUnit, barUnits, spaceUnits, ascender, tracker, descender, quietZone, BarcodeTextSide.BelowBars, 0F)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="FourStateMetrics"/> struct.
    /// </summary>
    /// <param name="xDimension">The bar width in millimetres, which is one module.</param>
    /// <param name="runUnit">The width of one run unit in modules.</param>
    /// <param name="barUnits">The width of a bar in run units.</param>
    /// <param name="spaceUnits">The width of the space between bars in run units.</param>
    /// <param name="ascender">The height of the ascender part above the tracker, in modules.</param>
    /// <param name="tracker">The height of the tracker, in modules.</param>
    /// <param name="descender">The height of the descender part below the tracker, in modules.</param>
    /// <param name="quietZone">The clear zone at each end of the symbol, in modules.</param>
    /// <param name="textSide">The side of the bars the human readable interpretation stands on.</param>
    /// <param name="textClearance">The clear space between the bars and the human readable interpretation, in modules.</param>
    public FourStateMetrics(float xDimension, float runUnit, int barUnits, int spaceUnits, float ascender, float tracker, float descender, float quietZone, BarcodeTextSide textSide, float textClearance)
        : this(xDimension, runUnit, barUnits, spaceUnits, ascender, tracker, descender, quietZone, textSide, textClearance, BarcodeTextAlignment.Centered)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="FourStateMetrics"/> struct.
    /// </summary>
    /// <param name="xDimension">The bar width in millimetres, which is one module.</param>
    /// <param name="runUnit">The width of one run unit in modules.</param>
    /// <param name="barUnits">The width of a bar in run units.</param>
    /// <param name="spaceUnits">The width of the space between bars in run units.</param>
    /// <param name="ascender">The height of the ascender part above the tracker, in modules.</param>
    /// <param name="tracker">The height of the tracker, in modules.</param>
    /// <param name="descender">The height of the descender part below the tracker, in modules.</param>
    /// <param name="quietZone">The clear zone at each end of the symbol, in modules.</param>
    /// <param name="textSide">The side of the bars the human readable interpretation stands on.</param>
    /// <param name="textClearance">The clear space between the bars and the human readable interpretation, in modules.</param>
    /// <param name="textAlignment">Where the human readable interpretation stands within the symbol width.</param>
    public FourStateMetrics(float xDimension, float runUnit, int barUnits, int spaceUnits, float ascender, float tracker, float descender, float quietZone, BarcodeTextSide textSide, float textClearance, BarcodeTextAlignment textAlignment)
    {
        this.XDimension = xDimension;
        this.RunUnit = runUnit;
        this.BarUnits = barUnits;
        this.SpaceUnits = spaceUnits;
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
    public float XDimension { get; }

    /// <summary>
    /// Gets the width of one run unit in modules.
    /// </summary>
    public float RunUnit { get; }

    /// <summary>
    /// Gets the width of a bar in run units.
    /// </summary>
    public int BarUnits { get; }

    /// <summary>
    /// Gets the width of the space between bars in run units.
    /// </summary>
    public int SpaceUnits { get; }

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
    /// Gets the height of a full bar in modules.
    /// </summary>
    public float FullBar => this.Ascender + this.Tracker + this.Descender;
}
