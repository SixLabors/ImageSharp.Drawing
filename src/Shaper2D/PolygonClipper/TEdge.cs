// <copyright file="TEdge.cs" company="Scott Williams">
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
        /// Gets or sets the bot.
        /// </summary>
        /// <value>
        /// The bot.
        /// </value>
        public System.Numerics.Vector2 Bot { get; set; }

        /// <summary>
        /// Gets or sets the curr.
        /// </summary>
        /// <value>
        /// The curr.
        /// </value>
        /// <remarks>
        /// updated for every new scanbeam.
        /// </remarks>
        public System.Numerics.Vector2 Curr { get; set; }

        /// <summary>
        /// Gets or sets the top.
        /// </summary>
        /// <value>
        /// The top.
        /// </value>
        internal System.Numerics.Vector2 Top { get; set; }

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
        public PolyType PolyType { get; set; }

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
        public int WindindDelta { get; set; }

        /// <summary>
        /// The winding count
        /// </summary>
        public int WindingCount { get; set; }

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
        /// The previous
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
        /// The previous in ael
        /// </summary>
        public Edge PreviousInAEL { get; set; }

        /// <summary>
        /// The next in sel
        /// </summary>
        public Edge NextInSEL { get; set; }

        /// <summary>
        /// The previous in sel
        /// </summary>
        public Edge PreviousInSEL { get; set; }
    }
}