/*
 * NOTE : this file is note required to draw shapes with imagesharp in production
 * just reference ImageSharp.Drawing.Paths it already has all the mappings required.
 * */


using System;
using System.Buffers;
using System.Collections.Immutable;
using System.Numerics;
using ImageSharp.Drawing;

namespace SixLabors.Shapes.DrawShapesWithImageSharp
{
    /// <summary>
    /// A drawable mapping between a <see cref="SixLabors.Shapes.IShape"/>/<see cref="SixLabors.Shapes.IPath"/> and a drawable/fillable region.
    /// </summary>
    internal class ShapePath : Drawable
    {
        /// <summary>
        /// The fillable shape
        /// </summary>
        private readonly IPath shape;

        /// <summary>
        /// Initializes a new instance of the <see cref="ShapePath"/> class.
        /// </summary>
        /// <param name="path">The path.</param>
        public ShapePath(IPath path)
        {
            this.shape = path;
            this.Bounds = Convert(path.Bounds);
        }

        /// <inheritdoc/>
        public override int MaxIntersections => this.shape.MaxIntersections;

        /// <inheritdoc/>
        public override ImageSharp.Rectangle Bounds { get; }

        /// <inheritdoc/>
        public override int ScanX(int x, float[] buffer, int length, int offset)
        {
            Vector2 start = new Vector2(x, this.Bounds.Top - 1);
            Vector2 end = new Vector2(x, this.Bounds.Bottom + 1);
            Vector2[] innerbuffer = ArrayPool<Vector2>.Shared.Rent(length);
            try
            {
                int count = this.shape.FindIntersections(start, end, innerbuffer, length, 0);

                for (int i = 0; i < count; i++)
                {
                    buffer[i + offset] = innerbuffer[i].Y;
                }

                return count;
            }
            finally
            {
                ArrayPool<Vector2>.Shared.Return(innerbuffer);
            }
        }

        /// <inheritdoc/>
        public override int ScanY(int y, float[] buffer, int length, int offset)
        {
            Vector2 start = new Vector2(this.Bounds.Left - 1, y);
            Vector2 end = new Vector2(this.Bounds.Right + 1, y);
            Vector2[] innerbuffer = ArrayPool<Vector2>.Shared.Rent(length);
            try
            {
                int count = this.shape.FindIntersections(start, end, innerbuffer, length, 0);

                for (int i = 0; i < count; i++)
                {
                    buffer[i + offset] = innerbuffer[i].X;
                }

                return count;
            }
            finally
            {
                ArrayPool<Vector2>.Shared.Return(innerbuffer);
            }
        }

        /// <inheritdoc/>
        public override ImageSharp.Drawing.PointInfo GetPointInfo(int x, int y)
        {
            Vector2 point = new Vector2(x, y);

            var dist = this.shape.Distance(point);

            return new ImageSharp.Drawing.PointInfo
                       {
                           DistanceAlongPath = dist.DistanceAlongPath,
                           DistanceFromPath =
                               dist.DistanceFromPath < 0
                                   ? -dist.DistanceFromPath
                                   : dist.DistanceFromPath
                       };
        }

        private static ImageSharp.Rectangle Convert(SixLabors.Shapes.Rectangle source)
        {
            int left = (int)Math.Floor(source.Left);
            int right = (int)Math.Ceiling(source.Right);
            int top = (int)Math.Floor(source.Top);
            int bottom = (int)Math.Ceiling(source.Bottom);
            return new ImageSharp.Rectangle(left, top, right - left, bottom - top);
        }
    }
}