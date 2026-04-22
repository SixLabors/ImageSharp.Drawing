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
    /// Returns the number of linear vertices emitted by this segment when flattened under the supplied
    /// device-space <paramref name="scale"/>.
    /// </summary>
    /// <param name="scale">The X/Y scale at which curves are flattened. Pass <see cref="Vector2.One"/> for local-space counts.</param>
    /// <returns>The number of linear vertices this segment emits.</returns>
    public int LinearVertexCount(Vector2 scale);

    /// <summary>
    /// Writes the segment's linearized points to <paramref name="destination"/>, baked at the supplied
    /// device-space <paramref name="scale"/>.
    /// </summary>
    /// <param name="destination">The destination point span.</param>
    /// <param name="skipFirstPoint">Whether to skip the first emitted point.</param>
    /// <param name="scale">The X/Y scale at which curves are flattened. Pass <see cref="Vector2.One"/> for local-space output.</param>
    public void CopyTo(Span<PointF> destination, bool skipFirstPoint, Vector2 scale);

    /// <summary>
    /// Transforms the current LineSegment using specified matrix.
    /// </summary>
    /// <param name="matrix">The matrix.</param>
    /// <returns>A line segment with the matrix applied to it.</returns>
    public ILineSegment Transform(Matrix4x4 matrix);
}
