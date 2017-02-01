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
        /// Gets a value indicating whether this instance is horizontal.
        /// </summary>
        /// <value>
        /// <c>true</c> if this instance is horizontal; otherwise, <c>false</c>.
        /// </value>
        public bool IsHorizontal => this.Delta.Y == 0;

        /// <summary>
        /// Gets or sets the previous in sel.
        /// </summary>
        /// <value>
        /// The previous in sel.
        /// </value>
        public Edge PreviousInSEL { get; set; }

        /// <summary>
        /// Initializes the specified next.
        /// </summary>
        /// <param name="next">The next.</param>
        /// <param name="prev">The previous.</param>
        /// <param name="pt">The pt.</param>
        public void Init(Edge next, Edge prev, Vector2 pt)
        {
            this.NextEdge = next;
            this.PreviousEdge = prev;
            this.Current = pt;
            this.OutIndex = Clipper.Unassigned;
        }

        /// <summary>
        /// Lasts the horizonal edge.
        /// </summary>
        /// <returns>The last horizontal edge</returns>
        public Edge LastHorizonalEdge()
        {
            var lastHorzEdge = this;
            while (lastHorzEdge.NextInLML != null && lastHorzEdge.NextInLML.IsHorizontal)
            {
                lastHorzEdge = lastHorzEdge.NextInLML;
            }

            return lastHorzEdge;
        }

        /// <summary>
        /// Gets the next in ael.
        /// </summary>
        /// <param name="direction">The direction.</param>
        /// <returns>the next edge based on direction</returns>
        public Edge GetNextInAEL(Direction direction)
        {
            return direction == Direction.LeftToRight ? this.NextInAEL : this.PreviousInAEL;
        }

        /// <summary>
        /// Determines whether the edge is Maxima in relation to y.
        /// </summary>
        /// <param name="y">The y.</param>
        /// <returns>
        ///   <c>true</c> if the specified y is maxima; otherwise, <c>false</c>.
        /// </returns>
        public bool IsMaxima(double y)
        {
            return this.Top.Y == y && this.NextInLML == null;
        }

        /// <summary>
        /// Gets the maxima pair.
        /// </summary>
        /// <returns>The maxima pair for this edge</returns>
        public Edge GetMaximaPair()
        {
            if ((this.NextEdge.Top == this.Top) && this.NextEdge.NextInLML == null)
            {
                return this.NextEdge;
            }

            if ((this.PreviousEdge.Top == this.Top) && this.PreviousEdge.NextInLML == null)
            {
                return this.PreviousEdge;
            }

            return null;
        }

        /// <summary>
        /// Determines whether the specified intermediate in relation to y.
        /// </summary>
        /// <param name="y">The y.</param>
        /// <returns>
        ///   <c>true</c> if the specified y is intermediate; otherwise, <c>false</c>.
        /// </returns>
        public bool IsIntermediate(double y)
        {
            return this.Top.Y == y && this.NextInLML != null;
        }

        /// <summary>
        /// Gets the maxima pair ex.
        /// </summary>
        /// <returns>The maxima pair for this edge unless it should be skipped</returns>
        public Edge GetMaximaPairEx()
        {
            // as above but returns null if MaxPair isn't in AEL (unless it's horizontal)
            Edge result = this.GetMaximaPair();
            if (result == null || result.OutIndex == Clipper.Skip ||
              ((result.NextInAEL == result.PreviousInAEL) && !result.IsHorizontal))
            {
                return null;
            }

            return result;
        }
    }
}