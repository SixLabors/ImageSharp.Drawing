// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Processing;

/// <summary>
/// Defines a pen that can apply a pattern to a line with a set brush and thickness
/// </summary>
/// <remarks>
/// The pattern will be in to the form of
/// <code>
/// new float[]{ 1f, 2f, 0.5f}
/// </code>
/// this will be converted into a pattern that is 3.5 times longer that the width with 3 sections.
/// <list type="bullet">
/// <item>Section 1 will be width long (making a square) and will be filled by the brush.</item>
/// <item>Section 2 will be width * 2 long and will be empty.</item>
/// <item>Section 3 will be width/2 long and will be filled.</item>
/// </list>
/// The pattern will immediately repeat without gap.
/// </remarks>
public class PatternPen : Pen
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PatternPen"/> class.
    /// </summary>
    /// <param name="color">The color.</param>
    /// <param name="strokePattern">The stroke pattern.</param>
    public PatternPen(Color color, float[] strokePattern)
        : base(new SolidBrush(color), 1, strokePattern)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PatternPen"/> class.
    /// </summary>
    /// <param name="color">The color.</param>
    /// <param name="strokeWidth">The stroke width in px units.</param>
    /// <param name="strokePattern">The stroke pattern.</param>
    public PatternPen(Color color, float strokeWidth, float[] strokePattern)
        : base(new SolidBrush(color), strokeWidth, strokePattern)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PatternPen"/> class.
    /// </summary>
    /// <param name="strokeFill">The brush used to fill the stroke outline.</param>
    /// <param name="strokeWidth">The stroke width in px units.</param>
    /// <param name="strokePattern">The stroke pattern.</param>
    public PatternPen(Brush strokeFill, float strokeWidth, float[] strokePattern)
        : base(strokeFill, strokeWidth, strokePattern)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PatternPen"/> class.
    /// </summary>
    /// <param name="options">The pen options.</param>
    public PatternPen(PenOptions options)
        : base(options)
    {
    }

    /// <inheritdoc/>
    public override bool Equals(Pen other)
    {
        if (other is PatternPen)
        {
            return base.Equals(other);
        }

        return false;
    }

    /// <inheritdoc />
    public override IPath GeneratePath(IPath path, float strokeWidth)
        => path.GenerateOutline(strokeWidth, this.StrokePattern, this.JointStyle, this.EndCapStyle);
}
