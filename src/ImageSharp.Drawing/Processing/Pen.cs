// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Processing;

/// <summary>
/// The base class for pens that can apply a pattern to a line with a set brush and thickness
/// </summary>
/// <remarks>
/// The pattern is expressed in the form
/// <code>
/// [1f, 2f, 0.5f]
/// </code>
/// this will be converted into a pattern that is 3.5 times longer than the width with 3 sections.
/// <list type="bullet">
/// <item>Section 1 will be width long (making a square) and will be filled by the brush.</item>
/// <item>Section 2 will be width * 2 long and will be empty.</item>
/// <item>Section 3 will be width/2 long and will be filled.</item>
/// </list>
/// The pattern will immediately repeat without gap.
/// </remarks>
public abstract class Pen : IEquatable<Pen>
{
    // Dash pattern segment lengths, expressed as multiples of the stroke width.
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
    /// <param name="strokeWidth">The stroke width in the path's local coordinate space before any drawing transform is applied.</param>
    protected Pen(Brush strokeFill, float strokeWidth)
        : this(strokeFill, strokeWidth, Pens.EmptyPattern)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Pen"/> class.
    /// </summary>
    /// <param name="strokeFill">The brush used to fill the stroke outline.</param>
    /// <param name="strokeWidth">The stroke width in the path's local coordinate space before any drawing transform is applied.</param>
    /// <param name="strokePattern">The stroke pattern.</param>
    protected Pen(Brush strokeFill, float strokeWidth, float[] strokePattern)
    {
        Guard.NotNull(strokeFill, nameof(strokeFill));

        Guard.MustBeGreaterThan(strokeWidth, 0, nameof(strokeWidth));
        Guard.NotNull(strokePattern, nameof(strokePattern));

        this.StrokeFill = strokeFill;
        this.StrokeWidth = strokeWidth;
        this.pattern = strokePattern;
        this.StrokePatternOffset = 0;
        this.StrokeOptions = new StrokeOptions();
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
        this.StrokePatternOffset = options.StrokePatternOffset;
        this.StrokeOptions = options.StrokeOptions ?? new StrokeOptions();
    }

    /// <inheritdoc cref="PenOptions.StrokeFill"/>
    public Brush StrokeFill { get; }

    /// <inheritdoc cref="PenOptions.StrokeWidth"/>
    public float StrokeWidth { get; }

    /// <inheritdoc cref="PenOptions.StrokePattern"/>
    public ReadOnlyMemory<float> StrokePattern => this.pattern;

    /// <inheritdoc cref="PenOptions.StrokePatternOffset"/>
    public float StrokePatternOffset { get; }

    /// <inheritdoc cref="PenOptions.StrokeOptions"/>
    public StrokeOptions StrokeOptions { get; }

    /// <summary>
    /// Applies the styling from the pen to a path and generates a new path with the final vector.
    /// </summary>
    /// <param name="path">The source path.</param>
    /// <returns>The <see cref="IPath"/> with the pen styling applied.</returns>
    public IPath GeneratePath(IPath path)
        => this.GeneratePath(path, this.StrokeWidth);

    /// <summary>
    /// Applies the styling from the pen to a path and generates a new path with the final vector.
    /// </summary>
    /// <param name="path">The source path.</param>
    /// <param name="strokeWidth">The stroke width in the path's local coordinate space before any drawing transform is applied.</param>
    /// <returns>The <see cref="IPath"/> with the pen styling applied.</returns>
    public abstract IPath GeneratePath(IPath path, float strokeWidth);

    /// <summary>
    /// Gets the largest distance from the centerline that the stroke outline can reach.
    /// </summary>
    /// <remarks>
    /// A miter apex reaches the miter limit m times the half width. A <see cref="LineJoin.Miter"/>
    /// join past the limit draws a clipped bevel, which reaches at most sqrt(m^2 + 1) half widths.
    /// At a reversal the bevel points are one half width out from the centerline and m half widths
    /// along it, which is that distance. The other miter joins draw a bevel or an arc past the
    /// limit. A square cap corner reaches
    /// sqrt(2) times the half width. Round and bevel joins and round and butt caps stay inside the
    /// half width.
    /// </remarks>
    /// <param name="widthScale">The transform-derived scale applied to the stroke width.</param>
    /// <returns>The distance in pixels.</returns>
    internal float GetOutlineRadius(float widthScale)
    {
        float halfWidth = this.StrokeWidth * widthScale * 0.5F;
        double miterLimit = Math.Max(this.StrokeOptions.MiterLimit, 1D);
        float joinRadius = this.StrokeOptions.LineJoin switch
        {
            LineJoin.Miter => (float)(halfWidth * Math.Sqrt((miterLimit * miterLimit) + 1D)),
            LineJoin.MiterRevert or LineJoin.MiterRound => (float)(halfWidth * miterLimit),
            _ => halfWidth
        };

        float capRadius = this.StrokeOptions.LineCap == LineCap.Square
            ? halfWidth * MathF.Sqrt(2F)
            : halfWidth;

        return MathF.Max(joinRadius, capRadius);
    }

    /// <inheritdoc/>
    public virtual bool Equals(Pen? other)
        => other != null
        && this.StrokeWidth == other.StrokeWidth
        && this.StrokeFill.Equals(other.StrokeFill)
        && this.StrokePatternOffset == other.StrokePatternOffset
        && this.StrokeOptions.Equals(other.StrokeOptions)
        && this.StrokePattern.Span.SequenceEqual(other.StrokePattern.Span);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => this.Equals(obj as Pen);

    /// <inheritdoc/>
    public override int GetHashCode()
        => HashCode.Combine(this.StrokeWidth, this.StrokeFill, this.StrokePatternOffset, this.StrokeOptions, this.pattern);
}
