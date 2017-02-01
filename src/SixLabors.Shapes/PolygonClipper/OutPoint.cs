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
        /// Points the in polygon.
        /// </summary>
        /// <param name="point">The point.</param>
        /// <returns>returns 0 if false, +1 if true, -1 if pt ON polygon boundary</returns>
        public int PointInPolygon(Vector2 point)
        {
            // See "The Point in Polygon Problem for Arbitrary Polygons" by Hormann & Agathos
            // http://citeseerx.ist.psu.edu/viewdoc/download?doi=10.1.1.88.5498&rep=rep1&type=pdf
            OutPoint op = this;
            int result = 0;
            OutPoint startOp = op;
            float ptx = point.X;
            float pty = point.Y;
            float poly0x = op.Point.X;
            float poly0y = op.Point.Y;
            do
            {
                op = op.Next;
                float poly1x = op.Point.X;
                float poly1y = op.Point.Y;

                if (poly1y == pty)
                {
                    if ((poly1x == ptx) || (poly0y == pty &&
                      ((poly1x > ptx) == (poly0x < ptx))))
                    {
                        return -1;
                    }
                }

                if ((poly0y < pty) != (poly1y < pty))
                {
                    if (poly0x >= ptx)
                    {
                        if (poly1x > ptx)
                        {
                            result = 1 - result;
                        }
                        else
                        {
                            double d = (double)((poly0x - ptx) * (poly1y - pty)) -
                              (double)((poly1x - ptx) * (poly0y - pty));
                            if (d == 0)
                            {
                                return -1;
                            }

                            if ((d > 0) == (poly1y > poly0y))
                            {
                                result = 1 - result;
                            }
                        }
                    }
                    else
                    {
                        if (poly1x > ptx)
                        {
                            double d = (double)((poly0x - ptx) * (poly1y - pty)) - (double)((poly1x - ptx) * (poly0y - pty));
                            if (d == 0)
                            {
                                return -1;
                            }

                            if ((d > 0) == (poly1y > poly0y))
                            {
                                result = 1 - result;
                            }
                        }
                    }
                }

                poly0x = poly1x;
                poly0y = poly1y;
            }
            while (startOp != op);

            return result;
        }

        /// <summary>
        /// Determines whether [contains] [the specified out PT1].
        /// </summary>
        /// <param name="outPt1">The out PT1.</param>
        /// <returns>
        ///   <c>true</c> if [contains] [the specified out PT1]; otherwise, <c>false</c>.
        /// </returns>
        public bool Contains(OutPoint outPt1)
        {
            OutPoint outPt2 = this;
            OutPoint op = outPt1;
            do
            {
                // nb: PointInPolygon returns 0 if false, +1 if true, -1 if pt on polygon
                int res = outPt2.PointInPolygon(op.Point);
                if (res >= 0)
                {
                    return res > 0;
                }

                op = op.Next;
            }
            while (op != outPt1);
            return true;
        }

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

        /// <summary>
        /// Duplicates the specified insert after.
        /// </summary>
        /// <param name="insertAfter">if set to <c>true</c> [insert after].</param>
        /// <returns>the duplicated point</returns>
        public OutPoint Duplicate(bool insertAfter)
        {
            OutPoint result = new OutPoint();
            result.Point = this.Point;
            result.Index = this.Index;
            if (insertAfter)
            {
                result.Next = this.Next;
                result.Previous = this;
                this.Next.Previous = result;
                this.Next = result;
            }
            else
            {
                result.Previous = this.Previous;
                result.Next = this;
                this.Previous.Next = result;
                this.Previous = result;
            }

            return result;
        }

        /// <summary>
        /// Calculates the area.
        /// </summary>
        /// <returns>the area</returns>
        public double CalculateArea()
        {
            OutPoint op = this;
            double a = 0;
            do
            {
                a = a + ((op.Previous.Point.X + op.Point.X) * (op.Previous.Point.Y - op.Point.Y));
                op = op.Next;
            }
            while (op != this);

            return a * 0.5;
        }

        /// <summary>
        /// Gets the bottom pt.
        /// </summary>
        /// <returns>the bottombpoint</returns>
        public OutPoint GetBottomPt()
        {
            OutPoint pp = this;
            OutPoint dups = null;
            OutPoint p = pp.Next;
            while (p != pp)
            {
                if (p.Point.Y > pp.Point.Y)
                {
                    pp = p;
                    dups = null;
                }
                else if (p.Point.Y == pp.Point.Y && p.Point.X <= pp.Point.X)
                {
                    if (p.Point.X < pp.Point.X)
                    {
                        dups = null;
                        pp = p;
                    }
                    else
                    {
                        if (p.Next != pp && p.Previous != pp)
                        {
                            dups = p;
                        }
                    }
                }

                p = p.Next;
            }

            if (dups != null)
            {
                // there appears to be at least 2 vertices at bottomPt so ...
                while (dups != p)
                {
                    if (!p.FirstIsBottomPt(dups))
                    {
                        pp = dups;
                    }

                    dups = dups.Next;
                    while (dups.Point != pp.Point)
                    {
                        dups = dups.Next;
                    }
                }
            }

            return pp;
        }

        /// <summary>
        /// Firsts the is bottom pt.
        /// </summary>
        /// <param name="btmPt2">The BTM PT2.</param>
        /// <returns>true if firsts the is bottom point</returns>
        public bool FirstIsBottomPt(OutPoint btmPt2)
        {
            OutPoint btmPt1 = this;
            OutPoint p = btmPt1.Previous;
            while ((p.Point == btmPt1.Point) && (p != btmPt1))
            {
                p = p.Previous;
            }

            double dx1p = Math.Abs(btmPt1.Point.Dx(p.Point));
            p = btmPt1.Next;
            while ((p.Point == btmPt1.Point) && (p != btmPt1))
            {
                p = p.Next;
            }

            double dx1n = Math.Abs(btmPt1.Point.Dx(p.Point));

            p = btmPt2.Previous;
            while ((p.Point == btmPt2.Point) && (p != btmPt2))
            {
                p = p.Previous;
            }

            double dx2p = Math.Abs(btmPt2.Point.Dx(p.Point));
            p = btmPt2.Next;
            while ((p.Point == btmPt2.Point) && (p != btmPt2))
            {
                p = p.Next;
            }

            double dx2n = Math.Abs(btmPt2.Point.Dx(p.Point));

            if (Math.Max(dx1p, dx1n) == Math.Max(dx2p, dx2n) &&
              Math.Min(dx1p, dx1n) == Math.Min(dx2p, dx2n))
            {
                if (btmPt1 == null)
                {
                    return false;
                }

                return btmPt1.CalculateArea() > 0; // if otherwise identical use orientation
            }
            else
            {
                return (dx1p >= dx2p && dx1p >= dx2n) || (dx1n >= dx2p && dx1n >= dx2n);
            }
        }
    }
}