// <copyright file="Ellipse.cs" company="Scott Williams">
// Copyright (c) Scott Williams and contributors.
// Licensed under the Apache License, Version 2.0.
// </copyright>

namespace SixLabors.Shapes
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Collections.Immutable;
    using System.Linq;
    using System.Numerics;

    /// <summary>
    /// A shape made up of a single path made up of one of more <see cref="ILineSegment"/>s
    /// </summary>
    public class Ellipse : Polygon
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="Ellipse" /> class.
        /// </summary>
        /// <param name="location">The location the center of the ellipse will be placed.</param>
        /// <param name="size">The width/hight of the final ellipse.</param>
        public Ellipse(Vector2 location, Size size)
            : base(CreateSegment(location, size))
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Ellipse" /> class.
        /// </summary>
        /// <param name="location">The location the center of the circle will be placed.</param>
        /// <param name="radius">The radius final circle.</param>
        public Ellipse(Vector2 location, float radius)
            : this(location, new Size(radius*2))
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Ellipse" /> class.
        /// </summary>
        /// <param name="x">The X coordinate of the center of the ellipse.</param>
        /// <param name="y">The Y coordinate of the center of the ellipse.</param>
        /// <param name="width">The width the ellipse should have.</param>
        /// <param name="height">The height the ellipse should have.</param>
        public Ellipse(float x, float y, float width, float height)
            : this(new Vector2(x, y), new Size(width, height))
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Ellipse" /> class.
        /// </summary>
        /// <param name="x">The X coordinate of the center of the circle.</param>
        /// <param name="y">The Y coordinate of the center of the circle.</param>
        /// <param name="radius">The radius final circle.</param>
        public Ellipse(float x, float y, float radius)
            : this(new Vector2(x, y), new Size(radius*2))
        {
        }

        private static new BezierLineSegment CreateSegment(Vector2 location, Size size)
        {
            Guard.MustBeGreaterThan(size.Width, 0, "width");
            Guard.MustBeGreaterThan(size.Height, 0, "height");

            var halfWidth = size.Width / 2;
            var twoThirdsWidth = size.Width * 2 / 3;
            var halfHeight = size.Height / 2;

            var halfHeightVector = new Vector2(0, halfHeight);
            var twoThirdsWidthVector = new Vector2(twoThirdsWidth, 0);
            var points = new Vector2[7] {
                location - halfHeightVector,
                location + twoThirdsWidthVector - halfHeightVector,
                location + twoThirdsWidthVector + halfHeightVector,
                location + halfHeightVector,
                location - twoThirdsWidthVector + halfHeightVector,
                location - twoThirdsWidthVector - halfHeightVector,
                location - halfHeightVector,
            };
            return new BezierLineSegment(points);
        }
    }
}
