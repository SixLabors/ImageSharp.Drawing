// <copyright file="Edge.cs" company="Scott Williams">
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
    /// TEdge
    /// </summary>
    internal class Edge
    {
        /// <summary>
        /// Gets or sets the source path.
        /// </summary>
        /// <value>
        /// The source path.
        /// </value>
        public IPath SourcePath { get; set; }

        /// <summary>
        /// Gets or sets the bottom.
        /// </summary>
        /// <value>
        /// The bot.
        /// </value>
        public System.Numerics.Vector2 Bottom { get; set; }

        /// <summary>
        /// Gets or sets the current.
        /// </summary>
        /// <value>
        /// The current.
        /// </value>
        /// <remarks>
        /// updated for every new scanbeam.
        /// </remarks>
        public System.Numerics.Vector2 Current { get; set; }

        /// <summary>
        /// Gets or sets the top.
        /// </summary>
        /// <value>
        /// The top.
        /// </value>
        public System.Numerics.Vector2 Top { get; set; }

        /// <summary>
        /// Gets or sets the delta.
        /// </summary>
        /// <value>
        /// The delta.
        /// </value>
        public System.Numerics.Vector2 Delta { get; set; }

        /// <summary>
        /// Gets or sets the dx.
        /// </summary>
        /// <value>
        /// The dx.
        /// </value>
        public double Dx { get; set; }

        /// <summary>
        /// Gets or sets the poly type.
        /// </summary>
        /// <value>
        /// The poly type.
        /// </value>
        public ClippingType PolyType { get; set; }

        /// <summary>
        /// Gets or sets the side.
        /// </summary>
        /// <value>
        /// The side.
        /// </value>
        /// <remarks>Side only refers to current side of solution poly</remarks>
        public EdgeSide Side { get; set; }

        /// <summary>
        /// Gets or sets the wind delta.
        /// </summary>
        /// <value>
        /// The wind delta.
        /// </value>
        /// <remarks>
        /// 1 or -1 depending on winding direction
        /// </remarks>
        public int WindingDelta { get; set; }

        /// <summary>
        /// Gets or sets the winding count
        /// </summary>
        public int WindingCount { get; set; }

        /// <summary>
        /// Gets or sets the type of the winding count in opposite poly.
        /// </summary>
        /// <value>
        /// The type of the winding count in opposite poly.
        /// </value>
        public int WindingCountInOppositePolyType { get; set; }

        /// <summary>
        /// Gets or sets the index of the out.
        /// </summary>
        /// <value>
        /// The index of the out.
        /// </value>
        public int OutIndex { get; set; }

        /// <summary>
        /// Gets or sets the next edge
        /// </summary>
        /// <value>
        /// The next edge.
        /// </value>
        public Edge NextEdge { get; set; }

        /// <summary>
        /// Gets or sets the previous
        /// </summary>
        public Edge PreviousEdge { get; set; }

        /// <summary>
        /// Gets or sets the next in LML.
        /// </summary>
        /// <value>
        /// The next in LML.
        /// </value>
        public Edge NextInLML { get; set; }

        /// <summary>
        /// Gets or sets the next in ael
        /// </summary>
        /// <value>
        /// The next in ael.
        /// </value>
        public Edge NextInAEL { get; set; }

        /// <summary>
        /// Gets or sets the previous in ael
        /// </summary>
        public Edge PreviousInAEL { get; set; }

        /// <summary>
        /// Gets or sets the next in sel
        /// </summary>
        public Edge NextInSEL { get; set; }

        /// <summary>
        /// Gets or sets the previous in sel.
        /// </summary>
        /// <value>
        /// The previous in sel.
        /// </value>
        public Edge PreviousInSEL { get; set; }
    }
}