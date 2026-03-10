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
        : base(segments.ToArray())
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

    /// <summary>
    /// Initializes a new instance of the <see cref="Polygon"/> class using the specified line segments.
    /// </summary>
    /// <remarks>
    /// If owned is set to <see langword="true"/>, modifications to the segments array after construction may affect
    /// the Polygon instance. If owned is <see langword="false"/>, the segments are copied to ensure the Polygon is not affected by
    /// external changes.
    /// </remarks>
    /// <param name="segments">An array of line segments that define the edges of the polygon. The order of segments determines the shape of
    /// the polygon.</param>
    /// <param name="owned">
    /// <see langword="true"/> to indicate that the Polygon instance takes ownership of the segments array;
    /// <see langword="false"/> to create a copy of the array.
    /// </param>
    internal Polygon(ILineSegment[] segments, bool owned)
        : base(owned ? segments : [.. segments])
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

        ILineSegment[] segments = new ILineSegment[this.LineSegments.Count];

        for (int i = 0; i < segments.Length; i++)
        {
            segments[i] = this.LineSegments[i].Transform(matrix);
        }

        return new Polygon(segments, true);
    }
}
