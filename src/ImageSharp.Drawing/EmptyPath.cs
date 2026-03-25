// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;

namespace SixLabors.ImageSharp.Drawing;

/// <summary>
/// A path that is always empty.
/// </summary>
public sealed class EmptyPath : IPath
{
    private static readonly LinearGeometry EmptyGeometry = new(
        new LinearGeometryInfo
        {
            Bounds = RectangleF.Empty,
            ContourCount = 0,
            PointCount = 0,
            SegmentCount = 0,
            NonHorizontalSegmentCountPixelBoundary = 0,
            NonHorizontalSegmentCountPixelCenter = 0
        },
        [],
        []);

    private EmptyPath(PathTypes pathType) => this.PathType = pathType;

    /// <summary>
    /// Gets the closed path instance of the empty path
    /// </summary>
    public static EmptyPath ClosedPath { get; } = new(PathTypes.Closed);

    /// <summary>
    /// Gets the open path instance of the empty path
    /// </summary>
    public static EmptyPath OpenPath { get; } = new(PathTypes.Open);

    /// <inheritdoc />
    public PathTypes PathType { get; }

    /// <inheritdoc />
    public RectangleF Bounds => RectangleF.Empty;

    /// <inheritdoc />
    public IPath AsClosedPath() => ClosedPath;

    /// <inheritdoc />
    public IEnumerable<ISimplePath> Flatten() => [];

    /// <inheritdoc />
    public LinearGeometry ToLinearGeometry() => EmptyGeometry;

    /// <inheritdoc />
    public IPath Transform(Matrix4x4 matrix) => this;
}
