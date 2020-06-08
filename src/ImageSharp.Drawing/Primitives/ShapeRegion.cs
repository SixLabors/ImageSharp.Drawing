// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Buffers;

namespace SixLabors.ImageSharp.Drawing
{
    /// <summary>
    /// A mapping between a <see cref="IPath"/> and a region.
    /// </summary>
    internal class ShapeRegion : Region
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ShapeRegion"/> class.
        /// </summary>
        /// <param name="shape">The shape.</param>
        public ShapeRegion(IPath shape)
        {
            IPath closedPath = shape.AsClosedPath();
            this.MaxIntersections = closedPath.MaxIntersections;
            this.Shape = closedPath;
            int left = (int)MathF.Floor(shape.Bounds.Left);
            int top = (int)MathF.Floor(shape.Bounds.Top);

            int right = (int)MathF.Ceiling(shape.Bounds.Right);
            int bottom = (int)MathF.Ceiling(shape.Bounds.Bottom);
            this.Bounds = Rectangle.FromLTRB(left, top, right, bottom);
        }

        /// <summary>
        /// Gets the fillable shape
        /// </summary>
        public IPath Shape { get; }

        /// <inheritdoc/>
        public override int MaxIntersections { get; }

        /// <inheritdoc/>
        public override Rectangle Bounds { get; }

        /// <inheritdoc/>
        public override int Scan(float y, Span<float> buffer, Configuration configuration, IntersectionRule intersectionRule)
        {
            var start = new PointF(this.Bounds.Left - 1, y);
            var end = new PointF(this.Bounds.Right + 1, y);

            using (IMemoryOwner<PointF> tempBuffer = configuration.MemoryAllocator.Allocate<PointF>(buffer.Length))
            {
                Span<PointF> innerBuffer = tempBuffer.Memory.Span;
                int count = this.Shape.FindIntersections(start, end, innerBuffer, intersectionRule);

                for (int i = 0; i < count; i++)
                {
                    buffer[i] = innerBuffer[i].X;
                }

                return count;
            }
        }
    }
}
