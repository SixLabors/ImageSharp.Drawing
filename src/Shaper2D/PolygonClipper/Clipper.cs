// <copyright file="Clipper.cs" company="Scott Williams">
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
    /// Library to clip polygons.
    /// </summary>
    internal class Clipper
    {
        private const double HorizontalDeltaLimit = -3.4E+38;
        private const int Skip = -2;
        private const int Unassigned = -1; // InitOptions that can be passed to the constructor ...
        private static readonly IComparer<IntersectNode> IntersectNodeComparer = new IntersectNodeSort();
        private readonly List<IntersectNode> intersectList = new List<IntersectNode>();

        private readonly List<Join> joins = new List<Join>();

        private readonly List<Join> ghostJoins = new List<Join>();
        private readonly List<List<Edge>> edges = new List<List<Edge>>();
        private readonly List<OutRec> polyOuts = new List<OutRec>();

        private Maxima maxima = null;
        private Edge sortedEdges = null;

        private LocalMinima minimaList;
        private LocalMinima currentLocalMinima;
        private Scanbeam scanbeam = null;
        private Edge activeEdges = null;

        /// <summary>
        /// Adds the paths.
        /// </summary>
        /// <param name="path">The path.</param>
        /// <param name="polyType">Type of the poly.</param>
        public void AddPaths(IEnumerable<IShape> path, PolyType polyType)
        {
            foreach (var p in path)
            {
                this.AddPath(p, polyType);
            }
        }

        /// <summary>
        /// Adds the path.
        /// </summary>
        /// <param name="path">The path.</param>
        /// <param name="polyType">Type of the poly.</param>
        public void AddPath(IShape path, PolyType polyType)
        {
            if (path is IPath)
            {
                this.AddPath((IPath)path, polyType);
            }
            else
            {
                foreach (var p in path.Paths)
                {
                    this.AddPath(p, polyType);
                }
            }
        }

        /// <summary>
        /// Adds the path.
        /// </summary>
        /// <param name="path">The path.</param>
        /// <param name="polyType">Type of the poly.</param>
        /// <returns>True if the path was added.</returns>
        /// <exception cref="ClipperException">AddPath: Open paths have been disabled.</exception>
        public bool AddPath(IPath path, PolyType polyType)
        {
            var points = path.AsSimpleLinearPath();

            int hi = points.Length - 1;
            while (hi > 0 && (points[hi] == points[0]))
            {
                --hi;
            }

            while (hi > 0 && (points[hi] == points[hi - 1]))
            {
                --hi;
            }

            if (hi < 2)
            {
                throw new ClipperException("must have more than 2 distinct points");
            }

            // create a new edge array ...
            List<Edge> edges = new List<Edge>(hi + 1);
            for (int i = 0; i <= hi; i++)
            {
                edges.Add(new Edge() { SourcePath = path });
            }

            bool isFlat = true;

            // 1. Basic (first) edge initialization ...
            edges[1].Current = points[1];

            InitEdge(edges[0], edges[1], edges[hi], points[0]);
            InitEdge(edges[hi], edges[0], edges[hi - 1], points[hi]);
            for (int i = hi - 1; i >= 1; --i)
            {
                InitEdge(edges[i], edges[i + 1], edges[i - 1], points[i]);
            }

            Edge startEdge = edges[0];

            // 2. Remove duplicate vertices, and (when closed) collinear edges ...
            Edge edge = startEdge;
            Edge loopStop = startEdge;
            while (true)
            {
                if (edge.Current == edge.NextEdge.Current)
                {
                    //remove unneeded edges
                    if (edge == edge.NextEdge)
                    {
                        break;
                    }

                    if (edge == startEdge)
                    {
                        startEdge = edge.NextEdge;
                    }

                    edge = RemoveEdge(edge);
                    loopStop = edge;
                    continue;
                }

                //can't have only 2 verticies
                //if (edge.PreviousEdge == edge.NextEdge)
                //{
                //    break; // only two vertices
                //}

                if (SlopesEqual(edge.PreviousEdge.Current, edge.Current, edge.NextEdge.Current))
                {
                    // Collinear edges are allowed for open paths but in closed paths
                    // the default is to merge adjacent collinear edges into a single edge.
                    // However, if the PreserveCollinear property is enabled, only overlapping
                    // collinear edges (ie spikes) will be removed from closed paths.
                    if (edge == startEdge)
                    {
                        startEdge = edge.NextEdge;
                    }

                    edge = RemoveEdge(edge);
                    edge = edge.PreviousEdge;
                    loopStop = edge;
                    continue;
                }

                edge = edge.NextEdge;
                if (edge == loopStop)
                {
                    break;
                }
            }

            if (edge.PreviousEdge == edge.NextEdge)
            {
                return false;
            }

            // 3. Do second stage of edge initialization ...
            edge = startEdge;
            do
            {
                this.InitEdge2(edge, polyType);
                edge = edge.NextEdge;
                if (isFlat && edge.Current.Y != startEdge.Current.Y)
                {
                    isFlat = false;
                }
            }
            while (edge != startEdge);

            // 4. Finally, add edge bounds to LocalMinima list ...
            // Totally flat paths must be handled differently when adding them
            // to LocalMinima list to avoid endless loops etc ...
            if (isFlat)
            {
                return false;
            }

            this.edges.Add(edges);
            Edge loopBreakerEdge = null;

            // workaround to avoid an endless loop in the while loop below when
            // open paths have matching start and end points ...
            if (edge.PreviousEdge.Bottom == edge.PreviousEdge.Top)
            {
                edge = edge.NextEdge;
            }

            while (true)
            {
                edge = FindNextLocMin(edge);
                if (edge == loopBreakerEdge)
                {
                    break;
                }
                else if (loopBreakerEdge == null)
                {
                    loopBreakerEdge = edge;
                }

                // E and E.Prev now share a local minima (left aligned if horizontal).
                // Compare their slopes to find which starts which bound ...
                LocalMinima locMin = new LocalMinima
                {
                    Next = null,
                    Y = edge.Bottom.Y
                };

                bool leftBoundIsForward;
                if (edge.Dx < edge.PreviousEdge.Dx)
                {
                    locMin.LeftBound = edge.PreviousEdge;
                    locMin.RightBound = edge;
                    leftBoundIsForward = false; // Q.nextInLML = Q.prev
                }
                else
                {
                    locMin.LeftBound = edge;
                    locMin.RightBound = edge.PreviousEdge;
                    leftBoundIsForward = true; // Q.nextInLML = Q.next
                }

                locMin.LeftBound.Side = EdgeSide.Left;
                locMin.RightBound.Side = EdgeSide.Right;

                if (locMin.LeftBound.NextEdge == locMin.RightBound)
                {
                    locMin.LeftBound.WindingDelta = -1;
                }
                else
                {
                    locMin.LeftBound.WindingDelta = 1;
                }

                locMin.RightBound.WindingDelta = -locMin.LeftBound.WindingDelta;

                edge = this.ProcessBound(locMin.LeftBound, leftBoundIsForward);
                if (edge.OutIndex == Skip)
                {
                    edge = this.ProcessBound(edge, leftBoundIsForward);
                }

                Edge edge2 = this.ProcessBound(locMin.RightBound, !leftBoundIsForward);
                if (edge2.OutIndex == Skip)
                {
                    edge2 = this.ProcessBound(edge2, !leftBoundIsForward);
                }

                if (locMin.LeftBound.OutIndex == Skip)
                {
                    locMin.LeftBound = null;
                }
                else if (locMin.RightBound.OutIndex == Skip)
                {
                    locMin.RightBound = null;
                }

                this.InsertLocalMinima(locMin);
                if (!leftBoundIsForward)
                {
                    edge = edge2;
                }
            }

            return true;
        }

        /// <summary>
        /// Executes the specified clip type.
        /// </summary>
        /// <returns>
        /// Returns the <see cref="IShape" /> array containing the converted polygons.
        /// </returns>
        public IShape[] Execute()
        {
            PolyTree polytree = new PolyTree();
            bool succeeded = this.ExecuteInternal();

            // build the return polygons ...
            if (succeeded)
            {
                this.BuildResult2(polytree);
            }

            return ExtractOutlines(polytree).ToArray();
        }

        private static float Round(double value)
        {
            return value < 0 ? (float)(value - 0.5) : (float)(value + 0.5);
        }

        private static float TopX(Edge edge, float currentY)
        {
            if (currentY == edge.Top.Y)
            {
                return edge.Top.X;
            }

            return edge.Bottom.X + Round(edge.Dx * (currentY - edge.Bottom.Y));
        }

        private static List<IShape> ExtractOutlines(PolyNode tree)
        {
            var result = new List<IShape>();
            ExtractOutlines(tree, result);
            return result;
        }

        private static void ExtractOutlines(PolyNode tree, List<IShape> shapes)
        {
            if (tree.Contour.Any())
            {
                // if the source path is set then we clipper retained the full path intact thus we can freely
                // use it and get any shape optimizations that are available.
                if (tree.SourcePath != null)
                {
                    shapes.Add((IShape)tree.SourcePath);
                }
                else
                {
                    Polygon polygon = new Polygon(new LinearLineSegment(tree.Contour.Select(x => new Point(x)).ToArray()));

                    shapes.Add(polygon);
                }
            }

            foreach (PolyNode c in tree.Children)
            {
                ExtractOutlines(c, shapes);
            }
        }

        private static void FixHoleLinkage(OutRec outRec)
        {
            // skip if an outermost polygon or
            // already already points to the correct FirstLeft ...
            if (outRec.FirstLeft == null ||
                  (outRec.IsHole != outRec.FirstLeft.IsHole &&
                  outRec.FirstLeft.Points != null))
            {
                return;
            }

            OutRec orfl = outRec.FirstLeft;
            while (orfl != null && ((orfl.IsHole == outRec.IsHole) || orfl.Points == null))
            {
                orfl = orfl.FirstLeft;
            }

            outRec.FirstLeft = orfl;
        }

        // See "The Point in Polygon Problem for Arbitrary Polygons" by Hormann & Agathos
        // http://citeseerx.ist.psu.edu/viewdoc/download?doi=10.1.1.88.5498&rep=rep1&type=pdf
        private static int PointInPolygon(Vector2 pt, OutPoint op)
        {
            // returns 0 if false, +1 if true, -1 if pt ON polygon boundary
            int result = 0;
            OutPoint startOp = op;
            float ptx = pt.X;
            float pty = pt.Y;
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

        private static bool Poly2ContainsPoly1(OutPoint outPt1, OutPoint outPt2)
        {
            OutPoint op = outPt1;
            do
            {
                // nb: PointInPolygon returns 0 if false, +1 if true, -1 if pt on polygon
                int res = PointInPolygon(op.Point, outPt2);
                if (res >= 0)
                {
                    return res > 0;
                }

                op = op.Next;
            }
            while (op != outPt1);
            return true;
        }

        private static void SwapSides(Edge edge1, Edge edge2)
        {
            EdgeSide side = edge1.Side;
            edge1.Side = edge2.Side;
            edge2.Side = side;
        }

        private static void SwapPolyIndexes(Edge edge1, Edge edge2)
        {
            int outIdx = edge1.OutIndex;
            edge1.OutIndex = edge2.OutIndex;
            edge2.OutIndex = outIdx;
        }

        private static double GetDx(Vector2 pt1, Vector2 pt2)
        {
            if (pt1.Y == pt2.Y)
            {
                return HorizontalDeltaLimit;
            }
            else
            {
                return (double)(pt2.X - pt1.X) / (pt2.Y - pt1.Y);
            }
        }

        private static bool HorizontalSegmentsOverlap(float seg1a, float seg1b, float seg2a, float seg2b)
        {
            if (seg1a > seg1b)
            {
                Swap(ref seg1a, ref seg1b);
            }

            if (seg2a > seg2b)
            {
                Swap(ref seg2a, ref seg2b);
            }

            return (seg1a < seg2b) && (seg2a < seg1b);
        }

        private static Edge FindNextLocMin(Edge edge)
        {
            Edge edge2;
            while (true)
            {
                while (edge.Bottom != edge.PreviousEdge.Bottom || edge.Current == edge.Top)
                {
                    edge = edge.NextEdge;
                }

                if (edge.Dx != HorizontalDeltaLimit && edge.PreviousEdge.Dx != HorizontalDeltaLimit)
                {
                    break;
                }

                while (edge.PreviousEdge.Dx == HorizontalDeltaLimit)
                {
                    edge = edge.PreviousEdge;
                }

                edge2 = edge;
                while (edge.Dx == HorizontalDeltaLimit)
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void Swap(ref float val1, ref float val2)
        {
            float tmp = val1;
            val1 = val2;
            val2 = tmp;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsHorizontal(Edge e)
        {
            return e.Delta.Y == 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool SlopesEqual(Edge e1, Edge e2)
        {
            return e1.Delta.Y * e2.Delta.X == e1.Delta.X * e2.Delta.Y;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool SlopesEqual(Vector2 pt1, Vector2 pt2, Vector2 pt3)
        {
            var dif12 = pt1 - pt2;
            var dif23 = pt2 - pt3;
            return (dif12.Y * dif23.X) - (dif12.X * dif23.Y) == 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool SlopesEqual(Vector2 pt1, Vector2 pt2, Vector2 pt3, Vector2 pt4)
        {
            var dif12 = pt1 - pt2;
            var dif34 = pt3 - pt4;

            return (dif12.Y * dif34.X) - (dif12.X * dif34.Y) == 0;
        }

        private static void InitEdge(Edge e, Edge eNext, Edge ePrev, Vector2 pt)
        {
            e.NextEdge = eNext;
            e.PreviousEdge = ePrev;
            e.Current = pt;
            e.OutIndex = Unassigned;
        }

        private static OutRec ParseFirstLeft(OutRec firstLeft)
        {
            while (firstLeft != null && firstLeft.Points == null)
            {
                firstLeft = firstLeft.FirstLeft;
            }

            return firstLeft;
        }

        private static Edge RemoveEdge(Edge e)
        {
            // removes e from double_linked_list (but without removing from memory)
            e.PreviousEdge.NextEdge = e.NextEdge;
            e.NextEdge.PreviousEdge = e.PreviousEdge;
            Edge result = e.NextEdge;
            e.PreviousEdge = null; // flag as removed (see ClipperBase.Clear)
            return result;
        }

        private static void ReverseHorizontal(Edge e)
        {
            // swap horizontal edges' top and bottom x's so they follow the natural
            // progression of the bounds - ie so their xbots will align with the
            // adjoining lower edge. [Helpful in the ProcessHorizontal() method.]
            var t = e.Top;
            var b = e.Bottom;
            Swap(ref t.X, ref b.X);
            e.Top = t;
            e.Bottom = b;

            // old code incase the above doesn't work
            // Swap(ref e.Top.X, ref e.Bot.X);
        }

        private bool ExecuteInternal()
        {
            try
            {
                this.Reset();
                this.sortedEdges = null;
                this.maxima = null;

                float botY, topY;
                if (!this.PopScanbeam(out botY))
                {
                    return false;
                }

                this.InsertLocalMinimaIntoAEL(botY);
                while (this.PopScanbeam(out topY) || this.LocalMinimaPending())
                {
                    this.ProcessHorizontals();
                    this.ghostJoins.Clear();
                    if (!this.ProcessIntersections(topY))
                    {
                        return false;
                    }

                    this.ProcessEdgesAtTopOfScanbeam(topY);
                    botY = topY;
                    this.InsertLocalMinimaIntoAEL(botY);
                }

                // fix orientations ...
                foreach (OutRec outRec in this.polyOuts)
                {
                    if (outRec.Points == null || outRec.IsOpen)
                    {
                        continue;
                    }
                }

                this.JoinCommonEdges();

                foreach (OutRec outRec in this.polyOuts)
                {
                    if (outRec.Points == null)
                    {
                        continue;
                    }
                    else if (outRec.IsOpen)
                    {
                        this.FixupOutPolyline(outRec);
                    }
                    else
                    {
                        this.FixupOutPolygon(outRec);
                    }
                }

                return true;
            }
            finally
            {
                this.joins.Clear();
                this.ghostJoins.Clear();
            }
        }

        private void AddJoin(OutPoint op1, OutPoint op2, Vector2 offPt)
        {
            Join j = new Join();
            j.OutPoint1 = op1;
            j.OutPoint2 = op2;
            j.OffPoint = offPt;
            this.joins.Add(j);
        }

        private void AddGhostJoin(OutPoint op, Vector2 offPt)
        {
            Join j = new Join();
            j.OutPoint1 = op;
            j.OffPoint = offPt;
            this.ghostJoins.Add(j);
        }

        private void InsertLocalMinimaIntoAEL(float botY)
        {
            LocalMinima lm;
            while (this.PopLocalMinima(botY, out lm))
            {
                Edge lb = lm.LeftBound;
                Edge rb = lm.RightBound;

                OutPoint op1 = null;
                if (lb == null)
                {
                    this.InsertEdgeIntoAEL(rb, null);
                    this.SetWindingCount(rb);
                    if (this.IsContributing(rb))
                    {
                        op1 = this.AddOutPt(rb, rb.Bottom);
                    }
                }
                else if (rb == null)
                {
                    this.InsertEdgeIntoAEL(lb, null);
                    this.SetWindingCount(lb);
                    if (this.IsContributing(lb))
                    {
                        op1 = this.AddOutPt(lb, lb.Bottom);
                    }

                    this.InsertScanbeam(lb.Top.Y);
                }
                else
                {
                    this.InsertEdgeIntoAEL(lb, null);
                    this.InsertEdgeIntoAEL(rb, lb);
                    this.SetWindingCount(lb);
                    rb.WindingCount = lb.WindingCount;
                    rb.WindingCountInOppositePolyType = lb.WindingCountInOppositePolyType;
                    if (this.IsContributing(lb))
                    {
                        op1 = this.AddLocalMinPoly(lb, rb, lb.Bottom);
                    }

                    this.InsertScanbeam(lb.Top.Y);
                }

                if (rb != null)
                {
                    if (IsHorizontal(rb))
                    {
                        if (rb.NextInLML != null)
                        {
                            this.InsertScanbeam(rb.NextInLML.Top.Y);
                        }

                        this.AddEdgeToSEL(rb);
                    }
                    else
                    {
                        this.InsertScanbeam(rb.Top.Y);
                    }
                }

                if (lb == null || rb == null)
                {
                    continue;
                }

                // if output polygons share an Edge with a horizontal rb, they'll need joining later ...
                if (op1 != null && IsHorizontal(rb) &&
                 this.ghostJoins.Count > 0 && rb.WindingDelta != 0)
                {
                    for (int i = 0; i < this.ghostJoins.Count; i++)
                    {
                        // if the horizontal Rb and a 'ghost' horizontal overlap, then convert
                        // the 'ghost' join to a real join ready for later ...
                        Join j = this.ghostJoins[i];
                        if (HorizontalSegmentsOverlap(j.OutPoint1.Point.X, j.OffPoint.X, rb.Bottom.X, rb.Top.X))
                        {
                            this.AddJoin(j.OutPoint1, op1, j.OffPoint);
                        }
                    }
                }

                if (lb.OutIndex >= 0 && lb.PreviousInAEL != null &&
                  lb.PreviousInAEL.Current.X == lb.Bottom.X &&
                  lb.PreviousInAEL.OutIndex >= 0 &&
                  SlopesEqual(lb.PreviousInAEL.Current, lb.PreviousInAEL.Top, lb.Current, lb.Top) &&
                  lb.WindingDelta != 0 && lb.PreviousInAEL.WindingDelta != 0)
                {
                    OutPoint op2 = this.AddOutPt(lb.PreviousInAEL, lb.Bottom);
                    this.AddJoin(op1, op2, lb.Top);
                }

                if (lb.NextInAEL != rb)
                {
                    if (rb.OutIndex >= 0 && rb.PreviousInAEL.OutIndex >= 0 &&
                      SlopesEqual(rb.PreviousInAEL.Current, rb.PreviousInAEL.Top, rb.Current, rb.Top) &&
                      rb.WindingDelta != 0 && rb.PreviousInAEL.WindingDelta != 0)
                    {
                        OutPoint op2 = this.AddOutPt(rb.PreviousInAEL, rb.Bottom);
                        this.AddJoin(op1, op2, rb.Top);
                    }

                    Edge e = lb.NextInAEL;
                    if (e != null)
                    {
                        while (e != rb)
                        {
                            // nb: For calculating winding counts etc, IntersectEdges() assumes
                            // that param1 will be to the right of param2 ABOVE the intersection ...
                            this.IntersectEdges(rb, e, lb.Current); // order important here
                            e = e.NextInAEL;
                        }
                    }
                }
            }
        }

        private void InsertEdgeIntoAEL(Edge edge, Edge startEdge)
        {
            if (this.activeEdges == null)
            {
                edge.PreviousInAEL = null;
                edge.NextInAEL = null;
                this.activeEdges = edge;
            }
            else if (startEdge == null && this.E2InsertsBeforeE1(this.activeEdges, edge))
            {
                edge.PreviousInAEL = null;
                edge.NextInAEL = this.activeEdges;
                this.activeEdges.PreviousInAEL = edge;
                this.activeEdges = edge;
            }
            else
            {
                if (startEdge == null)
                {
                    startEdge = this.activeEdges;
                }

                while (startEdge.NextInAEL != null &&
                  !this.E2InsertsBeforeE1(startEdge.NextInAEL, edge))
                {
                    startEdge = startEdge.NextInAEL;
                }

                edge.NextInAEL = startEdge.NextInAEL;
                if (startEdge.NextInAEL != null)
                {
                    startEdge.NextInAEL.PreviousInAEL = edge;
                }

                edge.PreviousInAEL = startEdge;
                startEdge.NextInAEL = edge;
            }
        }

        private bool E2InsertsBeforeE1(Edge e1, Edge e2)
        {
            if (e2.Current.X == e1.Current.X)
            {
                if (e2.Top.Y > e1.Top.Y)
                {
                    return e2.Top.X < TopX(e1, e2.Top.Y);
                }
                else
                {
                    return e1.Top.X > TopX(e2, e1.Top.Y);
                }
            }
            else
            {
                return e2.Current.X < e1.Current.X;
            }
        }

        private bool IsContributing(Edge edge)
        {
            // return false if a subj line has been flagged as inside a subj polygon
            if (edge.WindingDelta == 0 && edge.WindingCount != 1)
            {
                return false;
            }

            if (edge.PolyType == PolyType.Subject)
            {
                return edge.WindingCountInOppositePolyType == 0;
            }
            else
            {
                return edge.WindingCountInOppositePolyType != 0;
            }
        }

        private void SetWindingCount(Edge edge)
        {
            Edge e = edge.PreviousInAEL;

            // find the edge of the same polytype that immediately preceeds 'edge' in AEL
            while (e != null && ((e.PolyType != edge.PolyType) || (e.WindingDelta == 0)))
            {
                e = e.PreviousInAEL;
            }

            if (e == null)
            {
                if (edge.WindingDelta == 0)
                {
                    edge.WindingCount = 1;
                }
                else
                {
                    edge.WindingCount = edge.WindingDelta;
                }

                edge.WindingCountInOppositePolyType = 0;
                e = this.activeEdges; // ie get ready to calc WindCnt2
            }
            else if (edge.WindingDelta == 0)
            {
                edge.WindingCount = 1;
                edge.WindingCountInOppositePolyType = e.WindingCountInOppositePolyType;
                e = e.NextInAEL; // ie get ready to calc WindCnt2
            }
            else
            {
                // EvenOdd filling ...
                if (edge.WindingDelta == 0)
                {
                    // are we inside a subj polygon ...
                    bool inside = true;
                    Edge e2 = e.PreviousInAEL;
                    while (e2 != null)
                    {
                        if (e2.PolyType == e.PolyType && e2.WindingDelta != 0)
                        {
                            inside = !inside;
                        }

                        e2 = e2.PreviousInAEL;
                    }

                    edge.WindingCount = inside ? 0 : 1;
                }
                else
                {
                    edge.WindingCount = edge.WindingDelta;
                }

                edge.WindingCountInOppositePolyType = e.WindingCountInOppositePolyType;
                e = e.NextInAEL; // ie get ready to calc WindCnt2
            }

            // update WindCnt2 ...
            // EvenOdd filling ...
            while (e != edge)
            {
                if (e.WindingDelta != 0)
                {
                    edge.WindingCountInOppositePolyType = edge.WindingCountInOppositePolyType == 0 ? 1 : 0;
                }

                e = e.NextInAEL;
            }
        }

        private void AddEdgeToSEL(Edge edge)
        {
            // SEL pointers in PEdge are use to build transient lists of horizontal edges.
            // However, since we don't need to worry about processing order, all additions
            // are made to the front of the list ...
            if (this.sortedEdges == null)
            {
                this.sortedEdges = edge;
                edge.PreviousInSEL = null;
                edge.NextInSEL = null;
            }
            else
            {
                edge.NextInSEL = this.sortedEdges;
                edge.PreviousInSEL = null;
                this.sortedEdges.PreviousInSEL = edge;
                this.sortedEdges = edge;
            }
        }

        private bool PopEdgeFromSEL(out Edge e)
        {
            // Pop edge from front of SEL (ie SEL is a FILO list)
            e = this.sortedEdges;
            if (e == null)
            {
                return false;
            }

            Edge oldE = e;
            this.sortedEdges = e.NextInSEL;
            if (this.sortedEdges != null)
            {
                this.sortedEdges.PreviousInSEL = null;
            }

            oldE.NextInSEL = null;
            oldE.PreviousInSEL = null;
            return true;
        }

        private void CopyAELToSEL()
        {
            Edge e = this.activeEdges;
            this.sortedEdges = e;
            while (e != null)
            {
                e.PreviousInSEL = e.PreviousInAEL;
                e.NextInSEL = e.NextInAEL;
                e = e.NextInAEL;
            }
        }

        private void SwapPositionsInSEL(Edge edge1, Edge edge2)
        {
            if (edge1.NextInSEL == null && edge1.PreviousInSEL == null)
            {
                return;
            }

            if (edge2.NextInSEL == null && edge2.PreviousInSEL == null)
            {
                return;
            }

            if (edge1.NextInSEL == edge2)
            {
                Edge next = edge2.NextInSEL;
                if (next != null)
                {
                    next.PreviousInSEL = edge1;
                }

                Edge prev = edge1.PreviousInSEL;
                if (prev != null)
                {
                    prev.NextInSEL = edge2;
                }

                edge2.PreviousInSEL = prev;
                edge2.NextInSEL = edge1;
                edge1.PreviousInSEL = edge2;
                edge1.NextInSEL = next;
            }
            else if (edge2.NextInSEL == edge1)
            {
                Edge next = edge1.NextInSEL;
                if (next != null)
                {
                    next.PreviousInSEL = edge2;
                }

                Edge prev = edge2.PreviousInSEL;
                if (prev != null)
                {
                    prev.NextInSEL = edge1;
                }

                edge1.PreviousInSEL = prev;
                edge1.NextInSEL = edge2;
                edge2.PreviousInSEL = edge1;
                edge2.NextInSEL = next;
            }
            else
            {
                Edge next = edge1.NextInSEL;
                Edge prev = edge1.PreviousInSEL;
                edge1.NextInSEL = edge2.NextInSEL;
                if (edge1.NextInSEL != null)
                {
                    edge1.NextInSEL.PreviousInSEL = edge1;
                }

                edge1.PreviousInSEL = edge2.PreviousInSEL;
                if (edge1.PreviousInSEL != null)
                {
                    edge1.PreviousInSEL.NextInSEL = edge1;
                }

                edge2.NextInSEL = next;
                if (edge2.NextInSEL != null)
                {
                    edge2.NextInSEL.PreviousInSEL = edge2;
                }

                edge2.PreviousInSEL = prev;
                if (edge2.PreviousInSEL != null)
                {
                    edge2.PreviousInSEL.NextInSEL = edge2;
                }
            }

            if (edge1.PreviousInSEL == null)
            {
                this.sortedEdges = edge1;
            }
            else if (edge2.PreviousInSEL == null)
            {
                this.sortedEdges = edge2;
            }
        }

        private void AddLocalMaxPoly(Edge e1, Edge e2, Vector2 pt)
        {
            this.AddOutPt(e1, pt);
            if (e2.WindingDelta == 0)
            {
                this.AddOutPt(e2, pt);
            }

            if (e1.OutIndex == e2.OutIndex)
            {
                e1.OutIndex = Unassigned;
                e2.OutIndex = Unassigned;
            }
            else if (e1.OutIndex < e2.OutIndex)
            {
                this.AppendPolygon(e1, e2);
            }
            else
            {
                this.AppendPolygon(e2, e1);
            }
        }

        private OutPoint AddLocalMinPoly(Edge e1, Edge e2, Vector2 pt)
        {
            OutPoint result;
            Edge e, prevE;
            if (IsHorizontal(e2) || (e1.Dx > e2.Dx))
            {
                result = this.AddOutPt(e1, pt);
                e2.OutIndex = e1.OutIndex;
                e1.Side = EdgeSide.Left;
                e2.Side = EdgeSide.Right;
                e = e1;
                if (e.PreviousInAEL == e2)
                {
                    prevE = e2.PreviousInAEL;
                }
                else
                {
                    prevE = e.PreviousInAEL;
                }
            }
            else
            {
                result = this.AddOutPt(e2, pt);
                e1.OutIndex = e2.OutIndex;
                e1.Side = EdgeSide.Right;
                e2.Side = EdgeSide.Left;
                e = e2;
                if (e.PreviousInAEL == e1)
                {
                    prevE = e1.PreviousInAEL;
                }
                else
                {
                    prevE = e.PreviousInAEL;
                }
            }

            if (prevE != null && prevE.OutIndex >= 0)
            {
                float xPrev = TopX(prevE, pt.Y);
                float xE = TopX(e, pt.Y);
                if ((xPrev == xE) &&
                    (e.WindingDelta != 0) &&
                    (prevE.WindingDelta != 0) &&
                    SlopesEqual(new Vector2(xPrev, pt.Y), prevE.Top, new Vector2(xE, pt.Y), e.Top))
                {
                    OutPoint outPt = this.AddOutPt(prevE, pt);
                    this.AddJoin(result, outPt, e.Top);
                }
            }

            return result;
        }

        private OutPoint AddOutPt(Edge e, Vector2 pt)
        {
            if (e.OutIndex < 0)
            {
                OutRec outRec = this.CreateOutRec();
                outRec.SourcePath = e.SourcePath; // copy source from edge to outrec
                outRec.IsOpen = e.WindingDelta == 0;
                OutPoint newOp = new OutPoint();
                outRec.Points = newOp;
                newOp.Index = outRec.Index;
                newOp.Point = pt;
                newOp.Next = newOp;
                newOp.Previous = newOp;
                if (!outRec.IsOpen)
                {
                    this.SetHoleState(e, outRec);
                }

                e.OutIndex = outRec.Index; // nb: do this after SetZ !
                return newOp;
            }
            else
            {
                OutRec outRec = this.polyOuts[e.OutIndex];

                if (outRec.SourcePath != e.SourcePath)
                {
                    // this edge was from a different/unknown source
                    outRec.SourcePath = null; // drop source form output
                }

                // OutRec.Pts is the 'Left-most' point & OutRec.Pts.Prev is the 'Right-most'
                OutPoint op = outRec.Points;
                bool toFront = e.Side == EdgeSide.Left;
                if (toFront && pt == op.Point)
                {
                    return op;
                }
                else if (!toFront && pt == op.Previous.Point)
                {
                    return op.Previous;
                }

                // do we need to move the source to the point???
                OutPoint newOp = new OutPoint();
                newOp.Index = outRec.Index;
                newOp.Point = pt;
                newOp.Next = op;
                newOp.Previous = op.Previous;
                newOp.Previous.Next = newOp;
                op.Previous = newOp;
                if (toFront)
                {
                    outRec.Points = newOp;
                }

                return newOp;
            }
        }

        private OutPoint GetLastOutPt(Edge e)
        {
            OutRec outRec = this.polyOuts[e.OutIndex];
            if (e.Side == EdgeSide.Left)
            {
                return outRec.Points;
            }
            else
            {
                return outRec.Points.Previous;
            }
        }

        private void SetHoleState(Edge e, OutRec outRec)
        {
            Edge e2 = e.PreviousInAEL;
            Edge tmpEdge = null;
            while (e2 != null)
            {
                if (e2.OutIndex >= 0 && e2.WindingDelta != 0)
                {
                    if (tmpEdge == null)
                    {
                        tmpEdge = e2;
                    }
                    else if (tmpEdge.OutIndex == e2.OutIndex)
                    {
                        tmpEdge = null; // paired
                    }
                }

                e2 = e2.PreviousInAEL;
            }

            if (tmpEdge == null)
            {
                outRec.FirstLeft = null;
                outRec.IsHole = false;
            }
            else
            {
                outRec.FirstLeft = this.polyOuts[tmpEdge.OutIndex];
                outRec.IsHole = !outRec.FirstLeft.IsHole;
            }
        }

        private bool FirstIsBottomPt(OutPoint btmPt1, OutPoint btmPt2)
        {
            OutPoint p = btmPt1.Previous;
            while ((p.Point == btmPt1.Point) && (p != btmPt1))
            {
                p = p.Previous;
            }

            double dx1p = Math.Abs(GetDx(btmPt1.Point, p.Point));
            p = btmPt1.Next;
            while ((p.Point == btmPt1.Point) && (p != btmPt1))
            {
                p = p.Next;
            }

            double dx1n = Math.Abs(GetDx(btmPt1.Point, p.Point));

            p = btmPt2.Previous;
            while ((p.Point == btmPt2.Point) && (p != btmPt2))
            {
                p = p.Previous;
            }

            double dx2p = Math.Abs(GetDx(btmPt2.Point, p.Point));
            p = btmPt2.Next;
            while ((p.Point == btmPt2.Point) && (p != btmPt2))
            {
                p = p.Next;
            }

            double dx2n = Math.Abs(GetDx(btmPt2.Point, p.Point));

            if (Math.Max(dx1p, dx1n) == Math.Max(dx2p, dx2n) &&
              Math.Min(dx1p, dx1n) == Math.Min(dx2p, dx2n))
            {
                return this.Area(btmPt1) > 0; // if otherwise identical use orientation
            }
            else
            {
                return (dx1p >= dx2p && dx1p >= dx2n) || (dx1n >= dx2p && dx1n >= dx2n);
            }
        }

        private OutPoint GetBottomPt(OutPoint pp)
        {
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
                    if (!this.FirstIsBottomPt(p, dups))
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

        private OutRec GetLowermostRec(OutRec outRec1, OutRec outRec2)
        {
            // work out which polygon fragment has the correct hole state ...
            if (outRec1.BottomPoint == null)
            {
                outRec1.BottomPoint = this.GetBottomPt(outRec1.Points);
            }

            if (outRec2.BottomPoint == null)
            {
                outRec2.BottomPoint = this.GetBottomPt(outRec2.Points);
            }

            OutPoint bPt1 = outRec1.BottomPoint;
            OutPoint bPt2 = outRec2.BottomPoint;
            if (bPt1.Point.Y > bPt2.Point.Y)
            {
                return outRec1;
            }
            else if (bPt1.Point.Y < bPt2.Point.Y)
            {
                return outRec2;
            }
            else if (bPt1.Point.X < bPt2.Point.X)
            {
                return outRec1;
            }
            else if (bPt1.Point.X > bPt2.Point.X)
            {
                return outRec2;
            }
            else if (bPt1.Next == bPt1)
            {
                return outRec2;
            }
            else if (bPt2.Next == bPt2)
            {
                return outRec1;
            }
            else if (this.FirstIsBottomPt(bPt1, bPt2))
            {
                return outRec1;
            }
            else
            {
                return outRec2;
            }
        }

        private bool OutRec1RightOfOutRec2(OutRec outRec1, OutRec outRec2)
        {
            do
            {
                outRec1 = outRec1.FirstLeft;
                if (outRec1 == outRec2)
                {
                    return true;
                }
            }
            while (outRec1 != null);

            return false;
        }

        private OutRec GetOutRec(int idx)
        {
            OutRec outrec = this.polyOuts[idx];
            while (outrec != this.polyOuts[outrec.Index])
            {
                outrec = this.polyOuts[outrec.Index];
            }

            return outrec;
        }

        private void AppendPolygon(Edge e1, Edge e2)
        {
            OutRec outRec1 = this.polyOuts[e1.OutIndex];
            OutRec outRec2 = this.polyOuts[e2.OutIndex];

            OutRec holeStateRec;
            if (this.OutRec1RightOfOutRec2(outRec1, outRec2))
            {
                holeStateRec = outRec2;
            }
            else if (this.OutRec1RightOfOutRec2(outRec2, outRec1))
            {
                holeStateRec = outRec1;
            }
            else
            {
                holeStateRec = this.GetLowermostRec(outRec1, outRec2);
            }

            // get the start and ends of both output polygons and
            // join E2 poly onto E1 poly and delete pointers to E2 ...
            OutPoint p1_lft = outRec1.Points;
            OutPoint p1_rt = p1_lft.Previous;
            OutPoint p2_lft = outRec2.Points;
            OutPoint p2_rt = p2_lft.Previous;

            // join e2 poly onto e1 poly and delete pointers to e2 ...
            if (e1.Side == EdgeSide.Left)
            {
                if (e2.Side == EdgeSide.Left)
                {
                    // z y x a b c
                    this.ReversePolyPtLinks(p2_lft);
                    p2_lft.Next = p1_lft;
                    p1_lft.Previous = p2_lft;
                    p1_rt.Next = p2_rt;
                    p2_rt.Previous = p1_rt;
                    outRec1.Points = p2_rt;
                }
                else
                {
                    // x y z a b c
                    p2_rt.Next = p1_lft;
                    p1_lft.Previous = p2_rt;
                    p2_lft.Previous = p1_rt;
                    p1_rt.Next = p2_lft;
                    outRec1.Points = p2_lft;
                }
            }
            else
            {
                if (e2.Side == EdgeSide.Right)
                {
                    // a b c z y x
                    this.ReversePolyPtLinks(p2_lft);
                    p1_rt.Next = p2_rt;
                    p2_rt.Previous = p1_rt;
                    p2_lft.Next = p1_lft;
                    p1_lft.Previous = p2_lft;
                }
                else
                {
                    // a b c x y z
                    p1_rt.Next = p2_lft;
                    p2_lft.Previous = p1_rt;
                    p1_lft.Previous = p2_rt;
                    p2_rt.Next = p1_lft;
                }
            }

            outRec1.BottomPoint = null;
            if (holeStateRec == outRec2)
            {
                if (outRec2.FirstLeft != outRec1)
                {
                    outRec1.FirstLeft = outRec2.FirstLeft;
                }

                outRec1.IsHole = outRec2.IsHole;
            }

            outRec2.Points = null;
            outRec2.BottomPoint = null;

            outRec2.FirstLeft = outRec1;

            int okIdx = e1.OutIndex;
            int obsoleteIdx = e2.OutIndex;

            e1.OutIndex = Unassigned; // nb: safe because we only get here via AddLocalMaxPoly
            e2.OutIndex = Unassigned;

            Edge e = this.activeEdges;
            while (e != null)
            {
                if (e.OutIndex == obsoleteIdx)
                {
                    e.OutIndex = okIdx;
                    e.Side = e1.Side;
                    break;
                }

                e = e.NextInAEL;
            }

            outRec2.Index = outRec1.Index;
        }

        private void ReversePolyPtLinks(OutPoint pp)
        {
            if (pp == null)
            {
                return;
            }

            OutPoint pp1;
            OutPoint pp2;
            pp1 = pp;
            do
            {
                pp2 = pp1.Next;
                pp1.Next = pp1.Previous;
                pp1.Previous = pp2;
                pp1 = pp2;
            }
            while (pp1 != pp);
        }

        private void IntersectEdges(Edge e1, Edge e2, Vector2 pt)
        {
            // e1 will be to the left of e2 BELOW the intersection. Therefore e1 is before
            // e2 in AEL except when e1 is being inserted at the intersection point ...
            bool e1Contributing = e1.OutIndex >= 0;
            bool e2Contributing = e2.OutIndex >= 0;

            // update winding counts...
            // assumes that e1 will be to the Right of e2 ABOVE the intersection
            if (e1.PolyType == e2.PolyType)
            {
                int oldE1WindCnt = e1.WindingCount;
                e1.WindingCount = e2.WindingCount;
                e2.WindingCount = oldE1WindCnt;
            }
            else
            {
                e1.WindingCountInOppositePolyType = (e1.WindingCountInOppositePolyType == 0) ? 1 : 0;
                e2.WindingCountInOppositePolyType = (e2.WindingCountInOppositePolyType == 0) ? 1 : 0;
            }

            int e1Wc = Math.Abs(e1.WindingCount);
            int e2Wc = Math.Abs(e2.WindingCount);

            if (e1Contributing && e2Contributing)
            {
                if ((e1Wc != 0 && e1Wc != 1) || (e2Wc != 0 && e2Wc != 1) ||
                  (e1.PolyType != e2.PolyType))
                {
                    this.AddLocalMaxPoly(e1, e2, pt);
                }
                else
                {
                    this.AddOutPt(e1, pt);
                    this.AddOutPt(e2, pt);
                    SwapSides(e1, e2);
                    SwapPolyIndexes(e1, e2);
                }
            }
            else if (e1Contributing)
            {
                if (e2Wc == 0 || e2Wc == 1)
                {
                    this.AddOutPt(e1, pt);
                    SwapSides(e1, e2);
                    SwapPolyIndexes(e1, e2);
                }
            }
            else if (e2Contributing)
            {
                if (e1Wc == 0 || e1Wc == 1)
                {
                    this.AddOutPt(e2, pt);
                    SwapSides(e1, e2);
                    SwapPolyIndexes(e1, e2);
                }
            }
            else if ((e1Wc == 0 || e1Wc == 1) && (e2Wc == 0 || e2Wc == 1))
            {
                // neither edge is currently contributing ...
                float e1Wc2 = Math.Abs(e1.WindingCountInOppositePolyType);
                float e2Wc2 = Math.Abs(e2.WindingCountInOppositePolyType);

                if (e1.PolyType != e2.PolyType)
                {
                    this.AddLocalMinPoly(e1, e2, pt);
                }
                else if (e1Wc == 1 && e2Wc == 1)
                {
                    if (((e1.PolyType == PolyType.Clip) && (e1Wc2 > 0) && (e2Wc2 > 0)) ||
                        ((e1.PolyType == PolyType.Subject) && (e1Wc2 <= 0) && (e2Wc2 <= 0)))
                    {
                        this.AddLocalMinPoly(e1, e2, pt);
                    }
                }
                else
                {
                    SwapSides(e1, e2);
                }
            }
        }

        private void ProcessHorizontals()
        {
            Edge horzEdge; // m_SortedEdges;
            while (this.PopEdgeFromSEL(out horzEdge))
            {
                this.ProcessHorizontal(horzEdge);
            }
        }

        private void GetHorzDirection(Edge horzEdge, out Direction dir, out float left, out float right)
        {
            if (horzEdge.Bottom.X < horzEdge.Top.X)
            {
                left = horzEdge.Bottom.X;
                right = horzEdge.Top.X;
                dir = Direction.LeftToRight;
            }
            else
            {
                left = horzEdge.Top.X;
                right = horzEdge.Bottom.X;
                dir = Direction.RightToLeft;
            }
        }

        private void ProcessHorizontal(Edge horzEdge)
        {
            Direction dir;
            float horzLeft, horzRight;
            bool isOpen = horzEdge.WindingDelta == 0;

            this.GetHorzDirection(horzEdge, out dir, out horzLeft, out horzRight);

            Edge eLastHorz = horzEdge, eMaxPair = null;
            while (eLastHorz.NextInLML != null && IsHorizontal(eLastHorz.NextInLML))
            {
                eLastHorz = eLastHorz.NextInLML;
            }

            if (eLastHorz.NextInLML == null)
            {
                eMaxPair = this.GetMaximaPair(eLastHorz);
            }

            Maxima currMax = this.maxima;
            if (currMax != null)
            {
                // get the first maxima in range (X) ...
                if (dir == Direction.LeftToRight)
                {
                    while (currMax != null && currMax.X <= horzEdge.Bottom.X)
                    {
                        currMax = currMax.Next;
                    }

                    if (currMax != null && currMax.X >= eLastHorz.Top.X)
                    {
                        currMax = null;
                    }
                }
                else
                {
                    while (currMax.Next != null && currMax.Next.X < horzEdge.Bottom.X)
                    {
                        currMax = currMax.Next;
                    }

                    if (currMax.X <= eLastHorz.Top.X)
                    {
                        currMax = null;
                    }
                }
            }

            OutPoint op1 = null;

            // loop through consec. horizontal edges
            while (true)
            {
                bool isLastHorz = horzEdge == eLastHorz;
                Edge e = this.GetNextInAEL(horzEdge, dir);
                while (e != null)
                {
                    // this code block inserts extra coords into horizontal edges (in output
                    // polygons) whereever maxima touch these horizontal edges. This helps
                    // 'simplifying' polygons (ie if the Simplify property is set).
                    if (currMax != null)
                    {
                        if (dir == Direction.LeftToRight)
                        {
                            while (currMax != null && currMax.X < e.Current.X)
                            {
                                if (horzEdge.OutIndex >= 0 && !isOpen)
                                {
                                    this.AddOutPt(horzEdge, new Vector2(currMax.X, horzEdge.Bottom.Y));
                                }

                                currMax = currMax.Next;
                            }
                        }
                        else
                        {
                            while (currMax != null && currMax.X > e.Current.X)
                            {
                                if (horzEdge.OutIndex >= 0 && !isOpen)
                                {
                                    this.AddOutPt(horzEdge, new Vector2(currMax.X, horzEdge.Bottom.Y));
                                }

                                currMax = currMax.Previous;
                            }
                        }
                    }

                    if ((dir == Direction.LeftToRight && e.Current.X > horzRight) ||
                      (dir == Direction.RightToLeft && e.Current.X < horzLeft))
                    {
                        break;
                    }

                    // Also break if we've got to the end of an intermediate horizontal edge ...
                    // nb: Smaller Dx's are to the right of larger Dx's ABOVE the horizontal.
                    if (e.Current.X == horzEdge.Top.X && horzEdge.NextInLML != null &&
                      e.Dx < horzEdge.NextInLML.Dx)
                    {
                        break;
                    }

                    // note: may be done multiple times
                    if (horzEdge.OutIndex >= 0 && !isOpen)
                    {
                        op1 = this.AddOutPt(horzEdge, e.Current);
                        Edge eNextHorz = this.sortedEdges;
                        while (eNextHorz != null)
                        {
                            if (eNextHorz.OutIndex >= 0 &&
                              HorizontalSegmentsOverlap(horzEdge.Bottom.X, horzEdge.Top.X, eNextHorz.Bottom.X, eNextHorz.Top.X))
                            {
                                OutPoint op2 = this.GetLastOutPt(eNextHorz);
                                this.AddJoin(op2, op1, eNextHorz.Top);
                            }

                            eNextHorz = eNextHorz.NextInSEL;
                        }

                        this.AddGhostJoin(op1, horzEdge.Bottom);
                    }

                    // OK, so far we're still in range of the horizontal Edge  but make sure
                    // we're at the last of consec. horizontals when matching with eMaxPair
                    if (e == eMaxPair && isLastHorz)
                    {
                        if (horzEdge.OutIndex >= 0)
                        {
                            this.AddLocalMaxPoly(horzEdge, eMaxPair, horzEdge.Top);
                        }

                        this.DeleteFromAEL(horzEdge);
                        this.DeleteFromAEL(eMaxPair);
                        return;
                    }

                    if (dir == Direction.LeftToRight)
                    {
                        Vector2 pt = new Vector2(e.Current.X, horzEdge.Current.Y);
                        this.IntersectEdges(horzEdge, e, pt);
                    }
                    else
                    {
                        Vector2 pt = new Vector2(e.Current.X, horzEdge.Current.Y);
                        this.IntersectEdges(e, horzEdge, pt);
                    }

                    Edge eNext = this.GetNextInAEL(e, dir);
                    this.SwapPositionsInAEL(horzEdge, e);
                    e = eNext;
                } // end while(e != null)

                // Break out of loop if HorzEdge.NextInLML is not also horizontal ...
                if (horzEdge.NextInLML == null || !IsHorizontal(horzEdge.NextInLML))
                {
                    break;
                }

                this.UpdateEdgeIntoAEL(ref horzEdge);
                if (horzEdge.OutIndex >= 0)
                {
                    this.AddOutPt(horzEdge, horzEdge.Bottom);
                }

                this.GetHorzDirection(horzEdge, out dir, out horzLeft, out horzRight);
            }

            if (horzEdge.OutIndex >= 0 && op1 == null)
            {
                op1 = this.GetLastOutPt(horzEdge);
                Edge eNextHorz = this.sortedEdges;
                while (eNextHorz != null)
                {
                    if (eNextHorz.OutIndex >= 0 &&
                      HorizontalSegmentsOverlap(horzEdge.Bottom.X, horzEdge.Top.X, eNextHorz.Bottom.X, eNextHorz.Top.X))
                    {
                        OutPoint op2 = this.GetLastOutPt(eNextHorz);
                        this.AddJoin(op2, op1, eNextHorz.Top);
                    }

                    eNextHorz = eNextHorz.NextInSEL;
                }

                this.AddGhostJoin(op1, horzEdge.Top);
            }

            if (horzEdge.NextInLML != null)
            {
                if (horzEdge.OutIndex >= 0)
                {
                    op1 = this.AddOutPt(horzEdge, horzEdge.Top);

                    this.UpdateEdgeIntoAEL(ref horzEdge);
                    if (horzEdge.WindingDelta == 0)
                    {
                        return;
                    }

                    // nb: HorzEdge is no longer horizontal here
                    Edge ePrev = horzEdge.PreviousInAEL;
                    Edge eNext = horzEdge.NextInAEL;
                    if (ePrev != null && ePrev.Current.X == horzEdge.Bottom.X &&
                      ePrev.Current.Y == horzEdge.Bottom.Y && ePrev.WindingDelta != 0 &&
                      (ePrev.OutIndex >= 0 && ePrev.Current.Y > ePrev.Top.Y &&
                      SlopesEqual(horzEdge, ePrev)))
                    {
                        OutPoint op2 = this.AddOutPt(ePrev, horzEdge.Bottom);
                        this.AddJoin(op1, op2, horzEdge.Top);
                    }
                    else if (eNext != null && eNext.Current.X == horzEdge.Bottom.X &&
                      eNext.Current.Y == horzEdge.Bottom.Y && eNext.WindingDelta != 0 &&
                      eNext.OutIndex >= 0 && eNext.Current.Y > eNext.Top.Y &&
                      SlopesEqual(horzEdge, eNext))
                    {
                        OutPoint op2 = this.AddOutPt(eNext, horzEdge.Bottom);
                        this.AddJoin(op1, op2, horzEdge.Top);
                    }
                }
                else
                {
                    this.UpdateEdgeIntoAEL(ref horzEdge);
                }
            }
            else
            {
                if (horzEdge.OutIndex >= 0)
                {
                    this.AddOutPt(horzEdge, horzEdge.Top);
                }

                this.DeleteFromAEL(horzEdge);
            }
        }

        private Edge GetNextInAEL(Edge e, Direction direction)
        {
            return direction == Direction.LeftToRight ? e.NextInAEL : e.PreviousInAEL;
        }

        private bool IsMaxima(Edge e, double y)
        {
            return e != null && e.Top.Y == y && e.NextInLML == null;
        }

        private bool IsIntermediate(Edge e, double y)
        {
            return e.Top.Y == y && e.NextInLML != null;
        }

        private Edge GetMaximaPair(Edge e)
        {
            if ((e.NextEdge.Top == e.Top) && e.NextEdge.NextInLML == null)
            {
                return e.NextEdge;
            }
            else if ((e.PreviousEdge.Top == e.Top) && e.PreviousEdge.NextInLML == null)
            {
                return e.PreviousEdge;
            }
            else
            {
                return null;
            }
        }

        private Edge GetMaximaPairEx(Edge e)
        {
            // as above but returns null if MaxPair isn't in AEL (unless it's horizontal)
            Edge result = this.GetMaximaPair(e);
            if (result == null || result.OutIndex == Skip ||
              ((result.NextInAEL == result.PreviousInAEL) && !IsHorizontal(result)))
            {
                return null;
            }

            return result;
        }

        private bool ProcessIntersections(float topY)
        {
            if (this.activeEdges == null)
            {
                return true;
            }

            try
            {
                this.BuildIntersectList(topY);
                if (this.intersectList.Count == 0)
                {
                    return true;
                }

                if (this.intersectList.Count == 1 || this.FixupIntersectionOrder())
                {
                    this.ProcessIntersectList();
                }
                else
                {
                    return false;
                }
            }
            catch
            {
                this.sortedEdges = null;
                this.intersectList.Clear();
                throw new ClipperException("ProcessIntersections error");
            }

            this.sortedEdges = null;
            return true;
        }

        private void BuildIntersectList(float topY)
        {
            if (this.activeEdges == null)
            {
                return;
            }

            // prepare for sorting ...
            Edge e = this.activeEdges;
            this.sortedEdges = e;
            while (e != null)
            {
                e.PreviousInSEL = e.PreviousInAEL;
                e.NextInSEL = e.NextInAEL;
                e.Current = new Vector2(TopX(e, topY), e.Current.Y);
                e = e.NextInAEL;
            }

            // bubblesort ...
            bool isModified = true;
            while (isModified && this.sortedEdges != null)
            {
                isModified = false;
                e = this.sortedEdges;
                while (e.NextInSEL != null)
                {
                    Edge eNext = e.NextInSEL;
                    Vector2 pt;
                    if (e.Current.X > eNext.Current.X)
                    {
                        this.IntersectPoint(e, eNext, out pt);
                        if (pt.Y < topY)
                        {
                            pt = new Vector2(TopX(e, topY), topY);
                        }

                        IntersectNode newNode = new IntersectNode();
                        newNode.Edge1 = e;
                        newNode.Edge2 = eNext;
                        newNode.Point = pt;
                        this.intersectList.Add(newNode);

                        this.SwapPositionsInSEL(e, eNext);
                        isModified = true;
                    }
                    else
                    {
                        e = eNext;
                    }
                }

                if (e.PreviousInSEL != null)
                {
                    e.PreviousInSEL.NextInSEL = null;
                }
                else
                {
                    break;
                }
            }

            this.sortedEdges = null;
        }

        private bool EdgesAdjacent(IntersectNode inode)
        {
            return (inode.Edge1.NextInSEL == inode.Edge2) ||
              (inode.Edge1.PreviousInSEL == inode.Edge2);
        }

        private bool FixupIntersectionOrder()
        {
            // pre-condition: intersections are sorted bottom-most first.
            // Now it's crucial that intersections are made only between adjacent edges,
            // so to ensure this the order of intersections may need adjusting ...
            this.intersectList.Sort(IntersectNodeComparer);

            this.CopyAELToSEL();
            int cnt = this.intersectList.Count;
            for (int i = 0; i < cnt; i++)
            {
                if (!this.EdgesAdjacent(this.intersectList[i]))
                {
                    int j = i + 1;
                    while (j < cnt && !this.EdgesAdjacent(this.intersectList[j]))
                    {
                        j++;
                    }

                    if (j == cnt)
                    {
                        return false;
                    }

                    IntersectNode tmp = this.intersectList[i];
                    this.intersectList[i] = this.intersectList[j];
                    this.intersectList[j] = tmp;
                }

                this.SwapPositionsInSEL(this.intersectList[i].Edge1, this.intersectList[i].Edge2);
            }

            return true;
        }

        private void ProcessIntersectList()
        {
            for (int i = 0; i < this.intersectList.Count; i++)
            {
                IntersectNode iNode = this.intersectList[i];
                {
                    this.IntersectEdges(iNode.Edge1, iNode.Edge2, iNode.Point);
                    this.SwapPositionsInAEL(iNode.Edge1, iNode.Edge2);
                }
            }

            this.intersectList.Clear();
        }

        private void IntersectPoint(Edge edge1, Edge edge2, out Vector2 ip)
        {
            ip = default(Vector2);
            double b1, b2;

            // nb: with very large coordinate values, it's possible for SlopesEqual() to
            // return false but for the edge.Dx value be equal due to double precision rounding.
            if (edge1.Dx == edge2.Dx)
            {
                ip.Y = edge1.Current.Y;
                ip.X = TopX(edge1, ip.Y);
                return;
            }

            if (edge1.Delta.X == 0)
            {
                ip.X = edge1.Bottom.X;
                if (IsHorizontal(edge2))
                {
                    ip.Y = edge2.Bottom.Y;
                }
                else
                {
                    b2 = edge2.Bottom.Y - (edge2.Bottom.X / edge2.Dx);
                    ip.Y = Round((ip.X / edge2.Dx) + b2);
                }
            }
            else if (edge2.Delta.X == 0)
            {
                ip.X = edge2.Bottom.X;
                if (IsHorizontal(edge1))
                {
                    ip.Y = edge1.Bottom.Y;
                }
                else
                {
                    b1 = edge1.Bottom.Y - (edge1.Bottom.X / edge1.Dx);
                    ip.Y = Round((ip.X / edge1.Dx) + b1);
                }
            }
            else
            {
                b1 = edge1.Bottom.X - (edge1.Bottom.Y * edge1.Dx);
                b2 = edge2.Bottom.X - (edge2.Bottom.Y * edge2.Dx);
                double q = (b2 - b1) / (edge1.Dx - edge2.Dx);
                ip.Y = Round(q);
                if (Math.Abs(edge1.Dx) < Math.Abs(edge2.Dx))
                {
                    ip.X = Round((edge1.Dx * q) + b1);
                }
                else
                {
                    ip.X = Round((edge2.Dx * q) + b2);
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
                    ip.X = TopX(edge1, ip.Y);
                }
                else
                {
                    ip.X = TopX(edge2, ip.Y);
                }
            }

            // finally, don't allow 'ip' to be BELOW curr.Y (ie bottom of scanbeam) ...
            if (ip.Y > edge1.Current.Y)
            {
                ip.Y = edge1.Current.Y;

                // better to use the more vertical edge to derive X ...
                if (Math.Abs(edge1.Dx) > Math.Abs(edge2.Dx))
                {
                    ip.X = TopX(edge2, ip.Y);
                }
                else
                {
                    ip.X = TopX(edge1, ip.Y);
                }
            }
        }

        private void ProcessEdgesAtTopOfScanbeam(float topY)
        {
            Edge e = this.activeEdges;
            while (e != null)
            {
                // 1. process maxima, treating them as if they're 'bent' horizontal edges,
                //   but exclude maxima with horizontal edges. nb: e can't be a horizontal.
                bool isMaximaEdge = this.IsMaxima(e, topY);

                if (isMaximaEdge)
                {
                    Edge eMaxPair = this.GetMaximaPairEx(e);
                    isMaximaEdge = eMaxPair == null || !IsHorizontal(eMaxPair);
                }

                if (isMaximaEdge)
                {
                    Edge ePrev = e.PreviousInAEL;
                    this.DoMaxima(e);
                    if (ePrev == null)
                    {
                        e = this.activeEdges;
                    }
                    else
                    {
                        e = ePrev.NextInAEL;
                    }
                }
                else
                {
                    // 2. promote horizontal edges, otherwise update Curr.X and Curr.Y ...
                    if (this.IsIntermediate(e, topY) && IsHorizontal(e.NextInLML))
                    {
                        this.UpdateEdgeIntoAEL(ref e);
                        if (e.OutIndex >= 0)
                        {
                            this.AddOutPt(e, e.Bottom);
                        }

                        this.AddEdgeToSEL(e);
                    }
                    else
                    {
                        e.Current = new Vector2(TopX(e, topY), topY);
                    }

                    e = e.NextInAEL;
                }
            }

            // 3. Process horizontals at the Top of the scanbeam ...
            this.ProcessHorizontals();
            this.maxima = null;

            // 4. Promote intermediate vertices ...
            e = this.activeEdges;
            while (e != null)
            {
                if (this.IsIntermediate(e, topY))
                {
                    OutPoint op = null;
                    if (e.OutIndex >= 0)
                    {
                        op = this.AddOutPt(e, e.Top);
                    }

                    this.UpdateEdgeIntoAEL(ref e);

                    // if output polygons share an edge, they'll need joining later ...
                    Edge ePrev = e.PreviousInAEL;
                    Edge eNext = e.NextInAEL;
                    if (ePrev != null && ePrev.Current.X == e.Bottom.X &&
                      ePrev.Current.Y == e.Bottom.Y && op != null &&
                      ePrev.OutIndex >= 0 && ePrev.Current.Y > ePrev.Top.Y &&
                      SlopesEqual(e.Current, e.Top, ePrev.Current, ePrev.Top) &&
                      (e.WindingDelta != 0) && (ePrev.WindingDelta != 0))
                    {
                        OutPoint op2 = this.AddOutPt(ePrev, e.Bottom);
                        this.AddJoin(op, op2, e.Top);
                    }
                    else if (eNext != null && eNext.Current.X == e.Bottom.X &&
                      eNext.Current.Y == e.Bottom.Y && op != null &&
                      eNext.OutIndex >= 0 && eNext.Current.Y > eNext.Top.Y &&
                      SlopesEqual(e.Current, e.Top, eNext.Current, eNext.Top) &&
                      (e.WindingDelta != 0) && (eNext.WindingDelta != 0))
                    {
                        OutPoint op2 = this.AddOutPt(eNext, e.Bottom);
                        this.AddJoin(op, op2, e.Top);
                    }
                }

                e = e.NextInAEL;
            }
        }

        private void DoMaxima(Edge e)
        {
            Edge eMaxPair = this.GetMaximaPairEx(e);
            if (eMaxPair == null)
            {
                if (e.OutIndex >= 0)
                {
                    this.AddOutPt(e, e.Top);
                }

                this.DeleteFromAEL(e);
                return;
            }

            Edge eNext = e.NextInAEL;
            while (eNext != null && eNext != eMaxPair)
            {
                this.IntersectEdges(e, eNext, e.Top);
                this.SwapPositionsInAEL(e, eNext);
                eNext = e.NextInAEL;
            }

            if (e.OutIndex == Unassigned && eMaxPair.OutIndex == Unassigned)
            {
                this.DeleteFromAEL(e);
                this.DeleteFromAEL(eMaxPair);
            }
            else if (e.OutIndex >= 0 && eMaxPair.OutIndex >= 0)
            {
                if (e.OutIndex >= 0)
                {
                    this.AddLocalMaxPoly(e, eMaxPair, e.Top);
                }

                this.DeleteFromAEL(e);
                this.DeleteFromAEL(eMaxPair);
            }
            else
            {
                throw new ClipperException("DoMaxima error");
            }
        }

        private int PointCount(OutPoint pts)
        {
            if (pts == null)
            {
                return 0;
            }

            int result = 0;
            OutPoint p = pts;
            do
            {
                result++;
                p = p.Next;
            }
            while (p != pts);
            return result;
        }

        private void BuildResult2(PolyTree polytree)
        {
            polytree.Clear();

            // add each output polygon/contour to polytree ...
            polytree.AllPolygonNodes.Capacity = this.polyOuts.Count;
            for (int i = 0; i < this.polyOuts.Count; i++)
            {
                OutRec outRec = this.polyOuts[i];
                int cnt = this.PointCount(outRec.Points);
                if ((outRec.IsOpen && cnt < 2) ||
                  (!outRec.IsOpen && cnt < 3))
                {
                    continue;
                }

                FixHoleLinkage(outRec);
                PolyNode pn = new PolyNode();
                pn.SourcePath = outRec.SourcePath;
                polytree.AllPolygonNodes.Add(pn);
                outRec.PolyNode = pn;
                pn.Contour.Capacity = cnt;
                OutPoint op = outRec.Points.Previous;
                for (int j = 0; j < cnt; j++)
                {
                    pn.Contour.Add(op.Point);
                    op = op.Previous;
                }
            }

            // fixup PolyNode links etc ...
            polytree.Children.Capacity = this.polyOuts.Count;
            for (int i = 0; i < this.polyOuts.Count; i++)
            {
                OutRec outRec = this.polyOuts[i];
                if (outRec.PolyNode == null)
                {
                    continue;
                }
                else if (outRec.IsOpen)
                {
                    polytree.AddChild(outRec.PolyNode);
                }
                else if (outRec.FirstLeft != null &&
                  outRec.FirstLeft.PolyNode != null)
                {
                    outRec.FirstLeft.PolyNode.AddChild(outRec.PolyNode);
                }
                else
                {
                    polytree.AddChild(outRec.PolyNode);
                }
            }
        }

        private void FixupOutPolyline(OutRec outrec)
        {
            OutPoint pp = outrec.Points;
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
                outrec.Points = null;
            }
        }

        private void FixupOutPolygon(OutRec outRec)
        {
            // FixupOutPolygon() - removes duplicate points and simplifies consecutive
            // parallel edges by removing the middle vertex.
            OutPoint lastOK = null;
            outRec.BottomPoint = null;
            OutPoint pp = outRec.Points;
            while (true)
            {
                if (pp.Previous == pp || pp.Previous == pp.Next)
                {
                    outRec.Points = null;
                    return;
                }

                // test for duplicate points and collinear edges ...
                if ((pp.Point == pp.Next.Point) || (pp.Point == pp.Previous.Point) ||
                  SlopesEqual(pp.Previous.Point, pp.Point, pp.Next.Point))
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

            outRec.Points = pp;
        }

        private OutPoint DupOutPt(OutPoint outPt, bool insertAfter)
        {
            OutPoint result = new OutPoint();
            result.Point = outPt.Point;
            result.Index = outPt.Index;
            if (insertAfter)
            {
                result.Next = outPt.Next;
                result.Previous = outPt;
                outPt.Next.Previous = result;
                outPt.Next = result;
            }
            else
            {
                result.Previous = outPt.Previous;
                result.Next = outPt;
                outPt.Previous.Next = result;
                outPt.Previous = result;
            }

            return result;
        }

        private bool GetOverlap(float a1, float a2, float b1, float b2, out float left, out float right)
        {
            if (a1 < a2)
            {
                if (b1 < b2)
                {
                    left = Math.Max(a1, b1);
                    right = Math.Min(a2, b2);
                }
                else
                {
                    left = Math.Max(a1, b2);
                    right = Math.Min(a2, b1);
                }
            }
            else
            {
                if (b1 < b2)
                {
                    left = Math.Max(a2, b1);
                    right = Math.Min(a1, b2);
                }
                else
                {
                    left = Math.Max(a2, b2);
                    right = Math.Min(a1, b1);
                }
            }

            return left < right;
        }

        private bool JoinHorz(OutPoint op1, OutPoint op1b, OutPoint op2, OutPoint op2b, Vector2 pt, bool discardLeft)
        {
            Direction dir1 = op1.Point.X > op1b.Point.X ? Direction.RightToLeft : Direction.LeftToRight;
            Direction dir2 = op2.Point.X > op2b.Point.X ? Direction.RightToLeft : Direction.LeftToRight;
            if (dir1 == dir2)
            {
                return false;
            }

            // When DiscardLeft, we want Op1b to be on the Left of Op1, otherwise we
            // want Op1b to be on the Right. (And likewise with Op2 and Op2b.)
            // So, to facilitate this while inserting Op1b and Op2b ...
            // when DiscardLeft, make sure we're AT or RIGHT of Pt before adding Op1b,
            // otherwise make sure we're AT or LEFT of Pt. (Likewise with Op2b.)
            if (dir1 == Direction.LeftToRight)
            {
                while (op1.Next.Point.X <= pt.X &&
                  op1.Next.Point.X >= op1.Point.X && op1.Next.Point.Y == pt.Y)
                {
                    op1 = op1.Next;
                }

                if (discardLeft && (op1.Point.X != pt.X))
                {
                    op1 = op1.Next;
                }

                op1b = this.DupOutPt(op1, !discardLeft);
                if (op1b.Point != pt)
                {
                    op1 = op1b;
                    op1.Point = pt;
                    op1b = this.DupOutPt(op1, !discardLeft);
                }
            }
            else
            {
                while (op1.Next.Point.X >= pt.X &&
                        op1.Next.Point.X <= op1.Point.X &&
                        op1.Next.Point.Y == pt.Y)
                {
                    op1 = op1.Next;
                }

                if (!discardLeft && (op1.Point.X != pt.X))
                {
                    op1 = op1.Next;
                }

                op1b = this.DupOutPt(op1, discardLeft);
                if (op1b.Point != pt)
                {
                    op1 = op1b;
                    op1.Point = pt;
                    op1b = this.DupOutPt(op1, discardLeft);
                }
            }

            if (dir2 == Direction.LeftToRight)
            {
                while (op2.Next.Point.X <= pt.X &&
                  op2.Next.Point.X >= op2.Point.X &&
                  op2.Next.Point.Y == pt.Y)
                {
                    op2 = op2.Next;
                }

                if (discardLeft && (op2.Point.X != pt.X))
                {
                    op2 = op2.Next;
                }

                op2b = this.DupOutPt(op2, !discardLeft);
                if (op2b.Point != pt)
                {
                    op2 = op2b;
                    op2.Point = pt;
                    op2b = this.DupOutPt(op2, !discardLeft);
                }
            }
            else
            {
                while (op2.Next.Point.X >= pt.X &&
                  op2.Next.Point.X <= op2.Point.X &&
                  op2.Next.Point.Y == pt.Y)
                {
                    op2 = op2.Next;
                }

                if (!discardLeft && (op2.Point.X != pt.X))
                {
                    op2 = op2.Next;
                }

                op2b = this.DupOutPt(op2, discardLeft);
                if (op2b.Point != pt)
                {
                    op2 = op2b;
                    op2.Point = pt;
                    op2b = this.DupOutPt(op2, discardLeft);
                }
            }

            if ((dir1 == Direction.LeftToRight) == discardLeft)
            {
                op1.Previous = op2;
                op2.Next = op1;
                op1b.Next = op2b;
                op2b.Previous = op1b;
            }
            else
            {
                op1.Next = op2;
                op2.Previous = op1;
                op1b.Previous = op2b;
                op2b.Next = op1b;
            }

            return true;
        }

        private bool JoinPoints(Join j, OutRec outRec1, OutRec outRec2)
        {
            OutPoint op1 = j.OutPoint1, op1b;
            OutPoint op2 = j.OutPoint2, op2b;

            // There are 3 kinds of joins for output polygons ...
            // 1. Horizontal joins where Join.OutPt1 & Join.OutPt2 are vertices anywhere
            // along (horizontal) collinear edges (& Join.OffPt is on the same horizontal).
            // 2. Non-horizontal joins where Join.OutPt1 & Join.OutPt2 are at the same
            // location at the Bottom of the overlapping segment (& Join.OffPt is above).
            // 3. StrictlySimple joins where edges touch but are not collinear and where
            // Join.OutPt1, Join.OutPt2 & Join.OffPt all share the same point.
            bool isHorizontal = j.OutPoint1.Point.Y == j.OffPoint.Y;

            if (isHorizontal && (j.OffPoint == j.OutPoint1.Point) && (j.OffPoint == j.OutPoint2.Point))
            {
                // Strictly Simple join ...
                if (outRec1 != outRec2)
                {
                    return false;
                }

                op1b = j.OutPoint1.Next;
                while (op1b != op1 && (op1b.Point == j.OffPoint))
                {
                    op1b = op1b.Next;
                }

                bool reverse1 = op1b.Point.Y > j.OffPoint.Y;
                op2b = j.OutPoint2.Next;
                while (op2b != op2 && (op2b.Point == j.OffPoint))
                {
                    op2b = op2b.Next;
                }

                bool reverse2 = op2b.Point.Y > j.OffPoint.Y;
                if (reverse1 == reverse2)
                {
                    return false;
                }

                if (reverse1)
                {
                    op1b = this.DupOutPt(op1, false);
                    op2b = this.DupOutPt(op2, true);
                    op1.Previous = op2;
                    op2.Next = op1;
                    op1b.Next = op2b;
                    op2b.Previous = op1b;
                    j.OutPoint1 = op1;
                    j.OutPoint2 = op1b;
                    return true;
                }
                else
                {
                    op1b = this.DupOutPt(op1, true);
                    op2b = this.DupOutPt(op2, false);
                    op1.Next = op2;
                    op2.Previous = op1;
                    op1b.Previous = op2b;
                    op2b.Next = op1b;
                    j.OutPoint1 = op1;
                    j.OutPoint2 = op1b;
                    return true;
                }
            }
            else if (isHorizontal)
            {
                // treat horizontal joins differently to non-horizontal joins since with
                // them we're not yet sure where the overlapping is. OutPt1.Pt & OutPt2.Pt
                // may be anywhere along the horizontal edge.
                op1b = op1;
                while (op1.Previous.Point.Y == op1.Point.Y && op1.Previous != op1b && op1.Previous != op2)
                {
                    op1 = op1.Previous;
                }

                while (op1b.Next.Point.Y == op1b.Point.Y && op1b.Next != op1 && op1b.Next != op2)
                {
                    op1b = op1b.Next;
                }

                if (op1b.Next == op1 || op1b.Next == op2)
                {
                    return false; // a flat 'polygon'
                }

                op2b = op2;
                while (op2.Previous.Point.Y == op2.Point.Y && op2.Previous != op2b && op2.Previous != op1b)
                {
                    op2 = op2.Previous;
                }

                while (op2b.Next.Point.Y == op2b.Point.Y && op2b.Next != op2 && op2b.Next != op1)
                {
                    op2b = op2b.Next;
                }

                if (op2b.Next == op2 || op2b.Next == op1)
                {
                    return false; // a flat 'polygon'
                }

                float left, right;

                // Op1 -. Op1b & Op2 -. Op2b are the extremites of the horizontal edges
                if (!this.GetOverlap(op1.Point.X, op1b.Point.X, op2.Point.X, op2b.Point.X, out left, out right))
                {
                    return false;
                }

                // DiscardLeftSide: when overlapping edges are joined, a spike will created
                // which needs to be cleaned up. However, we don't want Op1 or Op2 caught up
                // on the discard Side as either may still be needed for other joins ...
                Vector2 pt;
                bool discardLeftSide;
                if (op1.Point.X >= left && op1.Point.X <= right)
                {
                    pt = op1.Point;
                    discardLeftSide = op1.Point.X > op1b.Point.X;
                }
                else if (op2.Point.X >= left && op2.Point.X <= right)
                {
                    pt = op2.Point;
                    discardLeftSide = op2.Point.X > op2b.Point.X;
                }
                else if (op1b.Point.X >= left && op1b.Point.X <= right)
                {
                    pt = op1b.Point;
                    discardLeftSide = op1b.Point.X > op1.Point.X;
                }
                else
                {
                    pt = op2b.Point;
                    discardLeftSide = op2b.Point.X > op2.Point.X;
                }

                j.OutPoint1 = op1;
                j.OutPoint2 = op2;
                return this.JoinHorz(op1, op1b, op2, op2b, pt, discardLeftSide);
            }
            else
            {
                // nb: For non-horizontal joins ...
                //    1. Jr.OutPt1.Pt.Y == Jr.OutPt2.Pt.Y
                //    2. Jr.OutPt1.Pt > Jr.OffPt.Y

                // make sure the polygons are correctly oriented ...
                op1b = op1.Next;
                while ((op1b.Point == op1.Point) && (op1b != op1))
                {
                    op1b = op1b.Next;
                }

                bool reverse1 = (op1b.Point.Y > op1.Point.Y) || !SlopesEqual(op1.Point, op1b.Point, j.OffPoint);
                if (reverse1)
                {
                    op1b = op1.Previous;
                    while ((op1b.Point == op1.Point) && (op1b != op1))
                    {
                        op1b = op1b.Previous;
                    }

                    if ((op1b.Point.Y > op1.Point.Y) ||
                      !SlopesEqual(op1.Point, op1b.Point, j.OffPoint))
                    {
                        return false;
                    }
                }

                op2b = op2.Next;
                while ((op2b.Point == op2.Point) && (op2b != op2))
                {
                    op2b = op2b.Next;
                }

                bool reverse2 = (op2b.Point.Y > op2.Point.Y) || !SlopesEqual(op2.Point, op2b.Point, j.OffPoint);
                if (reverse2)
                {
                    op2b = op2.Previous;
                    while ((op2b.Point == op2.Point) && (op2b != op2))
                    {
                        op2b = op2b.Previous;
                    }

                    if ((op2b.Point.Y > op2.Point.Y) ||
                      !SlopesEqual(op2.Point, op2b.Point, j.OffPoint))
                    {
                        return false;
                    }
                }

                if ((op1b == op1) || (op2b == op2) || (op1b == op2b) ||
                  ((outRec1 == outRec2) && (reverse1 == reverse2)))
                {
                    return false;
                }

                if (reverse1)
                {
                    op1b = this.DupOutPt(op1, false);
                    op2b = this.DupOutPt(op2, true);
                    op1.Previous = op2;
                    op2.Next = op1;
                    op1b.Next = op2b;
                    op2b.Previous = op1b;
                    j.OutPoint1 = op1;
                    j.OutPoint2 = op1b;
                    return true;
                }
                else
                {
                    op1b = this.DupOutPt(op1, true);
                    op2b = this.DupOutPt(op2, false);
                    op1.Next = op2;
                    op2.Previous = op1;
                    op1b.Previous = op2b;
                    op2b.Next = op1b;
                    j.OutPoint1 = op1;
                    j.OutPoint2 = op1b;
                    return true;
                }
            }
        }

        private void FixupFirstLefts1(OutRec oldOutRec, OutRec newOutRec)
        {
            foreach (OutRec outRec in this.polyOuts)
            {
                OutRec firstLeft = ParseFirstLeft(outRec.FirstLeft);
                if (outRec.Points != null && firstLeft == oldOutRec)
                {
                    if (Poly2ContainsPoly1(outRec.Points, newOutRec.Points))
                    {
                        outRec.FirstLeft = newOutRec;
                    }
                }
            }
        }

        private void FixupFirstLefts2(OutRec innerOutRec, OutRec outerOutRec)
        {
            // A polygon has split into two such that one is now the inner of the other.
            // It's possible that these polygons now wrap around other polygons, so check
            // every polygon that's also contained by OuterOutRec's FirstLeft container
            // (including nil) to see if they've become inner to the new inner polygon ...
            OutRec orfl = outerOutRec.FirstLeft;
            foreach (OutRec outRec in this.polyOuts)
            {
                if (outRec.Points == null || outRec == outerOutRec || outRec == innerOutRec)
                {
                    continue;
                }

                OutRec firstLeft = ParseFirstLeft(outRec.FirstLeft);
                if (firstLeft != orfl && firstLeft != innerOutRec && firstLeft != outerOutRec)
                {
                    continue;
                }

                if (Poly2ContainsPoly1(outRec.Points, innerOutRec.Points))
                {
                    outRec.FirstLeft = innerOutRec;
                }
                else if (Poly2ContainsPoly1(outRec.Points, outerOutRec.Points))
                {
                    outRec.FirstLeft = outerOutRec;
                }
                else if (outRec.FirstLeft == innerOutRec || outRec.FirstLeft == outerOutRec)
                {
                    outRec.FirstLeft = orfl;
                }
            }
        }

        private void FixupFirstLefts3(OutRec oldOutRec, OutRec newOutRec)
        {
            // same as FixupFirstLefts1 but doesn't call Poly2ContainsPoly1()
            foreach (OutRec outRec in this.polyOuts)
            {
                OutRec firstLeft = ParseFirstLeft(outRec.FirstLeft);
                if (outRec.Points != null && outRec.FirstLeft == oldOutRec)
                {
                    outRec.FirstLeft = newOutRec;
                }
            }
        }

        private void JoinCommonEdges()
        {
            for (int i = 0; i < this.joins.Count; i++)
            {
                Join join = this.joins[i];

                OutRec outRec1 = this.GetOutRec(join.OutPoint1.Index);
                OutRec outRec2 = this.GetOutRec(join.OutPoint2.Index);

                if (outRec1.Points == null || outRec2.Points == null)
                {
                    continue;
                }

                if (outRec1.IsOpen || outRec2.IsOpen)
                {
                    continue;
                }

                // get the polygon fragment with the correct hole state (FirstLeft)
                // before calling JoinPoints() ...
                OutRec holeStateRec;
                if (outRec1 == outRec2)
                {
                    holeStateRec = outRec1;
                }
                else if (this.OutRec1RightOfOutRec2(outRec1, outRec2))
                {
                    holeStateRec = outRec2;
                }
                else if (this.OutRec1RightOfOutRec2(outRec2, outRec1))
                {
                    holeStateRec = outRec1;
                }
                else
                {
                    holeStateRec = this.GetLowermostRec(outRec1, outRec2);
                }

                if (!this.JoinPoints(join, outRec1, outRec2))
                {
                    continue;
                }

                if (outRec1 == outRec2)
                {
                    // instead of joining two polygons, we've just created a new one by
                    // splitting one polygon into two.
                    outRec1.Points = join.OutPoint1;
                    outRec1.BottomPoint = null;
                    outRec2 = this.CreateOutRec();
                    outRec2.Points = join.OutPoint2;

                    // update all OutRec2.Pts Idx's ...
                    this.UpdateOutPtIdxs(outRec2);

                    if (Poly2ContainsPoly1(outRec2.Points, outRec1.Points))
                    {
                        // outRec1 contains outRec2 ...
                        outRec2.IsHole = !outRec1.IsHole;
                        outRec2.FirstLeft = outRec1;
                        this.FixupFirstLefts2(outRec2, outRec1);
                    }
                    else if (Poly2ContainsPoly1(outRec1.Points, outRec2.Points))
                    {
                        // outRec2 contains outRec1 ...
                        outRec2.IsHole = outRec1.IsHole;
                        outRec1.IsHole = !outRec2.IsHole;
                        outRec2.FirstLeft = outRec1.FirstLeft;
                        outRec1.FirstLeft = outRec2;
                        this.FixupFirstLefts2(outRec1, outRec2);
                    }
                    else
                    {
                        // the 2 polygons are completely separate ...
                        outRec2.IsHole = outRec1.IsHole;
                        outRec2.FirstLeft = outRec1.FirstLeft;
                        this.FixupFirstLefts1(outRec1, outRec2);
                    }
                }
                else
                {
                    // joined 2 polygons together ...
                    outRec2.Points = null;
                    outRec2.BottomPoint = null;
                    outRec2.Index = outRec1.Index;

                    outRec1.IsHole = holeStateRec.IsHole;
                    if (holeStateRec == outRec2)
                    {
                        outRec1.FirstLeft = outRec2.FirstLeft;
                    }

                    outRec2.FirstLeft = outRec1;

                    this.FixupFirstLefts3(outRec2, outRec1);
                }
            }
        }

        private void UpdateOutPtIdxs(OutRec outrec)
        {
            OutPoint op = outrec.Points;
            do
            {
                op.Index = outrec.Index;
                op = op.Previous;
            }
            while (op != outrec.Points);
        }

        private void DoSimplePolygons()
        {
            int i = 0;
            while (i < this.polyOuts.Count)
            {
                OutRec outrec = this.polyOuts[i++];
                OutPoint op = outrec.Points;
                if (op == null || outrec.IsOpen)
                {
                    continue;
                }

                do
                {
                    // for each Pt in Polygon until duplicate found do ...
                    OutPoint op2 = op.Next;
                    while (op2 != outrec.Points)
                    {
                        if ((op.Point == op2.Point) && op2.Next != op && op2.Previous != op)
                        {
                            // split the polygon into two ...
                            OutPoint op3 = op.Previous;
                            OutPoint op4 = op2.Previous;
                            op.Previous = op4;
                            op4.Next = op;
                            op2.Previous = op3;
                            op3.Next = op2;

                            outrec.Points = op;
                            OutRec outrec2 = this.CreateOutRec();
                            outrec2.Points = op2;
                            this.UpdateOutPtIdxs(outrec2);
                            if (Poly2ContainsPoly1(outrec2.Points, outrec.Points))
                            {
                                // OutRec2 is contained by OutRec1 ...
                                outrec2.IsHole = !outrec.IsHole;
                                outrec2.FirstLeft = outrec;
                                this.FixupFirstLefts2(outrec2, outrec);
                            }
                            else if (Poly2ContainsPoly1(outrec.Points, outrec2.Points))
                            {
                                // OutRec1 is contained by OutRec2 ...
                                outrec2.IsHole = outrec.IsHole;
                                outrec.IsHole = !outrec2.IsHole;
                                outrec2.FirstLeft = outrec.FirstLeft;
                                outrec.FirstLeft = outrec2;
                                this.FixupFirstLefts2(outrec, outrec2);
                            }
                            else
                            {
                                // the 2 polygons are separate ...
                                outrec2.IsHole = outrec.IsHole;
                                outrec2.FirstLeft = outrec.FirstLeft;
                                this.FixupFirstLefts1(outrec, outrec2);
                            }

                            op2 = op; // ie get ready for the next iteration
                        }

                        op2 = op2.Next;
                    }

                    op = op.Next;
                }
                while (op != outrec.Points);
            }
        }

        private double Area(OutPoint op)
        {
            OutPoint opFirst = op;
            if (op == null)
            {
                return 0;
            }

            double a = 0;
            do
            {
                a = a + ((op.Previous.Point.X + op.Point.X) * (op.Previous.Point.Y - op.Point.Y));
                op = op.Next;
            }
            while (op != opFirst);

            return a * 0.5;
        }

        private void SetDx(Edge e)
        {
            e.Delta = new Vector2(e.Top.X - e.Bottom.X, e.Top.Y - e.Bottom.Y);
            if (e.Delta.Y == 0)
            {
                e.Dx = HorizontalDeltaLimit;
            }
            else
            {
                e.Dx = e.Delta.X / e.Delta.Y;
            }
        }

        private void InsertLocalMinima(LocalMinima newLm)
        {
            if (this.minimaList == null)
            {
                this.minimaList = newLm;
            }
            else if (newLm.Y >= this.minimaList.Y)
            {
                newLm.Next = this.minimaList;
                this.minimaList = newLm;
            }
            else
            {
                LocalMinima tmpLm = this.minimaList;
                while (tmpLm.Next != null && (newLm.Y < tmpLm.Next.Y))
                {
                    tmpLm = tmpLm.Next;
                }

                newLm.Next = tmpLm.Next;
                tmpLm.Next = newLm;
            }
        }

        private bool PopLocalMinima(float y, out LocalMinima current)
        {
            current = this.currentLocalMinima;
            if (this.currentLocalMinima != null && this.currentLocalMinima.Y == y)
            {
                this.currentLocalMinima = this.currentLocalMinima.Next;
                return true;
            }

            return false;
        }

        private void Reset()
        {
            this.currentLocalMinima = this.minimaList;
            if (this.currentLocalMinima == null)
            {
                return; // ie nothing to process
            }

            // reset all edges ...
            this.scanbeam = null;
            LocalMinima lm = this.minimaList;
            while (lm != null)
            {
                this.InsertScanbeam(lm.Y);
                Edge e = lm.LeftBound;
                if (e != null)
                {
                    e.Current = e.Bottom;
                    e.OutIndex = Unassigned;
                }

                e = lm.RightBound;
                if (e != null)
                {
                    e.Current = e.Bottom;
                    e.OutIndex = Unassigned;
                }

                lm = lm.Next;
            }

            this.activeEdges = null;
        }

        private void InsertScanbeam(float y)
        {
            // single-linked list: sorted descending, ignoring dups.
            if (this.scanbeam == null)
            {
                this.scanbeam = new Scanbeam();
                this.scanbeam.Next = null;
                this.scanbeam.Y = y;
            }
            else if (y > this.scanbeam.Y)
            {
                Scanbeam newSb = new Scanbeam();
                newSb.Y = y;
                newSb.Next = this.scanbeam;
                this.scanbeam = newSb;
            }
            else
            {
                Scanbeam sb2 = this.scanbeam;
                while (sb2.Next != null && (y <= sb2.Next.Y))
                {
                    sb2 = sb2.Next;
                }

                if (y == sb2.Y)
                {
                    return; // ie ignores duplicates
                }

                Scanbeam newSb = new Scanbeam();
                newSb.Y = y;
                newSb.Next = sb2.Next;
                sb2.Next = newSb;
            }
        }

        private bool PopScanbeam(out float y)
        {
            if (this.scanbeam == null)
            {
                y = 0;
                return false;
            }

            y = this.scanbeam.Y;
            this.scanbeam = this.scanbeam.Next;
            return true;
        }

        private bool LocalMinimaPending()
        {
            return this.currentLocalMinima != null;
        }

        private OutRec CreateOutRec()
        {
            OutRec result = new OutRec();
            result.Index = Unassigned;
            result.IsHole = false;
            result.IsOpen = false;
            result.FirstLeft = null;
            result.Points = null;
            result.BottomPoint = null;
            result.PolyNode = null;
            this.polyOuts.Add(result);
            result.Index = this.polyOuts.Count - 1;
            return result;
        }

        private void UpdateEdgeIntoAEL(ref Edge e)
        {
            if (e.NextInLML == null)
            {
                throw new ClipperException("UpdateEdgeIntoAEL: invalid call");
            }

            Edge aelPrev = e.PreviousInAEL;
            Edge aelNext = e.NextInAEL;
            e.NextInLML.OutIndex = e.OutIndex;
            if (aelPrev != null)
            {
                aelPrev.NextInAEL = e.NextInLML;
            }
            else
            {
                this.activeEdges = e.NextInLML;
            }

            if (aelNext != null)
            {
                aelNext.PreviousInAEL = e.NextInLML;
            }

            e.NextInLML.Side = e.Side;
            e.NextInLML.WindingDelta = e.WindingDelta;
            e.NextInLML.WindingCount = e.WindingCount;
            e.NextInLML.WindingCountInOppositePolyType = e.WindingCountInOppositePolyType;
            e = e.NextInLML;
            e.Current = e.Bottom;
            e.PreviousInAEL = aelPrev;
            e.NextInAEL = aelNext;
            if (!IsHorizontal(e))
            {
                this.InsertScanbeam(e.Top.Y);
            }
        }

        private void SwapPositionsInAEL(Edge edge1, Edge edge2)
        {
            // check that one or other edge hasn't already been removed from AEL ...
            if (edge1.NextInAEL == edge1.PreviousInAEL ||
              edge2.NextInAEL == edge2.PreviousInAEL)
            {
                return;
            }

            if (edge1.NextInAEL == edge2)
            {
                Edge next = edge2.NextInAEL;
                if (next != null)
                {
                    next.PreviousInAEL = edge1;
                }

                Edge prev = edge1.PreviousInAEL;
                if (prev != null)
                {
                    prev.NextInAEL = edge2;
                }

                edge2.PreviousInAEL = prev;
                edge2.NextInAEL = edge1;
                edge1.PreviousInAEL = edge2;
                edge1.NextInAEL = next;
            }
            else if (edge2.NextInAEL == edge1)
            {
                Edge next = edge1.NextInAEL;
                if (next != null)
                {
                    next.PreviousInAEL = edge2;
                }

                Edge prev = edge2.PreviousInAEL;
                if (prev != null)
                {
                    prev.NextInAEL = edge1;
                }

                edge1.PreviousInAEL = prev;
                edge1.NextInAEL = edge2;
                edge2.PreviousInAEL = edge1;
                edge2.NextInAEL = next;
            }
            else
            {
                Edge next = edge1.NextInAEL;
                Edge prev = edge1.PreviousInAEL;
                edge1.NextInAEL = edge2.NextInAEL;
                if (edge1.NextInAEL != null)
                {
                    edge1.NextInAEL.PreviousInAEL = edge1;
                }

                edge1.PreviousInAEL = edge2.PreviousInAEL;
                if (edge1.PreviousInAEL != null)
                {
                    edge1.PreviousInAEL.NextInAEL = edge1;
                }

                edge2.NextInAEL = next;
                if (edge2.NextInAEL != null)
                {
                    edge2.NextInAEL.PreviousInAEL = edge2;
                }

                edge2.PreviousInAEL = prev;
                if (edge2.PreviousInAEL != null)
                {
                    edge2.PreviousInAEL.NextInAEL = edge2;
                }
            }

            if (edge1.PreviousInAEL == null)
            {
                this.activeEdges = edge1;
            }
            else if (edge2.PreviousInAEL == null)
            {
                this.activeEdges = edge2;
            }
        }

        private void DeleteFromAEL(Edge e)
        {
            Edge aelPrev = e.PreviousInAEL;
            Edge aelNext = e.NextInAEL;
            if (aelPrev == null && aelNext == null && (e != this.activeEdges))
            {
                return; // already deleted
            }

            if (aelPrev != null)
            {
                aelPrev.NextInAEL = aelNext;
            }
            else
            {
                this.activeEdges = aelNext;
            }

            if (aelNext != null)
            {
                aelNext.PreviousInAEL = aelPrev;
            }

            e.NextInAEL = null;
            e.PreviousInAEL = null;
        }

        private void InitEdge2(Edge e, PolyType polyType)
        {
            if (e.Current.Y >= e.NextEdge.Current.Y)
            {
                e.Bottom = e.Current;
                e.Top = e.NextEdge.Current;
            }
            else
            {
                e.Top = e.Current;
                e.Bottom = e.NextEdge.Current;
            }

            this.SetDx(e);
            e.PolyType = polyType;
        }

        private Edge ProcessBound(Edge edge, bool leftBoundIsForward)
        {
            Edge eStart, result = edge;
            Edge horz;

            if (result.OutIndex == Skip)
            {
                // check if there are edges beyond the skip edge in the bound and if so
                // create another LocMin and calling ProcessBound once more ...
                edge = result;
                if (leftBoundIsForward)
                {
                    while (edge.Top.Y == edge.NextEdge.Bottom.Y)
                    {
                        edge = edge.NextEdge;
                    }

                    while (edge != result && edge.Dx == HorizontalDeltaLimit)
                    {
                        edge = edge.PreviousEdge;
                    }
                }
                else
                {
                    while (edge.Top.Y == edge.PreviousEdge.Bottom.Y)
                    {
                        edge = edge.PreviousEdge;
                    }

                    while (edge != result && edge.Dx == HorizontalDeltaLimit)
                    {
                        edge = edge.NextEdge;
                    }
                }

                if (edge == result)
                {
                    if (leftBoundIsForward)
                    {
                        result = edge.NextEdge;
                    }
                    else
                    {
                        result = edge.PreviousEdge;
                    }
                }
                else
                {
                    // there are more edges in the bound beyond result starting with E
                    if (leftBoundIsForward)
                    {
                        edge = result.NextEdge;
                    }
                    else
                    {
                        edge = result.PreviousEdge;
                    }

                    LocalMinima locMin = new LocalMinima();
                    locMin.Next = null;
                    locMin.Y = edge.Bottom.Y;
                    locMin.LeftBound = null;
                    locMin.RightBound = edge;
                    edge.WindingDelta = 0;
                    result = this.ProcessBound(edge, leftBoundIsForward);
                    this.InsertLocalMinima(locMin);
                }

                return result;
            }

            if (edge.Dx == HorizontalDeltaLimit)
            {
                // We need to be careful with open paths because this may not be a
                // true local minima (ie E may be following a skip edge).
                // Also, consecutive horz. edges may start heading left before going right.
                if (leftBoundIsForward)
                {
                    eStart = edge.PreviousEdge;
                }
                else
                {
                    eStart = edge.NextEdge;
                }

                // ie an adjoining horizontal skip edge
                if (eStart.Dx == HorizontalDeltaLimit)
                {
                    if (eStart.Bottom.X != edge.Bottom.X && eStart.Top.X != edge.Bottom.X)
                    {
                        ReverseHorizontal(edge);
                    }
                }
                else if (eStart.Bottom.X != edge.Bottom.X)
                {
                    ReverseHorizontal(edge);
                }
            }

            eStart = edge;
            if (leftBoundIsForward)
            {
                while (result.Top.Y == result.NextEdge.Bottom.Y && result.NextEdge.OutIndex != Skip)
                {
                    result = result.NextEdge;
                }

                if (result.Dx == HorizontalDeltaLimit && result.NextEdge.OutIndex != Skip)
                {
                    // nb: at the top of a bound, horizontals are added to the bound
                    // only when the preceding edge attaches to the horizontal's left vertex
                    // unless a Skip edge is encountered when that becomes the top divide
                    horz = result;
                    while (horz.PreviousEdge.Dx == HorizontalDeltaLimit)
                    {
                        horz = horz.PreviousEdge;
                    }

                    if (horz.PreviousEdge.Top.X > result.NextEdge.Top.X)
                    {
                        result = horz.PreviousEdge;
                    }
                }

                while (edge != result)
                {
                    edge.NextInLML = edge.NextEdge;
                    if (edge.Dx == HorizontalDeltaLimit && edge != eStart && edge.Bottom.X != edge.PreviousEdge.Top.X)
                    {
                        ReverseHorizontal(edge);
                    }

                    edge = edge.NextEdge;
                }

                if (edge.Dx == HorizontalDeltaLimit && edge != eStart && edge.Bottom.X != edge.PreviousEdge.Top.X)
                {
                    ReverseHorizontal(edge);
                }

                result = result.NextEdge; // move to the edge just beyond current bound
            }
            else
            {
                while (result.Top.Y == result.PreviousEdge.Bottom.Y && result.PreviousEdge.OutIndex != Skip)
                {
                    result = result.PreviousEdge;
                }

                if (result.Dx == HorizontalDeltaLimit && result.PreviousEdge.OutIndex != Skip)
                {
                    horz = result;
                    while (horz.NextEdge.Dx == HorizontalDeltaLimit)
                    {
                        horz = horz.NextEdge;
                    }

                    if (horz.NextEdge.Top.X == result.PreviousEdge.Top.X || horz.NextEdge.Top.X > result.PreviousEdge.Top.X)
                    {
                        result = horz.NextEdge;
                    }
                }

                while (edge != result)
                {
                    edge.NextInLML = edge.PreviousEdge;
                    if (edge.Dx == HorizontalDeltaLimit && edge != eStart && edge.Bottom.X != edge.NextEdge.Top.X)
                    {
                        ReverseHorizontal(edge);
                    }

                    edge = edge.PreviousEdge;
                }

                if (edge.Dx == HorizontalDeltaLimit && edge != eStart && edge.Bottom.X != edge.NextEdge.Top.X)
                {
                    ReverseHorizontal(edge);
                }

                result = result.PreviousEdge; // move to the edge just beyond current bound
            }

            return result;
        }
    }
}