// <copyright file="Join.cs" company="Scott Williams">
// Copyright (c) Scott Williams and contributors.
// Licensed under the Apache License, Version 2.0.
// </copyright>

namespace SixLabors.Shapes.PolygonClipper
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Numerics;
    using System.Runtime.CompilerServices;

    /// <summary>
    /// Joins 2 points.
    /// </summary>
    internal class Join
    {
        /// <summary>
        /// Gets or sets the out Point 1
        /// </summary>
        internal OutPoint OutPoint1 { get; set; }

        /// <summary>
        /// Gets or sets the out point 2
        /// </summary>
        internal OutPoint OutPoint2 { get; set; }

        /// <summary>
        /// Gets or sets the off point.
        /// </summary>
        internal System.Numerics.Vector2 OffPoint { get; set; }

        /// <summary>
        /// Horizontals the segments overlap.
        /// </summary>
        /// <param name="target">The target.</param>
        /// <returns>true if Horizontals the segments overlap</returns>
        public bool HorizontalSegmentsOverlap(Edge target)
        {
            float seg1a = this.OutPoint1.Point.X;
            float seg1b = this.OffPoint.X;

            float seg2a = target.Bottom.X;
            float seg2b = target.Top.X;

            if (seg1a > seg1b)
            {
                Helpers.Swap(ref seg1a, ref seg1b);
            }

            if (seg2a > seg2b)
            {
                Helpers.Swap(ref seg2a, ref seg2b);
            }

            return (seg1a < seg2b) && (seg2a < seg1b);
        }
    }
}