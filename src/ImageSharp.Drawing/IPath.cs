// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;

namespace SixLabors.ImageSharp.Drawing;

/// <summary>
/// Represents a logic path that can be drawn.
/// </summary>
public interface IPath
{
    /// <summary>
    /// Gets a value indicating whether this instance is closed, open or a composite path with a mixture of open and closed figures.
    /// </summary>
    public PathTypes PathType { get; }

    /// <summary>
    /// Gets the bounds enclosing the path.
    /// </summary>
    public RectangleF Bounds { get; }

    /// <summary>
    /// Converts the <see cref="IPath" /> into a simple linear path.
    /// </summary>
    /// <returns>Returns the current <see cref="IPath" /> as simple linear path.</returns>
    public IEnumerable<ISimplePath> Flatten();

    /// <summary>
    /// Converts this path into a retained <see cref="LinearGeometry"/>, flattening curves at the precision of
    /// the supplied device-space <paramref name="scale"/>.
    /// </summary>
    /// <param name="scale">The X/Y scale at which curves are flattened.</param>
    /// <returns>The retained linear geometry.</returns>
    public LinearGeometry ToLinearGeometry(Vector2 scale);

    /// <summary>
    /// Calculates the path length after flattening curves at the supplied scale.
    /// </summary>
    /// <param name="scale">The X/Y scale at which curves are flattened for length measurement.</param>
    /// <returns>The path length.</returns>
    public float ComputeLength(Vector2 scale);

    /// <summary>
    /// Calculates the non-negative path area after flattening curves at the supplied scale.
    /// </summary>
    /// <param name="scale">The X/Y scale at which curves are flattened for area measurement.</param>
    /// <returns>The path area.</returns>
    public float ComputeArea(Vector2 scale);

    /// <summary>
    /// Returns whether the supplied point is inside the path.
    /// </summary>
    /// <param name="point">The point to test.</param>
    /// <param name="intersectionRule">The fill rule used for containment.</param>
    /// <param name="scale">The X/Y scale at which curves are flattened for the containment test.</param>
    /// <returns><see langword="true"/> when the point is inside or on the path boundary.</returns>
    public bool Contains(PointF point, IntersectionRule intersectionRule, Vector2 scale);

    /// <summary>
    /// Gets path information at the specified distance along the path.
    /// </summary>
    /// <param name="distance">The distance along the path.</param>
    /// <param name="scale">The X/Y scale at which curves are flattened for distance measurement.</param>
    /// <param name="pathPoint">When this method returns, contains the path information at <paramref name="distance"/> if the distance resolves to a point on the path; otherwise, the default value.</param>
    /// <returns>
    /// <see langword="true"/> if <paramref name="distance"/> resolves to a point on the path;
    /// otherwise, <see langword="false"/>.
    /// </returns>
    public bool TryGetPathPointAtDistance(float distance, Vector2 scale, out PathPoint pathPoint);

    /// <summary>
    /// Creates a path segment between two distances along the path.
    /// </summary>
    /// <param name="startDistance">The segment start distance.</param>
    /// <param name="stopDistance">The segment stop distance.</param>
    /// <param name="startOnBeginFigure">Whether the returned segment starts a new figure at the first segment point.</param>
    /// <param name="scale">The X/Y scale at which curves are flattened for segment extraction.</param>
    /// <param name="path">When this method returns, contains the segment path if one was created; otherwise, an empty path.</param>
    /// <returns><see langword="true"/> when a segment path was created.</returns>
    public bool TryGetSegment(float startDistance, float stopDistance, bool startOnBeginFigure, Vector2 scale, out IPath path);

    /// <summary>
    /// Transforms the path using the specified matrix.
    /// </summary>
    /// <param name="matrix">The matrix.</param>
    /// <returns>A new path with the matrix applied to it.</returns>
    public IPath Transform(Matrix4x4 matrix);

    /// <summary>
    /// Returns this path with all figures closed.
    /// </summary>
    /// <returns>A new close <see cref="IPath"/>.</returns>
    public IPath AsClosedPath();
}
