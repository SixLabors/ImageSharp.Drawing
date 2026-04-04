// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;

namespace SixLabors.ImageSharp.Drawing;

/// <summary>
/// Represents a simple path segment
/// </summary>
public interface ILineSegment
{
    /// <summary>
    /// Gets the start point.
    /// </summary>
    public PointF StartPoint { get; }

    /// <summary>
    /// Gets the end point.
    /// </summary>
    /// <value>
    /// The end point.
    /// </value>
    public PointF EndPoint { get; }

    /// <summary>
    /// Gets the bounds of the linearized segment output.
    /// </summary>
    public RectangleF Bounds { get; }

    /// <summary>
    /// Gets the number of linear vertices emitted by this segment.
    /// </summary>
    public int LinearVertexCount { get; }

    /// <summary>
    /// Converts the <see cref="ILineSegment" /> into a simple linear path..
    /// </summary>
    /// <returns>Returns the current <see cref="ILineSegment" /> as simple linear path.</returns>
    public ReadOnlyMemory<PointF> Flatten();

    /// <summary>
    /// Copies the linearized point data to the destination span.
    /// </summary>
    /// <param name="destination">The destination point span.</param>
    /// <param name="skipFirstPoint">Whether to skip the first emitted point.</param>
    public void CopyTo(Span<PointF> destination, bool skipFirstPoint);

    /// <summary>
    /// Transforms the current LineSegment using specified matrix.
    /// </summary>
    /// <param name="matrix">The matrix.</param>
    /// <returns>A line segment with the matrix applied to it.</returns>
    public ILineSegment Transform(Matrix4x4 matrix);
}
