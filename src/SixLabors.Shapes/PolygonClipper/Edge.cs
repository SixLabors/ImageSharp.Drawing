// <copyright file="Edge.cs" company="Scott Williams">
// Copyright (c) Scott Williams and contributors.
// Licensed under the Apache License, Version 2.0.
// </copyright>

namespace SixLabors.Shapes.PolygonClipper
{
    using System;
    using System.Numerics;
    using System.Runtime.CompilerServices;

    /// <summary>
    /// Class for managing the edges as a linked list
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
        /// Gets the bottom.
        /// </summary>
        /// <value>
        /// The bot.
        /// </value>
        public Vector2 Bottom { get; private set; }

        /// <summary>
        /// Gets or sets the current point.
        /// </summary>
        /// <value>
        /// The current.
        /// </value>
        /// <remarks>
        /// updated for every new scanbeam.
        /// </remarks>
        public Vector2 Current { get; set; }

        /// <summary>
        /// Gets the top.
        /// </summary>
        /// <value>
        /// The top.
        /// </value>
        public Vector2 Top { get; private set; }

        /// <summary>
        /// Gets or sets the delta.
        /// </summary>
        /// <value>
        /// The delta.
        /// </value>
        public Vector2 Delta { get; set; }

        /// <summary>
        /// Gets the dx.
        /// </summary>
        /// <value>
        /// The dx.
        /// </value>
        public double Dx { get; private set; }

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
        /// Gets the next in LML.
        /// </summary>
        /// <value>
        /// The next in LML.
        /// </value>
        public Edge NextInLml { get; private set; }

        /// <summary>
        /// Gets or sets the next in ael
        /// </summary>
        /// <value>
        /// The next in ael.
        /// </value>
        public Edge NextInAel { get; set; }

        /// <summary>
        /// Gets or sets the previous in ael
        /// </summary>
        public Edge PreviousInAel { get; set; }

        /// <summary>
        /// Gets or sets the next in Sorted Edge List
        /// </summary>
        public Edge NextInSortedEdgeList { get; set; }

        /// <summary>
        /// Gets a value indicating whether this instance is horizontal.
        /// </summary>
        /// <value>
        /// <c>true</c> if this instance is horizontal; otherwise, <c>false</c>.
        /// </value>
        public bool IsHorizontal => this.Delta.Y == 0;

        /// <summary>
        /// Gets or sets the previous in Sorted Edge List.
        /// </summary>
        /// <value>
        /// The previous in Sorted Edge List.
        /// </value>
        public Edge PreviousInSortedEdgeList { get; set; }

        /// <summary>
        /// should be inserted before target
        /// </summary>
        /// <param name="target">The target.</param>
        /// <returns>true if it should insert itself before the target</returns>
        public bool ShouldInsertBefore(Edge target)
        {
            if (this.Current.X == target.Current.X)
            {
                if (this.Top.Y > target.Top.Y)
                {
                    return this.Top.X < target.TopX(this.Top.Y);
                }
                else
                {
                    return target.Top.X > this.TopX(target.Top.Y);
                }
            }
            else
            {
                return this.Current.X < target.Current.X;
            }
        }

        /// <summary>
        /// Gets the horizontal direction.
        /// </summary>
        /// <param name="left">The left.</param>
        /// <param name="right">The right.</param>
        /// <returns>the direction</returns>
        public Direction GetHorizontalDirection(out float left, out float right)
        {
            if (this.Bottom.X < this.Top.X)
            {
                left = this.Bottom.X;
                right = this.Top.X;
                return Direction.LeftToRight;
            }
            else
            {
                left = this.Top.X;
                right = this.Bottom.X;
                return Direction.RightToLeft;
            }
        }

