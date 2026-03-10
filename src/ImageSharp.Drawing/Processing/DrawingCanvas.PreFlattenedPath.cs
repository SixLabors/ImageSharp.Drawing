// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;

namespace SixLabors.ImageSharp.Drawing.Processing;

/// <content>
/// Contains private pre-flattened path types used by the canvas to avoid redundant curve subdivision.
/// </content>
public sealed partial class DrawingCanvas<TPixel>
{
    /// <summary>
    /// A lightweight <see cref="IPath"/> wrapper around pre-flattened points.
    /// <see cref="Flatten"/> returns <c>this</c> directly, avoiding redundant curve subdivision.
    /// Points are mutated in place on <see cref="Transform"/>; no buffers are copied.
    /// </summary>
    private sealed class PreFlattenedPath : IPath, ISimplePath
    {
        private readonly PointF[] points;
        private readonly bool isClosed;
        private RectangleF bounds;

        public PreFlattenedPath(PointF[] points, bool isClosed, RectangleF bounds)
        {
            this.points = points;
            this.isClosed = isClosed;
            this.bounds = bounds;
        }

        /// <inheritdoc />
        public RectangleF Bounds => this.bounds;

        /// <inheritdoc />
        public PathTypes PathType => this.isClosed ? PathTypes.Closed : PathTypes.Open;

        /// <inheritdoc />
        bool ISimplePath.IsClosed => this.isClosed;

        /// <inheritdoc />
        ReadOnlyMemory<PointF> ISimplePath.Points => this.points;

        /// <inheritdoc />
        public IEnumerable<ISimplePath> Flatten()
        {
            yield return this;
        }

        /// <summary>
        /// Transforms all points in place and updates the bounds.
        /// This mutates the current instance — the point buffer is not copied.
        /// </summary>
        /// <param name="matrix">The transform matrix.</param>
        /// <returns>This instance, with points and bounds updated.</returns>
        public IPath Transform(Matrix4x4 matrix)
        {
            if (matrix.IsIdentity)
            {
                return this;
            }

            float minX = float.MaxValue, minY = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue;

            for (int i = 0; i < this.points.Length; i++)
            {
                ref PointF p = ref this.points[i];
                p = PointF.Transform(p, matrix);

                if (p.X < minX)
                {
                    minX = p.X;
                }

                if (p.Y < minY)
                {
                    minY = p.Y;
                }

                if (p.X > maxX)
                {
                    maxX = p.X;
                }

                if (p.Y > maxY)
                {
                    maxY = p.Y;
                }
            }

            this.bounds = new RectangleF(minX, minY, maxX - minX, maxY - minY);
            return this;
        }

        /// <inheritdoc />
        public IPath AsClosedPath()
            => this.isClosed ? this : new PreFlattenedPath(this.points, true, this.bounds);
    }

    /// <summary>
    /// A lightweight <see cref="IPath"/> wrapper around multiple pre-flattened sub-paths.
    /// <see cref="Flatten"/> yields each sub-path directly, avoiding redundant curve subdivision.
    /// Sub-path points are mutated in place on <see cref="Transform"/>; no buffers are copied.
    /// </summary>
    private sealed class PreFlattenedCompositePath : IPath
    {
        private readonly PreFlattenedPath[] subPaths;
        private RectangleF bounds;

        public PreFlattenedCompositePath(PreFlattenedPath[] subPaths, RectangleF bounds)
        {
            this.subPaths = subPaths;
            this.bounds = bounds;
        }

        /// <inheritdoc />
        public RectangleF Bounds => this.bounds;

        /// <inheritdoc />
        public PathTypes PathType
        {
            get
            {
                bool hasOpen = false;
                bool hasClosed = false;
                foreach (PreFlattenedPath sp in this.subPaths)
                {
                    if (sp.PathType == PathTypes.Open)
                    {
                        hasOpen = true;
                    }
                    else
                    {
                        hasClosed = true;
                    }

                    if (hasOpen && hasClosed)
                    {
                        return PathTypes.Mixed;
                    }
                }

                return hasClosed ? PathTypes.Closed : PathTypes.Open;
            }
        }

        /// <inheritdoc />
        public IEnumerable<ISimplePath> Flatten() => this.subPaths;

        /// <summary>
        /// Transforms all sub-path points in place and updates the bounds.
        /// This mutates the current instance — no buffers are copied.
        /// </summary>
        /// <param name="matrix">The transform matrix.</param>
        /// <returns>This instance, with all sub-paths and bounds updated.</returns>
        public IPath Transform(Matrix4x4 matrix)
        {
            if (matrix.IsIdentity)
            {
                return this;
            }

            float minX = float.MaxValue, minY = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue;

            for (int i = 0; i < this.subPaths.Length; i++)
            {
                this.subPaths[i].Transform(matrix);
                RectangleF spBounds = this.subPaths[i].Bounds;

                if (spBounds.Left < minX)
                {
                    minX = spBounds.Left;
                }

                if (spBounds.Top < minY)
                {
                    minY = spBounds.Top;
                }

                if (spBounds.Right > maxX)
                {
                    maxX = spBounds.Right;
                }

                if (spBounds.Bottom > maxY)
                {
                    maxY = spBounds.Bottom;
                }
            }

            this.bounds = new RectangleF(minX, minY, maxX - minX, maxY - minY);
            return this;
        }

        /// <inheritdoc />
        public IPath AsClosedPath()
        {
            if (this.PathType == PathTypes.Closed)
            {
                return this;
            }

            PreFlattenedPath[] closed = new PreFlattenedPath[this.subPaths.Length];
            for (int i = 0; i < this.subPaths.Length; i++)
            {
                closed[i] = (PreFlattenedPath)this.subPaths[i].AsClosedPath();
            }

            return new PreFlattenedCompositePath(closed, this.bounds);
        }
    }
}
