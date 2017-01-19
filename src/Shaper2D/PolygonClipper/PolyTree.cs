// <copyright file="PolyTree.cs" company="Scott Williams">
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
    /// Poly Tree
    /// </summary>
    /// <seealso cref="Shaper2D.PolygonClipper.PolyNode" />
    internal class PolyTree : PolyNode
    {
        /// <summary>
        /// Gets or sets all polygon nodes.
        /// </summary>
        public List<PolyNode> AllPolygonNodes { get; set; } = new List<PolyNode>();

        /// <summary>
        /// Gets the total.
        /// </summary>
        /// <value>
        /// The total.
        /// </value>
        public int Total
        {
            get
            {
                int result = this.AllPolygonNodes.Count;

                // with negative offsets, ignore the hidden outer polygon ...
                if (result > 0 && this.Children[0] != this.AllPolygonNodes[0])
                {
                    result--;
                }

                return result;
            }
        }

        /// <summary>
        /// Clears this instance.
        /// </summary>
        public void Clear()
        {
            for (int i = 0; i < this.AllPolygonNodes.Count; i++)
            {
                this.AllPolygonNodes[i] = null;
            }

            this.AllPolygonNodes.Clear();
            this.Children.Clear();
        }

        /// <summary>
        /// Gets the first.
        /// </summary>
        /// <returns>the first node</returns>
        public PolyNode GetFirst()
        {
            if (this.Children.Count > 0)
            {
                return this.Children[0];
            }
            else
            {
                return null;
            }
        }
    }
}
