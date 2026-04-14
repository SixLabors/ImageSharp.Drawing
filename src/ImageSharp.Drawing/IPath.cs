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
    /// Converts this path to a retained linear-geometry representation, applying the specified
    /// projective transform to each point during flattening.
    /// </summary>
    /// <param name="transform">The transform to apply to each point.</param>
    /// <remarks>
    /// <para>
    /// The returned <see cref="LinearGeometry"/> is the canonical lowered representation for backend scene building.
    /// It contains the point storage, contour metadata, and derived segment metadata needed to traverse the final
    /// linearized geometry without repeatedly rediscovering it through <see cref="Flatten()"/>.
    /// </para>
    /// <para>
    /// Closed contours are represented by contour metadata rather than by repeating the first point at the end of the
    /// stored point run.
    /// </para>
    /// <para>
    /// The returned geometry is expected to preserve the already-sanitized path shape. This method does not introduce
    /// additional geometric simplification policy beyond the mechanical stitching required by the segment emission
    /// contract.
    /// </para>
    /// </remarks>
    /// <returns>The retained linear geometry with transformed points.</returns>
    public LinearGeometry ToLinearGeometry(Matrix4x4 transform);

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
