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
        public bool IsOpen { get; set; } = false;

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

        /// <summary>
        /// Fixups the outs.
        /// </summary>
        public void FixupOuts()
        {
            if (this.Points == null)
            {
                return;
            }

            if (this.IsOpen)
            {
                this.FixupOutPolyline();
            }
            else
            {
                this.FixupOutPolygon();
            }
        }

        private void FixupOutPolyline()
        {
            OutPoint pp = this.Points;
            OutPoint lastPP = pp.Previous;
            while (pp != lastPP)
            {
                pp = pp.Next;
                if (pp.Point == pp.Previous.Point)
                {
                    if (pp == lastPP)
                    {
                        lastPP = pp.Previous;
                    }

                    OutPoint tmpPP = pp.Previous;
                    tmpPP.Next = pp.Next;
                    pp.Next.Previous = tmpPP;
                    pp = tmpPP;
                }
            }

            if (pp == pp.Previous)
            {
                this.Points = null;
            }
        }

        private void FixupOutPolygon()
        {
            // FixupOutPolygon() - removes duplicate points and simplifies consecutive
            // parallel edges by removing the middle vertex.
            OutPoint lastOK = null;
            this.BottomPoint = null;
            OutPoint pp = this.Points;
            while (true)
            {
                if (pp.Previous == pp || pp.Previous == pp.Next)
                {
                    this.Points = null;
                    return;
                }

                // test for duplicate points and collinear edges ...
                if ((pp.Point == pp.Next.Point) || (pp.Point == pp.Previous.Point) ||
                  VectorHelpers.SlopesEqual(pp.Previous.Point, pp.Point, pp.Next.Point))
                {
                    lastOK = null;
                    pp.Previous.Next = pp.Next;
                    pp.Next.Previous = pp.Previous;
                    pp = pp.Previous;
                }
                else if (pp == lastOK)
                {
                    break;
                }
                else
                {
                    if (lastOK == null)
                    {
                        lastOK = pp;
                    }

                    pp = pp.Next;
                }
            }

            this.Points = pp;
        }
    }
}