        /// <summary>
        /// Finds the point 2 edges intersect
        /// </summary>
        /// <param name="edge2">The edge2.</param>
        /// <returns>the point where edges intersect</returns>
        public Vector2 IntersectPoint(Edge edge2)
        {
            Edge edge1 = this;
            Vector2 ip = default(Vector2);
            double b1, b2;

            // nb: with very large coordinate values, it's possible for SlopesEqual() to
            // return false but for the edge.Dx value be equal due to double precision rounding.
            if (edge1.Dx == edge2.Dx)
            {
                ip.Y = edge1.Current.Y;
                ip.X = edge1.TopX(ip.Y);
                return ip;
            }

            if (edge1.Delta.X == 0)
            {
                ip.X = edge1.Bottom.X;
                if (edge2.IsHorizontal)
                {
                    ip.Y = edge2.Bottom.Y;
                }
                else
                {
                    b2 = edge2.Bottom.Y - (edge2.Bottom.X / edge2.Dx);
                    ip.Y = Helpers.Round((ip.X / edge2.Dx) + b2);
                }
            }
            else if (edge2.Delta.X == 0)
            {
                ip.X = edge2.Bottom.X;
                if (edge1.IsHorizontal)
                {
                    ip.Y = edge1.Bottom.Y;
                }
                else
                {
                    b1 = edge1.Bottom.Y - (edge1.Bottom.X / edge1.Dx);
                    ip.Y = Helpers.Round((ip.X / edge1.Dx) + b1);
                }
            }
            else
            {
                b1 = edge1.Bottom.X - (edge1.Bottom.Y * edge1.Dx);
                b2 = edge2.Bottom.X - (edge2.Bottom.Y * edge2.Dx);
                double q = (b2 - b1) / (edge1.Dx - edge2.Dx);
                ip.Y = Helpers.Round(q);
                if (Math.Abs(edge1.Dx) < Math.Abs(edge2.Dx))
                {
                    ip.X = Helpers.Round((edge1.Dx * q) + b1);
                }
                else
                {
                    ip.X = Helpers.Round((edge2.Dx * q) + b2);
                }
            }

            if (ip.Y < edge1.Top.Y || ip.Y < edge2.Top.Y)
            {
                if (edge1.Top.Y > edge2.Top.Y)
                {
                    ip.Y = edge1.Top.Y;
                }
                else
                {
                    ip.Y = edge2.Top.Y;
                }

                if (Math.Abs(edge1.Dx) < Math.Abs(edge2.Dx))
                {
                    ip.X = edge1.TopX(ip.Y);
                }
                else
                {
                    ip.X = edge2.TopX(ip.Y);
                }
            }

            // finally, don't allow 'ip' to be BELOW curr.Y (ie bottom of scanbeam) ...
            if (ip.Y > edge1.Current.Y)
            {
                ip.Y = edge1.Current.Y;

                // better to use the more vertical edge to derive X ...
                if (Math.Abs(edge1.Dx) > Math.Abs(edge2.Dx))
                {
                    ip.X = edge2.TopX(ip.Y);
                }
                else
                {
                    ip.X = edge1.TopX(ip.Y);
                }
            }

            return ip;
        }

        /// <summary>
        /// Get the next boundry edge.
        /// </summary>
        /// <param name="leftBoundIsForward">if set to <c>true</c> [left bound is forward].</param>
        /// <returns>the next edge based on bounds</returns>
        public Edge NextBoundEdge(bool leftBoundIsForward)
        {
            Edge edge = this;

            while (edge.Top.Y == edge.GetNextEdge(leftBoundIsForward).Bottom.Y)
            {
                edge = edge.GetNextEdge(leftBoundIsForward);
            }

            while (edge != this && edge.Dx == Constants.HorizontalDeltaLimit)
            {
                edge = edge.GetNextEdge(!leftBoundIsForward);
            }

            return edge;
        }

        /// <summary>
        /// Fixes the horizontals.
        /// </summary>
        /// <param name="forwardsIsLeft">if set to <c>true</c> [forwards is left].</param>
        /// <returns>the edge just beyond current bound</returns>
        public Edge FixHorizontals(bool forwardsIsLeft)
        {
            Edge edge = this;
            if (edge.Dx == Constants.HorizontalDeltaLimit)
            {
                // We need to be careful with open paths because this may not be a
                // true local minima (ie E may be following a skip edge).
                // Also, consecutive horz. edges may start heading left before going right.
                Edge next = edge.GetNextEdge(forwardsIsLeft);

                // ie an adjoining horizontal skip edge
                if (next.Dx == Constants.HorizontalDeltaLimit)
                {
                    if (next.Bottom.X != edge.Bottom.X && next.Top.X != edge.Bottom.X)
                    {
                        edge.ReverseHorizontal();
                    }
                }
                else if (next.Bottom.X != edge.Bottom.X)
                {
                    edge.ReverseHorizontal();
                }
            }

            Edge start = edge;
            Edge result = edge;
            Edge nextEdgeToTest = result.GetNextEdge(forwardsIsLeft);
            while (result.Top.Y == nextEdgeToTest.Bottom.Y && nextEdgeToTest.OutIndex != Constants.Skip)
            {
                result = result.GetNextEdge(forwardsIsLeft);
                nextEdgeToTest = result.GetNextEdge(forwardsIsLeft);
            }

            if (result.Dx == Constants.HorizontalDeltaLimit && result.GetNextEdge(forwardsIsLeft).OutIndex != Constants.Skip)
            {
                var horizontalEdge = result;
                Edge horizontalEdgeToTest = horizontalEdge.GetNextEdge(!forwardsIsLeft);
                while (horizontalEdgeToTest.Dx == Constants.HorizontalDeltaLimit)
                {
                    horizontalEdge = horizontalEdge.GetNextEdge(!forwardsIsLeft);
                    horizontalEdgeToTest = horizontalEdge.GetNextEdge(!forwardsIsLeft);
                }

                if ((forwardsIsLeft && horizontalEdgeToTest.Top.X == horizontalEdgeToTest.Top.X) || horizontalEdgeToTest.Top.X > horizontalEdgeToTest.Top.X)
                {
                    result = horizontalEdge.GetNextEdge(!forwardsIsLeft);
                }
            }

            while (edge != result)
            {
                edge.NextInLml = edge.GetNextEdge(forwardsIsLeft);
                if (edge.Dx == Constants.HorizontalDeltaLimit && edge != start)
                {
                    if (edge.Bottom.X != edge.GetNextEdge(!forwardsIsLeft).Top.X)
                    {
                        edge.ReverseHorizontal();
                    }
                }

                edge = edge.GetNextEdge(forwardsIsLeft);
            }

            if (edge.Dx == Constants.HorizontalDeltaLimit && edge != start)
            {
                if (edge.Bottom.X != edge.GetNextEdge(!forwardsIsLeft).Top.X)
                {
                    edge.ReverseHorizontal();
                }
            }

            return result.GetNextEdge(forwardsIsLeft); // move to the edge just beyond current bound
        }

