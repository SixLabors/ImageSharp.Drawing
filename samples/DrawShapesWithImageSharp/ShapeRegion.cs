// <copyright file="ShapeRegion.cs" company="James Jackson-South">
// Copyright (c) James Jackson-South and contributors.
// Licensed under the Apache License, Version 2.0.
// </copyright>

using System.Buffers;
using System.Collections.Immutable;
using System.Numerics;

using ImageSharp.Drawing.Processors;

using SixLabors.Shapes;

using ImageSharp.Drawing;
using System;

namespace SixLabors.Shapes.DrawShapesWithImageSharp
{

    /// <summary>
    /// A drawable mapping between a <see cref="SixLabors.Shapes.IShape"/>/<see cref="SixLabors.Shapes.IPath"/> and a drawable/fillable region.
    /// </summary>
    internal class ShapeRegion : Region
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ShapeRegion"/> class.
        /// </summary>
        /// <param name="path">The path.</param>
        public ShapeRegion(IPath path)
            : this(path.AsShape())
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ShapeRegion"/> class.
        /// </summary>
        /// <param name="shape">The shape.</param>
        public ShapeRegion(IShape shape)
        {
            this.Shape = shape;
            this.Bounds = shape.Bounds.Convert();
        }

        /// <summary>
        /// Gets the fillable shape
        /// </summary>
        public IShape Shape { get; }

        /// <inheritdoc/>
        public override int MaxIntersections => this.Shape.MaxIntersections;

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
                int count = this.Shape.FindIntersections(
                    start,
                    end,
                    innerbuffer,
                    length,
                    0);

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
                int count = this.Shape.FindIntersections(
                    start,
                    end,
                    innerbuffer,
                    length,
                    0);

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
    }

    internal static class RectangleExtensions
    {
        /// <summary>
        /// Converts a Shaper2D <see cref="SixLabors.Shapes.Rectangle"/> to an ImageSharp <see cref="Rectangle"/> by creating a <see cref="Rectangle"/> the entirely surrounds the source.
        /// </summary>
        /// <param name="source">The image this method extends.</param>
        /// <returns>A <see cref="Rectangle"/> representation of this <see cref="SixLabors.Shapes.Rectangle"/></returns>
        public static ImageSharp.Rectangle Convert(this SixLabors.Shapes.Rectangle source)
        {
            int left = (int)Math.Floor(source.Left);
            int right = (int)Math.Ceiling(source.Right);
            int top = (int)Math.Floor(source.Top);
            int bottom = (int)Math.Ceiling(source.Bottom);
            return new ImageSharp.Rectangle(left, top, right - left, bottom - top);
        }
    }
}
