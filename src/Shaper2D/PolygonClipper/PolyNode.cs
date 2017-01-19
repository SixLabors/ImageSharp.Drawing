// <copyright file="PolyNode.cs" company="Scott Williams">
// Copyright (c) Scott Williams and contributors.
// Licensed under the Apache License, Version 2.0.
// </copyright>

namespace Shaper2D.PolygonClipper
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Numerics;
    using System.Runtime.CompilerServices;

    /// <summary>
    /// Poly Node
    /// </summary>
    internal class PolyNode
    {
        private List<PolyNode> children = new List<PolyNode>();

        private List<Vector2> polygon = new List<Vector2>();

        /// <summary>
        /// Gets or sets the index
        /// </summary>
        public int Index { get; set; }

        /// <summary>
        /// Gets the contour.
        /// </summary>
        /// <value>
        /// The contour.
        /// </value>
        public List<Vector2> Contour => this.polygon;

        /// <summary>
        /// Gets the children.
        /// </summary>
        /// <value>
        /// The children.
        /// </value>
        public List<PolyNode> Children => this.children;

        /// <summary>
        /// Gets or sets the source path.
        /// </summary>
        /// <value>
        /// The source path.
        /// </value>
        public IPath SourcePath { get; internal set; }

        /// <summary>
        /// Adds the child.
        /// </summary>
        /// <param name="child">The child.</param>
        internal void AddChild(PolyNode child)
        {
            int cnt = this.children.Count;
            this.children.Add(child);
            child.Index = cnt;
        }
    }
}