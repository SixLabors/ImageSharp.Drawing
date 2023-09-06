// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Processing;

/// <summary>
/// Defines a pen that can apply a pattern to a line with a set brush and thickness.
/// </summary>
public class SolidPen : Pen
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SolidPen"/> class.
    /// </summary>
    /// <param name="color">The color.</param>
    public SolidPen(Color color)
        : base(new SolidBrush(color))
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SolidPen"/> class.
    /// </summary>
    /// <param name="color">The color.</param>
    /// <param name="width">The width.</param>
    public SolidPen(Color color, float width)
        : base(new SolidBrush(color), width)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SolidPen"/> class.
    /// </summary>
    /// <param name="strokeFill">The brush used to fill the stroke outline.</param>
    public SolidPen(Brush strokeFill)
        : base(strokeFill)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SolidPen"/> class.
    /// </summary>
    /// <param name="strokeFill">The brush used to fill the stroke outline.</param>
    /// <param name="strokeWidth">The stroke width in px units.</param>
    public SolidPen(Brush strokeFill, float strokeWidth)
        : base(strokeFill, strokeWidth)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SolidPen"/> class.
    /// </summary>
    /// <param name="options">The pen options.</param>
    public SolidPen(PenOptions options)
        : base(options)
    {
    }

    /// <inheritdoc/>
    public override bool Equals(Pen? other)
    {
        if (other is SolidPen)
        {
            return base.Equals(other);
        }

        return false;
    }

    /// <inheritdoc />
    public override IPath GeneratePath(IPath path, float strokeWidth)
        => path.GenerateOutline(strokeWidth, this.JointStyle, this.EndCapStyle);
}
