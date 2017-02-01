// <copyright file="OutPoint.cs" company="Scott Williams">
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
    /// Represents the out point.
    /// </summary>
    internal class OutPoint
    {
        /// <summary>
        /// Gets or sets the index
        /// </summary>
        public int Index { get; set; }

        /// <summary>
        /// Gets or sets the point
        /// </summary>
        public System.Numerics.Vector2 Point { get; set; }

        /// <summary>
        /// Gets or sets the next <see cref="OutPoint"/>
        /// </summary>
        public OutPoint Next { get; set; }

        /// <summary>
        /// Gets or sets the previous <see cref="OutPoint"/>
        /// </summary>
        public OutPoint Previous { get; set; }

        /// <summary>
        /// Counts this instance.
        /// </summary>
        /// <returns>count the number of points in this set</returns>
        public int Count()
        {
            int result = 0;
            OutPoint p = this;
            do
            {
                result++;
                p = p.Next;
            }
            while (p != this);
            return result;
        }
    }
}