        /// <summary>
        /// Switches the indexes.
        /// </summary>
        /// <param name="target">The target.</param>
        public void SwitchIndexes(Edge target)
        {
            int outIdx = this.OutIndex;
            this.OutIndex = target.OutIndex;
            target.OutIndex = outIdx;
        }

        /// <summary>
        /// Horizontals the segments overlap.
        /// </summary>
        /// <param name="target">The target.</param>
        /// <returns>true if the segments overlap</returns>
        public bool HorizontalSegmentsOverlap(Edge target)
        {
            float segment1A = this.Bottom.X;
            float segment1B = this.Top.X;
            float segment2A = target.Bottom.X;
            float segment2B = target.Top.X;

            if (segment1A > segment1B)
            {
                Helpers.Swap(ref segment1A, ref segment1B);
            }

            if (segment2A > segment2B)
            {
                Helpers.Swap(ref segment2A, ref segment2B);
            }

            return (segment1A < segment2B) && (segment2A < segment1B);
        }

        /// <summary>
        /// Finds the next local minimum.
        /// </summary>
        /// <returns>the Next Loc minimum</returns>
        public Edge FindNextLocalMin()
        {
            Edge edge = this;
            Edge edge2;
            while (true)
            {
                while (edge.Bottom != edge.PreviousEdge.Bottom || edge.Current == edge.Top)
                {
                    edge = edge.NextEdge;
                }

                if (edge.Dx != Constants.HorizontalDeltaLimit && edge.PreviousEdge.Dx != Constants.HorizontalDeltaLimit)
                {
                    break;
                }

                while (edge.PreviousEdge.Dx == Constants.HorizontalDeltaLimit)
                {
                    edge = edge.PreviousEdge;
                }

                edge2 = edge;
                while (edge.Dx == Constants.HorizontalDeltaLimit)
                {
                    edge = edge.NextEdge;
                }

                if (edge.Top.Y == edge.PreviousEdge.Bottom.Y)
                {
                    continue; // ie just an intermediate horz.
                }

                if (edge2.PreviousEdge.Bottom.X < edge.Bottom.X)
                {
                    edge = edge2;
                }

                break;
            }

            return edge;
        }

        /// <summary>
        /// Switches the sides.
        /// </summary>
        /// <param name="target">The target.</param>
        public void SwitchSides(Edge target)
        {
            EdgeSide side = this.Side;
            this.Side = target.Side;
            target.Side = side;
        }

        /// <summary>
        /// Initializes Edge with the provided values
        /// </summary>
        /// <param name="next">The next.</param>
        /// <param name="prev">The previous.</param>
        /// <param name="point">The point.</param>
        public void Init(Edge next, Edge prev, Vector2 point)
        {
            this.NextEdge = next;
            this.PreviousEdge = prev;
            this.Current = point;
            this.OutIndex = Constants.Unassigned;
        }

        /// <summary>
        /// Initializes the type of the clipping.
        /// </summary>
        /// <param name="polyType">Type of the poly.</param>
        public void InitClippingType(ClippingType polyType)
        {
            if (this.Current.Y >= this.NextEdge.Current.Y)
            {
                this.Bottom = this.Current;
                this.Top = this.NextEdge.Current;
            }
            else
            {
                this.Top = this.Current;
                this.Bottom = this.NextEdge.Current;
            }

            this.PolyType = polyType;

            this.ConfigureDelta();
        }

