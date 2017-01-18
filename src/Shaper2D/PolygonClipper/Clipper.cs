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
        private LocalMinima currentLM;
        private Scanbeam scanbeam = null;
        private Edge activeEdges = null;

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
                return false;
            }

            // create a new edge array ...
            List<Edge> edges = new List<Edge>(hi + 1);
            for (int i = 0; i <= hi; i++)
            {
                edges.Add(new Edge() { SourcePath = path });
            }

            bool isFlat = true;

            // 1. Basic (first) edge initialization ...
            edges[1].Curr = points[1];

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
                // nb: allows matching start and end points when not Closed ...
                if (edge.Curr == edge.NextEdge.Curr)
                {
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

                if (edge.PreviousEdge == edge.NextEdge)
                {
                    break; // only two vertices
                }

                if (SlopesEqual(edge.PreviousEdge.Curr, edge.Curr, edge.NextEdge.Curr))
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
                if (isFlat && edge.Curr.Y != startEdge.Curr.Y)
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
            if (edge.PreviousEdge.Bot == edge.PreviousEdge.Top)
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
                LocalMinima locMin = new LocalMinima();
                locMin.Next = null;
                locMin.Y = edge.Bot.Y;
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
                    locMin.LeftBound.WindindDelta = -1;
                }
                else
                {
                    locMin.LeftBound.WindindDelta = 1;
                }

                locMin.RightBound.WindindDelta = -locMin.LeftBound.WindindDelta;

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
        /// Returns the <see cref="PolyTree" /> containing the converted polygons.
        /// </returns>
        public PolyTree Execute()
        {
            PolyTree polytree = new PolyTree();
            
            bool succeeded = this.ExecuteInternal();

            // build the return polygons ...
            if (succeeded)
            {
                this.BuildResult2(polytree);
            }

            return polytree;
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

            return edge.Bot.X + Round(edge.Dx * (currentY - edge.Bot.Y));
        }

        private static double DistanceFromLineSqrd(Vector2 pt, Vector2 ln1, Vector2 ln2)
        {
            // The equation of a line in general form (Ax + By + C = 0)
            // given 2 points (x¹,y¹) & (x²,y²) is ...
            // (y¹ - y²)x + (x² - x¹)y + (y² - y¹)x¹ - (x² - x¹)y¹ = 0
            // A = (y¹ - y²); B = (x² - x¹); C = (y² - y¹)x¹ - (x² - x¹)y¹
            // perpendicular distance of point (x³,y³) = (Ax³ + By³ + C)/Sqrt(A² + B²)
            // see http://en.wikipedia.org/wiki/Perpendicular_distance
            double a = ln1.Y - ln2.Y;
            double b = ln2.X - ln1.X;
            double c = (a * ln1.X) + (b * ln1.Y);
            c = (a * pt.X) + (b * pt.Y) - c;
            return (c * c) / ((a * a) + (b * b));
        }

        private static void FixHoleLinkage(OutRec outRec)
        {
            // skip if an outermost polygon or
            // already already points to the correct FirstLeft ...
            if (outRec.FirstLeft == null ||
                  (outRec.IsHole != outRec.FirstLeft.IsHole &&
                  outRec.FirstLeft.Pts != null))
            {
                return;
            }

            OutRec orfl = outRec.FirstLeft;
            while (orfl != null && ((orfl.IsHole == outRec.IsHole) || orfl.Pts == null))
            {
                orfl = orfl.FirstLeft;
            }

            outRec.FirstLeft = orfl;
        }

        // See "The Point in Polygon Problem for Arbitrary Polygons" by Hormann & Agathos
        // http://citeseerx.ist.psu.edu/viewdoc/download?doi=10.1.1.88.5498&rep=rep1&type=pdf
        private static int PointInPolygon(Vector2 pt, OutPt op)
        {
            // returns 0 if false, +1 if true, -1 if pt ON polygon boundary
            int result = 0;
            OutPt startOp = op;
            float ptx = pt.X;
            float pty = pt.Y;
            float poly0x = op.Pt.X;
            float poly0y = op.Pt.Y;
            do
            {
                op = op.Next;
                float poly1x = op.Pt.X;
                float poly1y = op.Pt.Y;

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

        private static bool Poly2ContainsPoly1(OutPt outPt1, OutPt outPt2)
        {
            OutPt op = outPt1;
            do
            {
                // nb: PointInPolygon returns 0 if false, +1 if true, -1 if pt on polygon
                int res = PointInPolygon(op.Pt, outPt2);
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

        private static bool HorzSegmentsOverlap(float seg1a, float seg1b, float seg2a, float seg2b)
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
                while (edge.Bot != edge.PreviousEdge.Bot || edge.Curr == edge.Top)
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

                if (edge.Top.Y == edge.PreviousEdge.Bot.Y)
                {
                    continue; // ie just an intermediate horz.
                }

                if (edge2.PreviousEdge.Bot.X < edge.Bot.X)
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
            e.Curr = pt;
            e.OutIndex = Unassigned;
        }

        private static OutRec ParseFirstLeft(OutRec firstLeft)
        {
            while (firstLeft != null && firstLeft.Pts == null)
            {
                firstLeft = firstLeft.FirstLeft;
            }

            return firstLeft;
        }

        private static bool Pt2IsBetweenPt1AndPt3(Vector2 pt1, Vector2 pt2, Vector2 pt3)
        {
            if ((pt1 == pt3) || (pt1 == pt2) || (pt3 == pt2))
            {
                return false;
            }
            else if (pt1.X != pt3.X)
            {
                return (pt2.X > pt1.X) == (pt2.X < pt3.X);
            }
            else
            {
                return (pt2.Y > pt1.Y) == (pt2.Y < pt3.Y);
            }
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
            var b = e.Bot;
            Swap(ref t.X, ref b.X);
            e.Top = t;
            e.Bot = b;

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
                    if (outRec.Pts == null || outRec.IsOpen)
                    {
                        continue;
                    }
                }

                this.JoinCommonEdges();

                foreach (OutRec outRec in this.polyOuts)
                {
                    if (outRec.Pts == null)
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

        private void AddJoin(OutPt op1, OutPt op2, Vector2 offPt)
        {
            Join j = new Join();
            j.OutPt1 = op1;
            j.OutPt2 = op2;
            j.OffPt = offPt;
            this.joins.Add(j);
        }

        private void AddGhostJoin(OutPt op, Vector2 offPt)
        {
            Join j = new Join();
            j.OutPt1 = op;
            j.OffPt = offPt;
            this.ghostJoins.Add(j);
        }

        private void InsertLocalMinimaIntoAEL(float botY)
        {
            LocalMinima lm;
            while (this.PopLocalMinima(botY, out lm))
            {
                Edge lb = lm.LeftBound;
                Edge rb = lm.RightBound;

                OutPt op1 = null;
                if (lb == null)
                {
                    this.InsertEdgeIntoAEL(rb, null);
                    this.SetWindingCount(rb);
                    if (this.IsContributing(rb))
                    {
                        op1 = this.AddOutPt(rb, rb.Bot);
                    }
                }
                else if (rb == null)
                {
                    this.InsertEdgeIntoAEL(lb, null);
                    this.SetWindingCount(lb);
                    if (this.IsContributing(lb))
                    {
                        op1 = this.AddOutPt(lb, lb.Bot);
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
                        op1 = this.AddLocalMinPoly(lb, rb, lb.Bot);
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
                 this.ghostJoins.Count > 0 && rb.WindindDelta != 0)
                {
                    for (int i = 0; i < this.ghostJoins.Count; i++)
                    {
                        // if the horizontal Rb and a 'ghost' horizontal overlap, then convert
                        // the 'ghost' join to a real join ready for later ...
                        Join j = this.ghostJoins[i];
                        if (HorzSegmentsOverlap(j.OutPt1.Pt.X, j.OffPt.X, rb.Bot.X, rb.Top.X))
                        {
                            this.AddJoin(j.OutPt1, op1, j.OffPt);
                        }
                    }
                }

                if (lb.OutIndex >= 0 && lb.PreviousInAEL != null &&
                  lb.PreviousInAEL.Curr.X == lb.Bot.X &&
                  lb.PreviousInAEL.OutIndex >= 0 &&
                  SlopesEqual(lb.PreviousInAEL.Curr, lb.PreviousInAEL.Top, lb.Curr, lb.Top) &&
                  lb.WindindDelta != 0 && lb.PreviousInAEL.WindindDelta != 0)
                {
                    OutPt op2 = this.AddOutPt(lb.PreviousInAEL, lb.Bot);
                    this.AddJoin(op1, op2, lb.Top);
                }

                if (lb.NextInAEL != rb)
                {
                    if (rb.OutIndex >= 0 && rb.PreviousInAEL.OutIndex >= 0 &&
                      SlopesEqual(rb.PreviousInAEL.Curr, rb.PreviousInAEL.Top, rb.Curr, rb.Top) &&
                      rb.WindindDelta != 0 && rb.PreviousInAEL.WindindDelta != 0)
                    {
                        OutPt op2 = this.AddOutPt(rb.PreviousInAEL, rb.Bot);
                        this.AddJoin(op1, op2, rb.Top);
                    }

                    Edge e = lb.NextInAEL;
                    if (e != null)
                    {
                        while (e != rb)
                        {
                            // nb: For calculating winding counts etc, IntersectEdges() assumes
                            // that param1 will be to the right of param2 ABOVE the intersection ...
                            this.IntersectEdges(rb, e, lb.Curr); // order important here
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
            if (e2.Curr.X == e1.Curr.X)
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
                return e2.Curr.X < e1.Curr.X;
            }
        }

        private bool IsContributing(Edge edge)
        {
            // return false if a subj line has been flagged as inside a subj polygon
            if (edge.WindindDelta == 0 && edge.WindingCount != 1)
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
            while (e != null && ((e.PolyType != edge.PolyType) || (e.WindindDelta == 0)))
            {
                e = e.PreviousInAEL;
            }

            if (e == null)
            {
                if (edge.WindindDelta == 0)
                {
                    edge.WindingCount = 1;
                }
                else
                {
                    edge.WindingCount = edge.WindindDelta;
                }

                edge.WindingCountInOppositePolyType = 0;
                e = this.activeEdges; // ie get ready to calc WindCnt2
            }
            else if (edge.WindindDelta == 0)
            {
                edge.WindingCount = 1;
                edge.WindingCountInOppositePolyType = e.WindingCountInOppositePolyType;
                e = e.NextInAEL; // ie get ready to calc WindCnt2
            }
            else
            {
                // EvenOdd filling ...
                if (edge.WindindDelta == 0)
                {
                    // are we inside a subj polygon ...
                    bool inside = true;
                    Edge e2 = e.PreviousInAEL;
                    while (e2 != null)
                    {
                        if (e2.PolyType == e.PolyType && e2.WindindDelta != 0)
                        {
                            inside = !inside;
                        }

                        e2 = e2.PreviousInAEL;
                    }

                    edge.WindingCount = inside ? 0 : 1;
                }
                else
                {
                    edge.WindingCount = edge.WindindDelta;
                }

                edge.WindingCountInOppositePolyType = e.WindingCountInOppositePolyType;
                e = e.NextInAEL; // ie get ready to calc WindCnt2
            }

            // update WindCnt2 ...
            // EvenOdd filling ...
            while (e != edge)
            {
                if (e.WindindDelta != 0)
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
            if (e2.WindindDelta == 0)
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

        private OutPt AddLocalMinPoly(Edge e1, Edge e2, Vector2 pt)
        {
            OutPt result;
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
                    (e.WindindDelta != 0) &&
                    (prevE.WindindDelta != 0) &&
                    SlopesEqual(new Vector2(xPrev, pt.Y), prevE.Top, new Vector2(xE, pt.Y), e.Top))
                {
                    OutPt outPt = this.AddOutPt(prevE, pt);
                    this.AddJoin(result, outPt, e.Top);
                }
            }

            return result;
        }

        private OutPt AddOutPt(Edge e, Vector2 pt)
        {
            if (e.OutIndex < 0)
            {
                OutRec outRec = this.CreateOutRec();
                outRec.SourcePath = e.SourcePath; // copy source from edge to outrec
                outRec.IsOpen = e.WindindDelta == 0;
                OutPt newOp = new OutPt();
                outRec.Pts = newOp;
                newOp.Idx = outRec.Idx;
                newOp.Pt = pt;
                newOp.Next = newOp;
                newOp.Prev = newOp;
                if (!outRec.IsOpen)
                {
                    this.SetHoleState(e, outRec);
                }

                e.OutIndex = outRec.Idx; // nb: do this after SetZ !
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
                OutPt op = outRec.Pts;
                bool toFront = e.Side == EdgeSide.Left;
                if (toFront && pt == op.Pt)
                {
                    return op;
                }
                else if (!toFront && pt == op.Prev.Pt)
                {
                    return op.Prev;
                }

                // do we need to move the source to the point???
                OutPt newOp = new OutPt();
                newOp.Idx = outRec.Idx;
                newOp.Pt = pt;
                newOp.Next = op;
                newOp.Prev = op.Prev;
                newOp.Prev.Next = newOp;
                op.Prev = newOp;
                if (toFront)
                {
                    outRec.Pts = newOp;
                }

                return newOp;
            }
        }

        private OutPt GetLastOutPt(Edge e)
        {
            OutRec outRec = this.polyOuts[e.OutIndex];
            if (e.Side == EdgeSide.Left)
            {
                return outRec.Pts;
            }
            else
            {
                return outRec.Pts.Prev;
            }
        }

        private void SetHoleState(Edge e, OutRec outRec)
        {
            Edge e2 = e.PreviousInAEL;
            Edge eTmp = null;
            while (e2 != null)
            {
                if (e2.OutIndex >= 0 && e2.WindindDelta != 0)
                {
                    if (eTmp == null)
                    {
                        eTmp = e2;
                    }
                    else if (eTmp.OutIndex == e2.OutIndex)
                    {
                        eTmp = null; // paired
                    }
                }

                e2 = e2.PreviousInAEL;
            }

            if (eTmp == null)
            {
                outRec.FirstLeft = null;
                outRec.IsHole = false;
            }
            else
            {
                outRec.FirstLeft = this.polyOuts[eTmp.OutIndex];
                outRec.IsHole = !outRec.FirstLeft.IsHole;
            }
        }

        private bool FirstIsBottomPt(OutPt btmPt1, OutPt btmPt2)
        {
            OutPt p = btmPt1.Prev;
            while ((p.Pt == btmPt1.Pt) && (p != btmPt1))
            {
                p = p.Prev;
            }

            double dx1p = Math.Abs(GetDx(btmPt1.Pt, p.Pt));
            p = btmPt1.Next;
            while ((p.Pt == btmPt1.Pt) && (p != btmPt1))
            {
                p = p.Next;
            }

            double dx1n = Math.Abs(GetDx(btmPt1.Pt, p.Pt));

            p = btmPt2.Prev;
            while ((p.Pt == btmPt2.Pt) && (p != btmPt2))
            {
                p = p.Prev;
            }

            double dx2p = Math.Abs(GetDx(btmPt2.Pt, p.Pt));
            p = btmPt2.Next;
            while ((p.Pt == btmPt2.Pt) && (p != btmPt2))
            {
                p = p.Next;
            }

            double dx2n = Math.Abs(GetDx(btmPt2.Pt, p.Pt));

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

        private OutPt GetBottomPt(OutPt pp)
        {
            OutPt dups = null;
            OutPt p = pp.Next;
            while (p != pp)
            {
                if (p.Pt.Y > pp.Pt.Y)
                {
                    pp = p;
                    dups = null;
                }
                else if (p.Pt.Y == pp.Pt.Y && p.Pt.X <= pp.Pt.X)
                {
                    if (p.Pt.X < pp.Pt.X)
                    {
                        dups = null;
                        pp = p;
                    }
                    else
                    {
                        if (p.Next != pp && p.Prev != pp)
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
                    while (dups.Pt != pp.Pt)
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
            if (outRec1.BottomPt == null)
            {
                outRec1.BottomPt = this.GetBottomPt(outRec1.Pts);
            }

            if (outRec2.BottomPt == null)
            {
                outRec2.BottomPt = this.GetBottomPt(outRec2.Pts);
            }

            OutPt bPt1 = outRec1.BottomPt;
            OutPt bPt2 = outRec2.BottomPt;
            if (bPt1.Pt.Y > bPt2.Pt.Y)
            {
                return outRec1;
            }
            else if (bPt1.Pt.Y < bPt2.Pt.Y)
            {
                return outRec2;
            }
            else if (bPt1.Pt.X < bPt2.Pt.X)
            {
                return outRec1;
            }
            else if (bPt1.Pt.X > bPt2.Pt.X)
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
            while (outrec != this.polyOuts[outrec.Idx])
            {
                outrec = this.polyOuts[outrec.Idx];
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
            OutPt p1_lft = outRec1.Pts;
            OutPt p1_rt = p1_lft.Prev;
            OutPt p2_lft = outRec2.Pts;
            OutPt p2_rt = p2_lft.Prev;

            // join e2 poly onto e1 poly and delete pointers to e2 ...
            if (e1.Side == EdgeSide.Left)
            {
                if (e2.Side == EdgeSide.Left)
                {
                    // z y x a b c
                    this.ReversePolyPtLinks(p2_lft);
                    p2_lft.Next = p1_lft;
                    p1_lft.Prev = p2_lft;
                    p1_rt.Next = p2_rt;
                    p2_rt.Prev = p1_rt;
                    outRec1.Pts = p2_rt;
                }
                else
                {
                    // x y z a b c
                    p2_rt.Next = p1_lft;
                    p1_lft.Prev = p2_rt;
                    p2_lft.Prev = p1_rt;
                    p1_rt.Next = p2_lft;
                    outRec1.Pts = p2_lft;
                }
            }
            else
            {
                if (e2.Side == EdgeSide.Right)
                {
                    // a b c z y x
                    this.ReversePolyPtLinks(p2_lft);
                    p1_rt.Next = p2_rt;
                    p2_rt.Prev = p1_rt;
                    p2_lft.Next = p1_lft;
                    p1_lft.Prev = p2_lft;
                }
                else
                {
                    // a b c x y z
                    p1_rt.Next = p2_lft;
                    p2_lft.Prev = p1_rt;
                    p1_lft.Prev = p2_rt;
                    p2_rt.Next = p1_lft;
                }
            }

            outRec1.BottomPt = null;
            if (holeStateRec == outRec2)
            {
                if (outRec2.FirstLeft != outRec1)
                {
                    outRec1.FirstLeft = outRec2.FirstLeft;
                }

                outRec1.IsHole = outRec2.IsHole;
            }

            outRec2.Pts = null;
            outRec2.BottomPt = null;

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

            outRec2.Idx = outRec1.Idx;
        }

        private void ReversePolyPtLinks(OutPt pp)
        {
            if (pp == null)
            {
                return;
            }

            OutPt pp1;
            OutPt pp2;
            pp1 = pp;
            do
            {
                pp2 = pp1.Next;
                pp1.Next = pp1.Prev;
                pp1.Prev = pp2;
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

            int e1Wc, e2Wc;
            e1Wc = Math.Abs(e1.WindingCount);
            e2Wc = Math.Abs(e2.WindingCount);

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
                float e1Wc2, e2Wc2;

                e1Wc2 = Math.Abs(e1.WindingCountInOppositePolyType);
                e2Wc2 = Math.Abs(e2.WindingCountInOppositePolyType);

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
            if (horzEdge.Bot.X < horzEdge.Top.X)
            {
                left = horzEdge.Bot.X;
                right = horzEdge.Top.X;
                dir = Direction.LeftToRight;
            }
            else
            {
                left = horzEdge.Top.X;
                right = horzEdge.Bot.X;
                dir = Direction.RightToLeft;
            }
        }

        private void ProcessHorizontal(Edge horzEdge)
        {
            Direction dir;
            float horzLeft, horzRight;
            bool isOpen = horzEdge.WindindDelta == 0;

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
                    while (currMax != null && currMax.X <= horzEdge.Bot.X)
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
                    while (currMax.Next != null && currMax.Next.X < horzEdge.Bot.X)
                    {
                        currMax = currMax.Next;
                    }

                    if (currMax.X <= eLastHorz.Top.X)
                    {
                        currMax = null;
                    }
                }
            }

            OutPt op1 = null;

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
                            while (currMax != null && currMax.X < e.Curr.X)
                            {
                                if (horzEdge.OutIndex >= 0 && !isOpen)
                                {
                                    this.AddOutPt(horzEdge, new Vector2(currMax.X, horzEdge.Bot.Y));
                                }

                                currMax = currMax.Next;
                            }
                        }
                        else
                        {
                            while (currMax != null && currMax.X > e.Curr.X)
                            {
                                if (horzEdge.OutIndex >= 0 && !isOpen)
                                {
                                    this.AddOutPt(horzEdge, new Vector2(currMax.X, horzEdge.Bot.Y));
                                }

                                currMax = currMax.Prev;
                            }
                        }
                    }

                    if ((dir == Direction.LeftToRight && e.Curr.X > horzRight) ||
                      (dir == Direction.RightToLeft && e.Curr.X < horzLeft))
                    {
                        break;
                    }

                    // Also break if we've got to the end of an intermediate horizontal edge ...
                    // nb: Smaller Dx's are to the right of larger Dx's ABOVE the horizontal.
                    if (e.Curr.X == horzEdge.Top.X && horzEdge.NextInLML != null &&
                      e.Dx < horzEdge.NextInLML.Dx)
                    {
                        break;
                    }

                    // note: may be done multiple times
                    if (horzEdge.OutIndex >= 0 && !isOpen)
                    {
                        op1 = this.AddOutPt(horzEdge, e.Curr);
                        Edge eNextHorz = this.sortedEdges;
                        while (eNextHorz != null)
                        {
                            if (eNextHorz.OutIndex >= 0 &&
                              HorzSegmentsOverlap(horzEdge.Bot.X, horzEdge.Top.X, eNextHorz.Bot.X, eNextHorz.Top.X))
                            {
                                OutPt op2 = this.GetLastOutPt(eNextHorz);
                                this.AddJoin(op2, op1, eNextHorz.Top);
                            }

                            eNextHorz = eNextHorz.NextInSEL;
                        }

                        this.AddGhostJoin(op1, horzEdge.Bot);
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
                        Vector2 pt = new Vector2(e.Curr.X, horzEdge.Curr.Y);
                        this.IntersectEdges(horzEdge, e, pt);
                    }
                    else
                    {
                        Vector2 pt = new Vector2(e.Curr.X, horzEdge.Curr.Y);
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
                    this.AddOutPt(horzEdge, horzEdge.Bot);
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
                      HorzSegmentsOverlap(horzEdge.Bot.X, horzEdge.Top.X, eNextHorz.Bot.X, eNextHorz.Top.X))
                    {
                        OutPt op2 = this.GetLastOutPt(eNextHorz);
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
                    if (horzEdge.WindindDelta == 0)
                    {
                        return;
                    }

                    // nb: HorzEdge is no longer horizontal here
                    Edge ePrev = horzEdge.PreviousInAEL;
                    Edge eNext = horzEdge.NextInAEL;
                    if (ePrev != null && ePrev.Curr.X == horzEdge.Bot.X &&
                      ePrev.Curr.Y == horzEdge.Bot.Y && ePrev.WindindDelta != 0 &&
                      (ePrev.OutIndex >= 0 && ePrev.Curr.Y > ePrev.Top.Y &&
                      SlopesEqual(horzEdge, ePrev)))
                    {
                        OutPt op2 = this.AddOutPt(ePrev, horzEdge.Bot);
                        this.AddJoin(op1, op2, horzEdge.Top);
                    }
                    else if (eNext != null && eNext.Curr.X == horzEdge.Bot.X &&
                      eNext.Curr.Y == horzEdge.Bot.Y && eNext.WindindDelta != 0 &&
                      eNext.OutIndex >= 0 && eNext.Curr.Y > eNext.Top.Y &&
                      SlopesEqual(horzEdge, eNext))
                    {
                        OutPt op2 = this.AddOutPt(eNext, horzEdge.Bot);
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
                e.Curr = new Vector2(TopX(e, topY), e.Curr.Y);
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
                    if (e.Curr.X > eNext.Curr.X)
                    {
                        this.IntersectPoint(e, eNext, out pt);
                        if (pt.Y < topY)
                        {
                            pt = new Vector2(TopX(e, topY), topY);
                        }

                        IntersectNode newNode = new IntersectNode();
                        newNode.Edge1 = e;
                        newNode.Edge2 = eNext;
                        newNode.Pt = pt;
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
                    this.IntersectEdges(iNode.Edge1, iNode.Edge2, iNode.Pt);
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
                ip.Y = edge1.Curr.Y;
                ip.X = TopX(edge1, ip.Y);
                return;
            }

            if (edge1.Delta.X == 0)
            {
                ip.X = edge1.Bot.X;
                if (IsHorizontal(edge2))
                {
                    ip.Y = edge2.Bot.Y;
                }
                else
                {
                    b2 = edge2.Bot.Y - (edge2.Bot.X / edge2.Dx);
                    ip.Y = Round((ip.X / edge2.Dx) + b2);
                }
            }
            else if (edge2.Delta.X == 0)
            {
                ip.X = edge2.Bot.X;
                if (IsHorizontal(edge1))
                {
                    ip.Y = edge1.Bot.Y;
                }
                else
                {
                    b1 = edge1.Bot.Y - (edge1.Bot.X / edge1.Dx);
                    ip.Y = Round((ip.X / edge1.Dx) + b1);
                }
            }
            else
            {
                b1 = edge1.Bot.X - (edge1.Bot.Y * edge1.Dx);
                b2 = edge2.Bot.X - (edge2.Bot.Y * edge2.Dx);
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
            if (ip.Y > edge1.Curr.Y)
            {
                ip.Y = edge1.Curr.Y;

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
                            this.AddOutPt(e, e.Bot);
                        }

                        this.AddEdgeToSEL(e);
                    }
                    else
                    {
                        e.Curr = new Vector2(TopX(e, topY), topY);
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
                    OutPt op = null;
                    if (e.OutIndex >= 0)
                    {
                        op = this.AddOutPt(e, e.Top);
                    }

                    this.UpdateEdgeIntoAEL(ref e);

                    // if output polygons share an edge, they'll need joining later ...
                    Edge ePrev = e.PreviousInAEL;
                    Edge eNext = e.NextInAEL;
                    if (ePrev != null && ePrev.Curr.X == e.Bot.X &&
                      ePrev.Curr.Y == e.Bot.Y && op != null &&
                      ePrev.OutIndex >= 0 && ePrev.Curr.Y > ePrev.Top.Y &&
                      SlopesEqual(e.Curr, e.Top, ePrev.Curr, ePrev.Top) &&
                      (e.WindindDelta != 0) && (ePrev.WindindDelta != 0))
                    {
                        OutPt op2 = this.AddOutPt(ePrev, e.Bot);
                        this.AddJoin(op, op2, e.Top);
                    }
                    else if (eNext != null && eNext.Curr.X == e.Bot.X &&
                      eNext.Curr.Y == e.Bot.Y && op != null &&
                      eNext.OutIndex >= 0 && eNext.Curr.Y > eNext.Top.Y &&
                      SlopesEqual(e.Curr, e.Top, eNext.Curr, eNext.Top) &&
                      (e.WindindDelta != 0) && (eNext.WindindDelta != 0))
                    {
                        OutPt op2 = this.AddOutPt(eNext, e.Bot);
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

        private int PointCount(OutPt pts)
        {
            if (pts == null)
            {
                return 0;
            }

            int result = 0;
            OutPt p = pts;
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
            polytree.AllPolys.Capacity = this.polyOuts.Count;
            for (int i = 0; i < this.polyOuts.Count; i++)
            {
                OutRec outRec = this.polyOuts[i];
                int cnt = this.PointCount(outRec.Pts);
                if ((outRec.IsOpen && cnt < 2) ||
                  (!outRec.IsOpen && cnt < 3))
                {
                    continue;
                }

                FixHoleLinkage(outRec);
                PolyNode pn = new PolyNode();
                pn.SourcePath = outRec.SourcePath;
                polytree.AllPolys.Add(pn);
                outRec.PolyNode = pn;
                pn.Polygon.Capacity = cnt;
                OutPt op = outRec.Pts.Prev;
                for (int j = 0; j < cnt; j++)
                {
                    pn.Polygon.Add(op.Pt);
                    op = op.Prev;
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
                    outRec.PolyNode.IsOpen = true;
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
            OutPt pp = outrec.Pts;
            OutPt lastPP = pp.Prev;
            while (pp != lastPP)
            {
                pp = pp.Next;
                if (pp.Pt == pp.Prev.Pt)
                {
                    if (pp == lastPP)
                    {
                        lastPP = pp.Prev;
                    }

                    OutPt tmpPP = pp.Prev;
                    tmpPP.Next = pp.Next;
                    pp.Next.Prev = tmpPP;
                    pp = tmpPP;
                }
            }

            if (pp == pp.Prev)
            {
                outrec.Pts = null;
            }
        }

        private void FixupOutPolygon(OutRec outRec)
        {
            // FixupOutPolygon() - removes duplicate points and simplifies consecutive
            // parallel edges by removing the middle vertex.
            OutPt lastOK = null;
            outRec.BottomPt = null;
            OutPt pp = outRec.Pts;
            while (true)
            {
                if (pp.Prev == pp || pp.Prev == pp.Next)
                {
                    outRec.Pts = null;
                    return;
                }

                // test for duplicate points and collinear edges ...
                if ((pp.Pt == pp.Next.Pt) || (pp.Pt == pp.Prev.Pt) ||
                  SlopesEqual(pp.Prev.Pt, pp.Pt, pp.Next.Pt))
                {
                    lastOK = null;
                    pp.Prev.Next = pp.Next;
                    pp.Next.Prev = pp.Prev;
                    pp = pp.Prev;
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

            outRec.Pts = pp;
        }

        private OutPt DupOutPt(OutPt outPt, bool insertAfter)
        {
            OutPt result = new OutPt();
            result.Pt = outPt.Pt;
            result.Idx = outPt.Idx;
            if (insertAfter)
            {
                result.Next = outPt.Next;
                result.Prev = outPt;
                outPt.Next.Prev = result;
                outPt.Next = result;
            }
            else
            {
                result.Prev = outPt.Prev;
                result.Next = outPt;
                outPt.Prev.Next = result;
                outPt.Prev = result;
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

        private bool JoinHorz(OutPt op1, OutPt op1b, OutPt op2, OutPt op2b, Vector2 pt, bool discardLeft)
        {
            Direction dir1 = op1.Pt.X > op1b.Pt.X ? Direction.RightToLeft : Direction.LeftToRight;
            Direction dir2 = op2.Pt.X > op2b.Pt.X ? Direction.RightToLeft : Direction.LeftToRight;
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
                while (op1.Next.Pt.X <= pt.X &&
                  op1.Next.Pt.X >= op1.Pt.X && op1.Next.Pt.Y == pt.Y)
                {
                    op1 = op1.Next;
                }

                if (discardLeft && (op1.Pt.X != pt.X))
                {
                    op1 = op1.Next;
                }

                op1b = this.DupOutPt(op1, !discardLeft);
                if (op1b.Pt != pt)
                {
                    op1 = op1b;
                    op1.Pt = pt;
                    op1b = this.DupOutPt(op1, !discardLeft);
                }
            }
            else
            {
                while (op1.Next.Pt.X >= pt.X &&
                        op1.Next.Pt.X <= op1.Pt.X &&
                        op1.Next.Pt.Y == pt.Y)
                {
                    op1 = op1.Next;
                }

                if (!discardLeft && (op1.Pt.X != pt.X))
                {
                    op1 = op1.Next;
                }

                op1b = this.DupOutPt(op1, discardLeft);
                if (op1b.Pt != pt)
                {
                    op1 = op1b;
                    op1.Pt = pt;
                    op1b = this.DupOutPt(op1, discardLeft);
                }
            }

            if (dir2 == Direction.LeftToRight)
            {
                while (op2.Next.Pt.X <= pt.X &&
                  op2.Next.Pt.X >= op2.Pt.X &&
                  op2.Next.Pt.Y == pt.Y)
                {
                    op2 = op2.Next;
                }

                if (discardLeft && (op2.Pt.X != pt.X))
                {
                    op2 = op2.Next;
                }

                op2b = this.DupOutPt(op2, !discardLeft);
                if (op2b.Pt != pt)
                {
                    op2 = op2b;
                    op2.Pt = pt;
                    op2b = this.DupOutPt(op2, !discardLeft);
                }
            }
            else
            {
                while (op2.Next.Pt.X >= pt.X &&
                  op2.Next.Pt.X <= op2.Pt.X &&
                  op2.Next.Pt.Y == pt.Y)
                {
                    op2 = op2.Next;
                }

                if (!discardLeft && (op2.Pt.X != pt.X))
                {
                    op2 = op2.Next;
                }

                op2b = this.DupOutPt(op2, discardLeft);
                if (op2b.Pt != pt)
                {
                    op2 = op2b;
                    op2.Pt = pt;
                    op2b = this.DupOutPt(op2, discardLeft);
                }
            }

            if ((dir1 == Direction.LeftToRight) == discardLeft)
            {
                op1.Prev = op2;
                op2.Next = op1;
                op1b.Next = op2b;
                op2b.Prev = op1b;
            }
            else
            {
                op1.Next = op2;
                op2.Prev = op1;
                op1b.Prev = op2b;
                op2b.Next = op1b;
            }

            return true;
        }

        private bool JoinPoints(Join j, OutRec outRec1, OutRec outRec2)
        {
            OutPt op1 = j.OutPt1, op1b;
            OutPt op2 = j.OutPt2, op2b;

            // There are 3 kinds of joins for output polygons ...
            // 1. Horizontal joins where Join.OutPt1 & Join.OutPt2 are vertices anywhere
            // along (horizontal) collinear edges (& Join.OffPt is on the same horizontal).
            // 2. Non-horizontal joins where Join.OutPt1 & Join.OutPt2 are at the same
            // location at the Bottom of the overlapping segment (& Join.OffPt is above).
            // 3. StrictlySimple joins where edges touch but are not collinear and where
            // Join.OutPt1, Join.OutPt2 & Join.OffPt all share the same point.
            bool isHorizontal = j.OutPt1.Pt.Y == j.OffPt.Y;

            if (isHorizontal && (j.OffPt == j.OutPt1.Pt) && (j.OffPt == j.OutPt2.Pt))
            {
                // Strictly Simple join ...
                if (outRec1 != outRec2)
                {
                    return false;
                }

                op1b = j.OutPt1.Next;
                while (op1b != op1 && (op1b.Pt == j.OffPt))
                {
                    op1b = op1b.Next;
                }

                bool reverse1 = op1b.Pt.Y > j.OffPt.Y;
                op2b = j.OutPt2.Next;
                while (op2b != op2 && (op2b.Pt == j.OffPt))
                {
                    op2b = op2b.Next;
                }

                bool reverse2 = op2b.Pt.Y > j.OffPt.Y;
                if (reverse1 == reverse2)
                {
                    return false;
                }

                if (reverse1)
                {
                    op1b = this.DupOutPt(op1, false);
                    op2b = this.DupOutPt(op2, true);
                    op1.Prev = op2;
                    op2.Next = op1;
                    op1b.Next = op2b;
                    op2b.Prev = op1b;
                    j.OutPt1 = op1;
                    j.OutPt2 = op1b;
                    return true;
                }
                else
                {
                    op1b = this.DupOutPt(op1, true);
                    op2b = this.DupOutPt(op2, false);
                    op1.Next = op2;
                    op2.Prev = op1;
                    op1b.Prev = op2b;
                    op2b.Next = op1b;
                    j.OutPt1 = op1;
                    j.OutPt2 = op1b;
                    return true;
                }
            }
            else if (isHorizontal)
            {
                // treat horizontal joins differently to non-horizontal joins since with
                // them we're not yet sure where the overlapping is. OutPt1.Pt & OutPt2.Pt
                // may be anywhere along the horizontal edge.
                op1b = op1;
                while (op1.Prev.Pt.Y == op1.Pt.Y && op1.Prev != op1b && op1.Prev != op2)
                {
                    op1 = op1.Prev;
                }

                while (op1b.Next.Pt.Y == op1b.Pt.Y && op1b.Next != op1 && op1b.Next != op2)
                {
                    op1b = op1b.Next;
                }

                if (op1b.Next == op1 || op1b.Next == op2)
                {
                    return false; // a flat 'polygon'
                }

                op2b = op2;
                while (op2.Prev.Pt.Y == op2.Pt.Y && op2.Prev != op2b && op2.Prev != op1b)
                {
                    op2 = op2.Prev;
                }

                while (op2b.Next.Pt.Y == op2b.Pt.Y && op2b.Next != op2 && op2b.Next != op1)
                {
                    op2b = op2b.Next;
                }

                if (op2b.Next == op2 || op2b.Next == op1)
                {
                    return false; // a flat 'polygon'
                }

                float left, right;

                // Op1 -. Op1b & Op2 -. Op2b are the extremites of the horizontal edges
                if (!this.GetOverlap(op1.Pt.X, op1b.Pt.X, op2.Pt.X, op2b.Pt.X, out left, out right))
                {
                    return false;
                }

                // DiscardLeftSide: when overlapping edges are joined, a spike will created
                // which needs to be cleaned up. However, we don't want Op1 or Op2 caught up
                // on the discard Side as either may still be needed for other joins ...
                Vector2 pt;
                bool discardLeftSide;
                if (op1.Pt.X >= left && op1.Pt.X <= right)
                {
                    pt = op1.Pt;
                    discardLeftSide = op1.Pt.X > op1b.Pt.X;
                }
                else if (op2.Pt.X >= left && op2.Pt.X <= right)
                {
                    pt = op2.Pt;
                    discardLeftSide = op2.Pt.X > op2b.Pt.X;
                }
                else if (op1b.Pt.X >= left && op1b.Pt.X <= right)
                {
                    pt = op1b.Pt;
                    discardLeftSide = op1b.Pt.X > op1.Pt.X;
                }
                else
                {
                    pt = op2b.Pt;
                    discardLeftSide = op2b.Pt.X > op2.Pt.X;
                }

                j.OutPt1 = op1;
                j.OutPt2 = op2;
                return this.JoinHorz(op1, op1b, op2, op2b, pt, discardLeftSide);
            }
            else
            {
                // nb: For non-horizontal joins ...
                //    1. Jr.OutPt1.Pt.Y == Jr.OutPt2.Pt.Y
                //    2. Jr.OutPt1.Pt > Jr.OffPt.Y

                // make sure the polygons are correctly oriented ...
                op1b = op1.Next;
                while ((op1b.Pt == op1.Pt) && (op1b != op1))
                {
                    op1b = op1b.Next;
                }

                bool reverse1 = (op1b.Pt.Y > op1.Pt.Y) || !SlopesEqual(op1.Pt, op1b.Pt, j.OffPt);
                if (reverse1)
                {
                    op1b = op1.Prev;
                    while ((op1b.Pt == op1.Pt) && (op1b != op1))
                    {
                        op1b = op1b.Prev;
                    }

                    if ((op1b.Pt.Y > op1.Pt.Y) ||
                      !SlopesEqual(op1.Pt, op1b.Pt, j.OffPt))
                    {
                        return false;
                    }
                }

                op2b = op2.Next;
                while ((op2b.Pt == op2.Pt) && (op2b != op2))
                {
                    op2b = op2b.Next;
                }

                bool reverse2 = (op2b.Pt.Y > op2.Pt.Y) || !SlopesEqual(op2.Pt, op2b.Pt, j.OffPt);
                if (reverse2)
                {
                    op2b = op2.Prev;
                    while ((op2b.Pt == op2.Pt) && (op2b != op2))
                    {
                        op2b = op2b.Prev;
                    }

                    if ((op2b.Pt.Y > op2.Pt.Y) ||
                      !SlopesEqual(op2.Pt, op2b.Pt, j.OffPt))
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
                    op1.Prev = op2;
                    op2.Next = op1;
                    op1b.Next = op2b;
                    op2b.Prev = op1b;
                    j.OutPt1 = op1;
                    j.OutPt2 = op1b;
                    return true;
                }
                else
                {
                    op1b = this.DupOutPt(op1, true);
                    op2b = this.DupOutPt(op2, false);
                    op1.Next = op2;
                    op2.Prev = op1;
                    op1b.Prev = op2b;
                    op2b.Next = op1b;
                    j.OutPt1 = op1;
                    j.OutPt2 = op1b;
                    return true;
                }
            }
        }

        private void FixupFirstLefts1(OutRec oldOutRec, OutRec newOutRec)
        {
            foreach (OutRec outRec in this.polyOuts)
            {
                OutRec firstLeft = ParseFirstLeft(outRec.FirstLeft);
                if (outRec.Pts != null && firstLeft == oldOutRec)
                {
                    if (Poly2ContainsPoly1(outRec.Pts, newOutRec.Pts))
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
                if (outRec.Pts == null || outRec == outerOutRec || outRec == innerOutRec)
                {
                    continue;
                }

                OutRec firstLeft = ParseFirstLeft(outRec.FirstLeft);
                if (firstLeft != orfl && firstLeft != innerOutRec && firstLeft != outerOutRec)
                {
                    continue;
                }

                if (Poly2ContainsPoly1(outRec.Pts, innerOutRec.Pts))
                {
                    outRec.FirstLeft = innerOutRec;
                }
                else if (Poly2ContainsPoly1(outRec.Pts, outerOutRec.Pts))
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
                if (outRec.Pts != null && outRec.FirstLeft == oldOutRec)
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

                OutRec outRec1 = this.GetOutRec(join.OutPt1.Idx);
                OutRec outRec2 = this.GetOutRec(join.OutPt2.Idx);

                if (outRec1.Pts == null || outRec2.Pts == null)
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
                    outRec1.Pts = join.OutPt1;
                    outRec1.BottomPt = null;
                    outRec2 = this.CreateOutRec();
                    outRec2.Pts = join.OutPt2;

                    // update all OutRec2.Pts Idx's ...
                    this.UpdateOutPtIdxs(outRec2);

                    if (Poly2ContainsPoly1(outRec2.Pts, outRec1.Pts))
                    {
                        // outRec1 contains outRec2 ...
                        outRec2.IsHole = !outRec1.IsHole;
                        outRec2.FirstLeft = outRec1;

                        this.FixupFirstLefts2(outRec2, outRec1);

                    }
                    else if (Poly2ContainsPoly1(outRec1.Pts, outRec2.Pts))
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
                    outRec2.Pts = null;
                    outRec2.BottomPt = null;
                    outRec2.Idx = outRec1.Idx;

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
            OutPt op = outrec.Pts;
            do
            {
                op.Idx = outrec.Idx;
                op = op.Prev;
            }
            while (op != outrec.Pts);
        }

        private void DoSimplePolygons()
        {
            int i = 0;
            while (i < this.polyOuts.Count)
            {
                OutRec outrec = this.polyOuts[i++];
                OutPt op = outrec.Pts;
                if (op == null || outrec.IsOpen)
                {
                    continue;
                }

                do
                {
                    // for each Pt in Polygon until duplicate found do ...
                    OutPt op2 = op.Next;
                    while (op2 != outrec.Pts)
                    {
                        if ((op.Pt == op2.Pt) && op2.Next != op && op2.Prev != op)
                        {
                            // split the polygon into two ...
                            OutPt op3 = op.Prev;
                            OutPt op4 = op2.Prev;
                            op.Prev = op4;
                            op4.Next = op;
                            op2.Prev = op3;
                            op3.Next = op2;

                            outrec.Pts = op;
                            OutRec outrec2 = this.CreateOutRec();
                            outrec2.Pts = op2;
                            this.UpdateOutPtIdxs(outrec2);
                            if (Poly2ContainsPoly1(outrec2.Pts, outrec.Pts))
                            {
                                // OutRec2 is contained by OutRec1 ...
                                outrec2.IsHole = !outrec.IsHole;
                                outrec2.FirstLeft = outrec;
                                this.FixupFirstLefts2(outrec2, outrec);
                            }
                            else if (Poly2ContainsPoly1(outrec.Pts, outrec2.Pts))
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
                while (op != outrec.Pts);
            }
        }

        private double Area(OutRec outRec)
        {
            return this.Area(outRec.Pts);
        }

        private double Area(OutPt op)
        {
            OutPt opFirst = op;
            if (op == null)
            {
                return 0;
            }

            double a = 0;
            do
            {
                a = a + ((op.Prev.Pt.X + op.Pt.X) * (op.Prev.Pt.Y - op.Pt.Y));
                op = op.Next;
            }
            while (op != opFirst);

            return a * 0.5;
        }

        private void SetDx(Edge e)
        {
            e.Delta = new Vector2(e.Top.X - e.Bot.X, e.Top.Y - e.Bot.Y);
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
            current = this.currentLM;
            if (this.currentLM != null && this.currentLM.Y == y)
            {
                this.currentLM = this.currentLM.Next;
                return true;
            }

            return false;
        }

        private void Reset()
        {
            this.currentLM = this.minimaList;
            if (this.currentLM == null)
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
                    e.Curr = e.Bot;
                    e.OutIndex = Unassigned;
                }

                e = lm.RightBound;
                if (e != null)
                {
                    e.Curr = e.Bot;
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
            return this.currentLM != null;
        }

        private OutRec CreateOutRec()
        {
            OutRec result = new OutRec();
            result.Idx = Unassigned;
            result.IsHole = false;
            result.IsOpen = false;
            result.FirstLeft = null;
            result.Pts = null;
            result.BottomPt = null;
            result.PolyNode = null;
            this.polyOuts.Add(result);
            result.Idx = this.polyOuts.Count - 1;
            return result;
        }

        private void DisposeOutRec(int index)
        {
            OutRec outRec = this.polyOuts[index];
            outRec.Pts = null;
            outRec = null;
            this.polyOuts[index] = null;
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
            e.NextInLML.WindindDelta = e.WindindDelta;
            e.NextInLML.WindingCount = e.WindingCount;
            e.NextInLML.WindingCountInOppositePolyType = e.WindingCountInOppositePolyType;
            e = e.NextInLML;
            e.Curr = e.Bot;
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
            if (e.Curr.Y >= e.NextEdge.Curr.Y)
            {
                e.Bot = e.Curr;
                e.Top = e.NextEdge.Curr;
            }
            else
            {
                e.Top = e.Curr;
                e.Bot = e.NextEdge.Curr;
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
                    while (edge.Top.Y == edge.NextEdge.Bot.Y)
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
                    while (edge.Top.Y == edge.PreviousEdge.Bot.Y)
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
                    locMin.Y = edge.Bot.Y;
                    locMin.LeftBound = null;
                    locMin.RightBound = edge;
                    edge.WindindDelta = 0;
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
                    if (eStart.Bot.X != edge.Bot.X && eStart.Top.X != edge.Bot.X)
                    {
                        ReverseHorizontal(edge);
                    }
                }
                else if (eStart.Bot.X != edge.Bot.X)
                {
                    ReverseHorizontal(edge);
                }
            }

            eStart = edge;
            if (leftBoundIsForward)
            {
                while (result.Top.Y == result.NextEdge.Bot.Y && result.NextEdge.OutIndex != Skip)
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
                    if (edge.Dx == HorizontalDeltaLimit && edge != eStart && edge.Bot.X != edge.PreviousEdge.Top.X)
                    {
                        ReverseHorizontal(edge);
                    }

                    edge = edge.NextEdge;
                }

                if (edge.Dx == HorizontalDeltaLimit && edge != eStart && edge.Bot.X != edge.PreviousEdge.Top.X)
                {
                    ReverseHorizontal(edge);
                }

                result = result.NextEdge; // move to the edge just beyond current bound
            }
            else
            {
                while (result.Top.Y == result.PreviousEdge.Bot.Y && result.PreviousEdge.OutIndex != Skip)
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
                    if (edge.Dx == HorizontalDeltaLimit && edge != eStart && edge.Bot.X != edge.NextEdge.Top.X)
                    {
                        ReverseHorizontal(edge);
                    }

                    edge = edge.PreviousEdge;
                }

                if (edge.Dx == HorizontalDeltaLimit && edge != eStart && edge.Bot.X != edge.NextEdge.Top.X)
                {
                    ReverseHorizontal(edge);
                }

                result = result.PreviousEdge; // move to the edge just beyond current bound
            }

            return result;
        }
    }
}