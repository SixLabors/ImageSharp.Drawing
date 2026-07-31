// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Processing;

/// <summary>
/// Provides a set of configurations options for pens.
/// </summary>
public struct PenOptions
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PenOptions"/> struct.
    /// </summary>
    /// <param name="strokeWidth">The stroke width in the path's local coordinate space before any drawing transform is applied.</param>
    public PenOptions(float strokeWidth)
        : this(Color.Black, strokeWidth)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PenOptions"/> struct.
    /// </summary>
    /// <param name="color">The color.</param>
    /// <param name="strokeWidth">The stroke width in the path's local coordinate space before any drawing transform is applied.</param>
    public PenOptions(Color color, float strokeWidth)
        : this(color, strokeWidth, null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PenOptions"/> struct.
    /// </summary>
    /// <param name="color">The color.</param>
    /// <param name="strokeWidth">The stroke width in the path's local coordinate space before any drawing transform is applied.</param>
    /// <param name="strokePattern">The stroke pattern.</param>
    public PenOptions(Color color, float strokeWidth, float[]? strokePattern)
        : this(new SolidBrush(color), strokeWidth, strokePattern)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PenOptions"/> struct.
    /// </summary>
    /// <param name="color">The color.</param>
    /// <param name="strokeWidth">The stroke width in the path's local coordinate space before any drawing transform is applied.</param>
    /// <param name="strokePattern">The stroke pattern.</param>
    /// <param name="strokePatternOffset">The distance into the stroke pattern, expressed as a multiple of <paramref name="strokeWidth"/>.</param>
    public PenOptions(Color color, float strokeWidth, float[] strokePattern, float strokePatternOffset)
        : this(new SolidBrush(color), strokeWidth, strokePattern, strokePatternOffset)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PenOptions"/> struct.
    /// </summary>
    /// <param name="strokeFill">The brush used to fill the stroke outline.</param>
    /// <param name="strokeWidth">The stroke width in the path's local coordinate space before any drawing transform is applied.</param>
    /// <param name="strokePattern">The stroke pattern.</param>
    public PenOptions(Brush strokeFill, float strokeWidth, float[]? strokePattern)
    {
        Guard.MustBeGreaterThan(strokeWidth, 0, nameof(strokeWidth));

        this.StrokeFill = strokeFill;
        this.StrokeWidth = strokeWidth;
        this.StrokePattern = strokePattern ?? Pens.EmptyPattern;
        this.StrokePatternOffset = 0;
        this.StrokeOptions = new StrokeOptions();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PenOptions"/> struct.
    /// </summary>
    /// <param name="strokeFill">The brush used to fill the stroke outline.</param>
    /// <param name="strokeWidth">The stroke width in the path's local coordinate space before any drawing transform is applied.</param>
    /// <param name="strokePattern">The stroke pattern.</param>
    /// <param name="strokePatternOffset">The distance into the stroke pattern, expressed as a multiple of <paramref name="strokeWidth"/>.</param>
    public PenOptions(Brush strokeFill, float strokeWidth, float[] strokePattern, float strokePatternOffset)
        : this(strokeFill, strokeWidth, strokePattern)
    {
        this.StrokePatternOffset = strokePatternOffset;
    }

    /// <summary>
    /// Gets the brush used to fill the stroke outline. Defaults to <see cref="SolidBrush"/>.
    /// </summary>
    public Brush StrokeFill { get; }

    /// <summary>
    /// Gets the stroke width in the path's local coordinate space before any drawing transform is applied. Defaults to 1.
    /// </summary>
    public float StrokeWidth { get; }

    /// <summary>
    /// Gets the stroke pattern: alternating filled and empty segment lengths expressed as
    /// multiples of <see cref="StrokeWidth"/>, starting with a filled segment.
    /// An empty pattern produces a continuous stroke.
    /// </summary>
    public float[] StrokePattern { get; }

    /// <summary>
    /// Gets or sets the distance into the stroke pattern, expressed as a multiple of <see cref="StrokeWidth"/>.
    /// </summary>
    public float StrokePatternOffset { get; set; }

    /// <summary>
    /// Gets or sets the stroke geometry options used to stroke paths drawn with this pen.
    /// </summary>
    public StrokeOptions? StrokeOptions { get; set; }
}
