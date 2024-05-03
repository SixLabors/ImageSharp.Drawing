// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;

namespace SixLabors.ImageSharp.Drawing;

/// <summary>
/// Represents a complex polygon made up of one or more shapes overlayed on each other,
/// where overlaps causes holes.
/// </summary>
/// <seealso cref="IPath" />
public sealed class ComplexPolygon : IPath, IPathInternals, IInternalPathOwner
{
    private readonly IPath[] paths;
    private List<InternalPath>? internalPaths;
    private float length;
    private RectangleF? bounds;

    /// <summary>
    /// Initializes a new instance of the <see cref="ComplexPolygon"/> class.
    /// </summary>
    /// <param name="contour">The contour path.</param>
    /// <param name="hole">The hole path.</param>
    public ComplexPolygon(PointF[] contour, PointF[] hole)
        : this(new Path(new LinearLineSegment(contour)), new Path(new LinearLineSegment(hole)))
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ComplexPolygon" /> class.
    /// </summary>
    /// <param name="paths">The paths.</param>
    public ComplexPolygon(IEnumerable<IPath> paths)
        : this(paths.ToArray())
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ComplexPolygon" /> class.
    /// </summary>
    /// <param name="paths">The paths.</param>
    public ComplexPolygon(params IPath[] paths)
    {
        Guard.NotNull(paths, nameof(paths));

        this.paths = paths;

        if (paths.Length == 0)
        {
            this.bounds = RectangleF.Empty;
        }

        this.PathType = PathTypes.Mixed;
    }

    /// <inheritdoc/>
    public PathTypes PathType { get; }

    /// <summary>
    /// Gets the collection of paths that make up this shape.
    /// </summary>
    public IEnumerable<IPath> Paths => this.paths;

    /// <inheritdoc/>
    public RectangleF Bounds => this.bounds ??= this.CalcBounds();

    /// <inheritdoc/>
    public IPath Transform(Matrix3x2 matrix)
    {
        if (matrix.IsIdentity)
        {
            // No transform to apply skip it
            return this;
        }

        IPath[] shapes = new IPath[this.paths.Length];

        for (int i = 0; i < shapes.Length; i++)
        {
            shapes[i] = this.paths[i].Transform(matrix);
        }

        return new ComplexPolygon(shapes);
    }

    /// <inheritdoc />
    public IEnumerable<ISimplePath> Flatten()
    {
        List<ISimplePath> paths = new();
        foreach (IPath path in this.Paths)
        {
            paths.AddRange(path.Flatten());
        }

        return paths.ToArray();
    }

    /// <inheritdoc/>
    public IPath AsClosedPath()
    {
        if (this.PathType == PathTypes.Closed)
        {
            return this;
        }

        IPath[] paths = new IPath[this.paths.Length];
        for (int i = 0; i < this.paths.Length; i++)
        {
            paths[i] = this.paths[i].AsClosedPath();
        }

        return new ComplexPolygon(paths);
    }

    /// <inheritdoc/>
    SegmentInfo IPathInternals.PointAlongPath(float distance)
    {
        if (this.internalPaths == null)
        {
            this.InitInternalPaths();
        }

        distance %= this.length;
        foreach (InternalPath p in this.internalPaths)
        {
            if (p.Length >= distance)
            {
                return p.PointAlongPath(distance);
            }

            // Reduce it before trying the next path
            distance -= p.Length;
        }

        ThrowOutOfRange();
        return default;
    }

    /// <inheritdoc/>
    IReadOnlyList<InternalPath> IInternalPathOwner.GetRingsAsInternalPath()
    {
        this.InitInternalPaths();
        return this.internalPaths;
    }

    /// <summary>
    /// Initializes <see cref="internalPaths"/> and <see cref="length"/>.
    /// </summary>
    [MemberNotNull(nameof(internalPaths))]
    private void InitInternalPaths()
    {
        this.internalPaths = new List<InternalPath>(this.paths.Length);

        foreach (IPath p in this.paths)
        {
            foreach (ISimplePath s in p.Flatten())
            {
                InternalPath ip = new(s.Points, s.IsClosed);
                this.length += ip.Length;
                this.internalPaths.Add(ip);
            }
        }
    }

    private RectangleF CalcBounds()
    {
        float minX = float.MaxValue;
        float maxX = float.MinValue;
        float minY = float.MaxValue;
        float maxY = float.MinValue;

        foreach (IPath p in this.paths)
        {
            RectangleF pBounds = p.Bounds;

            minX = MathF.Min(minX, pBounds.Left);
            maxX = MathF.Max(maxX, pBounds.Right);
            minY = MathF.Min(minY, pBounds.Top);
            maxY = MathF.Max(maxY, pBounds.Bottom);
        }

        return new RectangleF(minX, minY, maxX - minX, maxY - minY);
    }

    private static InvalidOperationException ThrowOutOfRange() => new("Should not be possible to reach this line");
}
