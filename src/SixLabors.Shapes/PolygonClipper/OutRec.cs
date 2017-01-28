// <copyright file="OutRec.cs" company="Scott Williams">
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
    /// OutRec: contains a path in the clipping solution. Edges in the AEL will
    /// carry a pointer to an OutRec when they are part of the clipping solution.
    /// </summary>
    internal class OutRec
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="OutRec"/> class.
        /// </summary>
        /// <param name="index">The index.</param>
        public OutRec(int index)
        {
            this.Index = index;
            this.IsHole = false;
            this.IsOpen = false;
            this.FirstLeft = null;
            this.Points = null;
            this.BottomPoint = null;
        }

        /// <summary>
        /// Gets or sets the source path
        /// </summary>
        public IPath SourcePath { get; set; }

        /// <summary>
        /// Gets or sets the index
        /// </summary>
        public int Index { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether this instance is a hole.
        /// </summary>
        /// <value>
        ///   <c>true</c> if this instance is hole; otherwise, <c>false</c>.
        /// </value>
        public bool IsHole { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether this instance is open.
        /// </summary>
        /// <value>
        ///   <c>true</c> if this instance is open; otherwise, <c>false</c>.
        /// </value>
        public bool IsOpen { get; set; }

        /// <summary>
        /// Gets or sets the first left
        /// </summary>
        public OutRec FirstLeft { get; set; }

        /// <summary>
        /// Gets or sets the points.
        /// </summary>
        public OutPoint Points { get; set; }

        /// <summary>
        /// Gets or sets the bottom point.
        /// </summary>
        /// <value>
        /// The bottom point.
        /// </value>
        public OutPoint BottomPoint { get; set; }
    }
}