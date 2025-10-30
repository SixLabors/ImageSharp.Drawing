// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Processing;

/// <summary>
/// The base class for pens that can apply a pattern to a line with a set brush and thickness
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
public abstract class Pen : IEquatable<Pen>
{
    private readonly float[] pattern;

    /// <summary>
    /// Initializes a new instance of the <see cref="Pen"/> class.
    /// </summary>
    /// <param name="strokeFill">The brush used to fill the stroke outline.</param>
    protected Pen(Brush strokeFill)
        : this(strokeFill, 1)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Pen"/> class.
    /// </summary>
    /// <param name="strokeFill">The brush used to fill the stroke outline.</param>
    /// <param name="strokeWidth">The stroke width in px units.</param>
    protected Pen(Brush strokeFill, float strokeWidth)
        : this(strokeFill, strokeWidth, Pens.EmptyPattern)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Pen"/> class.
    /// </summary>
    /// <param name="strokeFill">The brush used to fill the stroke outline.</param>
    /// <param name="strokeWidth">The stroke width in px units.</param>
    /// <param name="strokePattern">The stroke pattern.</param>
    protected Pen(Brush strokeFill, float strokeWidth, float[] strokePattern)
    {
        Guard.NotNull(strokeFill, nameof(strokeFill));

        Guard.MustBeGreaterThan(strokeWidth, 0, nameof(strokeWidth));
        Guard.NotNull(strokePattern, nameof(strokePattern));

        this.StrokeFill = strokeFill;
        this.StrokeWidth = strokeWidth;
        this.pattern = strokePattern;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Pen"/> class.
    /// </summary>
    /// <param name="options">The pen options.</param>
    protected Pen(PenOptions options)
    {
        this.StrokeFill = options.StrokeFill;
        this.StrokeWidth = options.StrokeWidth;
        this.pattern = options.StrokePattern;
        this.JointStyle = options.JointStyle;
        this.EndCapStyle = options.EndCapStyle;
    }

    /// <inheritdoc cref="PenOptions.StrokeFill"/>
    public Brush StrokeFill { get; }

    /// <inheritdoc cref="PenOptions.StrokeWidth"/>
    public float StrokeWidth { get; }

    /// <inheritdoc cref="PenOptions.StrokePattern"/>
    public ReadOnlySpan<float> StrokePattern => this.pattern;

    /// <inheritdoc cref="PenOptions.JointStyle"/>
    public JointStyle JointStyle { get; }

    /// <inheritdoc cref="PenOptions.EndCapStyle"/>
    public EndCapStyle EndCapStyle { get; }

    /// <summary>
    /// Applies the styling from the pen to a path and generate a new path with the final vector.
    /// </summary>
    /// <param name="path">The source path</param>
    /// <returns>The <see cref="IPath"/> with the pen styling applied.</returns>
    public IPath GeneratePath(IPath path)
        => this.GeneratePath(path, this.StrokeWidth);

    /// <summary>
    /// Applies the styling from the pen to a path and generate a new path with the final vector.
    /// </summary>
    /// <param name="path">The source path</param>
    /// <param name="strokeWidth">The stroke width in px units.</param>
    /// <returns>The <see cref="IPath"/> with the pen styling applied.</returns>
    public abstract IPath GeneratePath(IPath path, float strokeWidth);

    /// <inheritdoc/>
    public virtual bool Equals(Pen? other)
        => other != null
        && this.StrokeWidth == other.StrokeWidth
        && this.JointStyle == other.JointStyle
        && this.EndCapStyle == other.EndCapStyle
        && this.StrokeFill.Equals(other.StrokeFill)
        && this.StrokePattern.SequenceEqual(other.StrokePattern);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => this.Equals(obj as Pen);

    /// <inheritdoc/>
    public override int GetHashCode()
        => HashCode.Combine(this.StrokeWidth, this.JointStyle, this.EndCapStyle, this.StrokeFill, this.pattern);
}
