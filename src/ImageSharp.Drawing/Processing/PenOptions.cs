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
    /// <param name="strokeWidth">The stroke width in px units.</param>
    public PenOptions(float strokeWidth)
        : this(Color.Black, strokeWidth)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PenOptions"/> struct.
    /// </summary>
    /// <param name="color">The color.</param>
    /// <param name="strokeWidth">The stroke width in px units.</param>
    public PenOptions(Color color, float strokeWidth)
        : this(color, strokeWidth, null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PenOptions"/> struct.
    /// </summary>
    /// <param name="color">The color.</param>
    /// <param name="strokeWidth">The stroke width in px units.</param>
    /// <param name="strokePattern">The stroke pattern.</param>
    public PenOptions(Color color, float strokeWidth, float[]? strokePattern)
        : this(new SolidBrush(color), strokeWidth, strokePattern)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PenOptions"/> struct.
    /// </summary>
    /// <param name="strokeFill">The brush used to fill the stroke outline.</param>
    /// <param name="strokeWidth">The stroke width in px units.</param>
    /// <param name="strokePattern">The stroke pattern.</param>
    public PenOptions(Brush strokeFill, float strokeWidth, float[]? strokePattern)
    {
        Guard.MustBeGreaterThan(strokeWidth, 0, nameof(strokeWidth));

        this.StrokeFill = strokeFill;
        this.StrokeWidth = strokeWidth;
        this.StrokePattern = strokePattern ?? Pens.EmptyPattern;
        this.JointStyle = JointStyle.Square;
        this.EndCapStyle = EndCapStyle.Butt;
    }

    /// <summary>
    /// Gets the brush used to fill the stroke outline. Defaults to <see cref="SolidBrush"/>.
    /// </summary>
    public Brush StrokeFill { get; }

    /// <summary>
    /// Gets the stroke width in px units. Defaults to 1px.
    /// </summary>
    public float StrokeWidth { get; }

    /// <summary>
    /// Gets the stroke pattern.
    /// </summary>
    public float[] StrokePattern { get; }

    /// <summary>
    /// Gets or sets the joint style.
    /// </summary>
    public JointStyle JointStyle { get; set; }

    /// <summary>
    /// Gets or sets the end cap style.
    /// </summary>
    public EndCapStyle EndCapStyle { get; set; }
}
