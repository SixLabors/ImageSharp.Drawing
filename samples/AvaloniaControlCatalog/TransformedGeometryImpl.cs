// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using Avalonia;
using Avalonia.Media;
using Avalonia.Platform;
using SixLabors.ImageSharp.Drawing;

namespace AvaloniaControlCatalog;

/// <summary>
/// Geometry wrapper that preserves the original source geometry and applied transform.
/// </summary>
internal sealed class TransformedGeometryImpl : GeometryImpl, ITransformedGeometryImpl
{
    /// <summary>
    /// Initializes a new transformed geometry wrapper.
    /// </summary>
    /// <param name="source">The untransformed source geometry.</param>
    /// <param name="transform">The transform applied to <paramref name="source"/>.</param>
    public TransformedGeometryImpl(GeometryImpl source, Matrix transform)
        : this(source, transform, transform.ToMatrix4x4())
    {
    }

    private TransformedGeometryImpl(GeometryImpl source, Matrix transform, Matrix4x4 matrix)
        : this(source, transform, matrix, source.StrokePath.Transform(matrix))
    {
    }

    private TransformedGeometryImpl(GeometryImpl source, Matrix transform, Matrix4x4 matrix, IPath strokePath)
        : base(
            strokePath,
            ReferenceEquals(source.StrokePath, source.FillPath) ? strokePath : source.FillPath?.Transform(matrix),
            source.FillRule)
    {
        this.SourceGeometry = source;
        this.Transform = transform;
    }

    /// <inheritdoc />
    public IGeometryImpl SourceGeometry { get; }

    /// <inheritdoc />
    public Matrix Transform { get; }
}