        /// <summary>
        /// Gets the next edge dependent on direction.
        /// </summary>
        /// <param name="leftBoundIsForward">if set to <c>true</c> [left bound is forward].</param>
        /// <returns>the next edge</returns>
        public Edge GetNextEdge(bool leftBoundIsForward)
        {
            if (leftBoundIsForward)
            {
                return this.NextEdge;
            }

            return this.PreviousEdge;
        }

        /// <summary>
        /// Reverses the horizontal.
        /// </summary>
        public void ReverseHorizontal()
        {
            // swap horizontal edges' top and bottom x's so they follow the natural
            // progression of the bounds - ie so their xbots will align with the
            // adjoining lower edge. [Helpful in the ProcessHorizontal() method.]
            var t = this.Top;
            var b = this.Bottom;
            Helpers.Swap(ref t.X, ref b.X);
            this.Top = t;
            this.Bottom = b;
        }

        /// <summary>
        /// Lasts the horizontal edge.
        /// </summary>
        /// <returns>The last horizontal edge</returns>
        public Edge LastHorizontalEdge()
        {
            var lastHorzEdge = this;
            while (lastHorzEdge.NextInLml != null && lastHorzEdge.NextInLml.IsHorizontal)
            {
                lastHorzEdge = lastHorzEdge.NextInLml;
            }

            return lastHorzEdge;
        }

        /// <summary>
        /// Gets the next in ael order.
        /// </summary>
        /// <param name="direction">The direction.</param>
        /// <returns>the next edge based on direction</returns>
        public Edge GetNextInAel(Direction direction)
        {
            return direction == Direction.LeftToRight ? this.NextInAel : this.PreviousInAel;
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
            return this.Top.Y == y && this.NextInLml == null;
        }

        /// <summary>
        /// Gets the maxima pair.
        /// </summary>
        /// <returns>The maxima pair for this edge</returns>
        public Edge GetMaximaPair()
        {
            if ((this.NextEdge.Top == this.Top) && this.NextEdge.NextInLml == null)
            {
                return this.NextEdge;
            }

            if ((this.PreviousEdge.Top == this.Top) && this.PreviousEdge.NextInLml == null)
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
            return this.Top.Y == y && this.NextInLml != null;
        }

        /// <summary>
        /// Gets the maxima pair ex.
        /// </summary>
        /// <returns>The maxima pair for this edge unless it should be skipped</returns>
        public Edge GetMaximaPairEx()
        {
            // as above but returns null if MaxPair isn't in AEL (unless it's horizontal)
            Edge result = this.GetMaximaPair();
            if (result == null || result.OutIndex == Constants.Skip ||
              ((result.NextInAel == result.PreviousInAel) && !result.IsHorizontal))
            {
                return null;
            }

            return result;
        }

        /// <summary>
        /// Tops the x.
        /// </summary>
        /// <param name="currentY">The current y.</param>
        /// <returns>Returns the calculated top X the current Y</returns>
        public float TopX(float currentY)
        {
            if (currentY == this.Top.Y)
            {
                return this.Top.X;
            }

            return this.Bottom.X + Helpers.Round(this.Dx * (currentY - this.Bottom.Y));
        }

        /// <summary>
        /// Removes the self return next.
        /// </summary>
        /// <returns>removes this node from the linked list and returns the next node in the list.</returns>
        public Edge RemoveSelfReturnNext()
        {
            // removes e from double_linked_list (but without removing from memory)
            this.PreviousEdge.NextEdge = this.NextEdge;
            this.NextEdge.PreviousEdge = this.PreviousEdge;
            Edge result = this.NextEdge;
            this.PreviousEdge = null; // flag as removed (see ClipperBase.Clear)
            return result;
        }

        /// <summary>
        /// Compares the slopes to determin if they are equivalent.
        /// </summary>
        /// <param name="other">The e2.</param>
        /// <returns>return true if other's slope is the same as this slop</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool SlopesEqual(Edge other)
        {
            return this.Delta.Y * other.Delta.X == this.Delta.X * other.Delta.Y;
        }

        private void ConfigureDelta()
        {
            this.Delta = new Vector2(this.Top.X - this.Bottom.X, this.Top.Y - this.Bottom.Y);
            if (this.Delta.Y == 0)
            {
                this.Dx = Constants.HorizontalDeltaLimit;
            }
            else
            {
                this.Dx = this.Delta.X / this.Delta.Y;
            }
        }
    }
}