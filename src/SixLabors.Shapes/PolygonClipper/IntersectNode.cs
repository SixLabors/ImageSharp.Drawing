// <copyright file="IntersectNode.cs" company="Scott Williams">
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
    /// Intersect Node
    /// </summary>
    internal class IntersectNode
    {
        /// <summary>
        /// Gets or sets the edge1
        /// </summary>
        public Edge Edge1 { get; set; }

        /// <summary>
        /// Gets or sets the edge2
        /// </summary>
        public Edge Edge2 { get; set; }

        /// <summary>
        /// Gets or sets the point.
        /// </summary>
        public System.Numerics.Vector2 Point { get; set; }

        public bool EdgesAdjacent()
        {
            return (this.Edge1.NextInSEL == this.Edge2) ||
              (this.Edge1.PreviousInSEL == this.Edge2);
        }
    }
}