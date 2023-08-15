// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;

namespace SixLabors.ImageSharp.Drawing;

/// <summary>
/// A shape made up of a single closed path made up of one of more <see cref="ILineSegment"/>s
/// </summary>
public class Polygon : Path
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Polygon"/> class.
    /// </summary>
    /// <param name="points">The collection of points; processed as a series of linear line segments.</param>
    public Polygon(PointF[] points)
        : this(new LinearLineSegment(points))
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Polygon"/> class.
    /// </summary>
    /// <param name="segments">The segments.</param>
    public Polygon(params ILineSegment[] segments)
        : base((IEnumerable<ILineSegment>)segments)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Polygon"/> class.
    /// </summary>
    /// <param name="segments">The segments.</param>
    public Polygon(IEnumerable<ILineSegment> segments)
        : base(segments)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Polygon" /> class.
    /// </summary>
    /// <param name="segment">The segment.</param>
    public Polygon(ILineSegment segment)
        : base(segment)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Polygon"/> class.
    /// </summary>
    /// <param name="path">The path.</param>
    internal Polygon(Path path)
        : base(path)
    {
    }

    /// <inheritdoc />
    public override bool IsClosed => true;

    /// <inheritdoc />
    public override IPath Transform(Matrix3x2 matrix)
    {
        if (matrix.IsIdentity)
        {
            return this;
        }

        var segments = new ILineSegment[this.LineSegments.Count];
        int i = 0;
        foreach (ILineSegment s in this.LineSegments)
        {
            segments[i++] = s.Transform(matrix);
        }

        return new Polygon(segments);
    }
}
