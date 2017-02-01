// <copyright file="Clipper.cs" company="Scott Williams">
// Copyright (c) Scott Williams and contributors.
// Licensed under the Apache License, Version 2.0.
// </copyright>

namespace SixLabors.Shapes.PolygonClipper
{
    using System;
    using System.Collections.Generic;
    using System.Collections.Immutable;
    using System.Linq;
    using System.Numerics;
    using System.Runtime.CompilerServices;

    /// <summary>
    /// Library to clip polygons.
    /// </summary>
    public class Clipper
    {
        private static readonly IComparer<IntersectNode> IntersectNodeComparer = new IntersectNodeSort();
        private readonly List<IntersectNode> intersectList = new List<IntersectNode>();

        private readonly List<Join> joins = new List<Join>();

        private readonly List<Join> ghostJoins = new List<Join>();
        private readonly List<List<Edge>> edges = new List<List<Edge>>();
        private readonly List<OutRec> polyOuts = new List<OutRec>();
        private readonly object syncRoot = new object();

        private Maxima maxima = null;
        private Edge sortedEdges = null;
        private LocalMinima minimaList;
        private LocalMinima currentLocalMinima;
        private Scanbeam scanbeam = null;
        private Edge activeEdges = null;
        private bool resultsDirty = true;
        private ImmutableArray<IShape> results;

        /// <summary>
        /// Initializes a new instance of the <see cref="Clipper"/> class.
        /// </summary>
        /// <param name="shapes">The shapes.</param>
        public Clipper(IEnumerable<ClipableShape> shapes)
        {
            Guard.NotNull(shapes, nameof(shapes));
            this.AddShapes(shapes);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Clipper" /> class.
        /// </summary>
        /// <param name="shapes">The shapes.</param>
        public Clipper(params ClipableShape[] shapes)
        {
            this.AddShapes(shapes);
        }

        /// <summary>
        /// Executes the specified clip type.
        /// </summary>
        /// <returns>
        /// Returns the <see cref="IShape" /> array containing the converted polygons.
        /// </returns>
        public ImmutableArray<IShape> GenerateClippedShapes()
        {
            if (!this.resultsDirty)
            {
                return this.results;
            }

            lock (this.syncRoot)
            {
                if (!this.resultsDirty)
                {
                    return this.results;
                }

                try
                {
                    bool succeeded = this.ExecuteInternal();

                    // build the return polygons ...
                    if (succeeded)
                    {
                        this.results = this.BuildResult();
                        this.resultsDirty = false;
                    }
                }
                finally
                {
                    this.DisposeAllPolyPoints();
                }

                return this.results;
            }
        }

        /// <summary>
        /// Adds the paths.
        /// </summary>
        /// <param name="clipableShaps">The clipable shaps.</param>
        public void AddShapes(IEnumerable<ClipableShape> clipableShaps)
        {
            foreach (var p in clipableShaps)
            {
                this.AddShape(p.Shape, p.Type);
            }
        }

        /// <summary>
        /// Adds the shapes.
        /// </summary>
        /// <param name="shapes">The shapes.</param>
        /// <param name="clippingType">The clipping type.</param>
        public void AddShapes(IEnumerable<IShape> shapes, ClippingType clippingType)
        {
            foreach (var p in shapes)
            {
                this.AddShape(p, clippingType);
            }
        }

        /// <summary>
        /// Adds the path.
        /// </summary>
        /// <param name="shape">The shape.</param>
        /// <param name="clippingType">The clipping type.</param>
        internal void AddShape(IShape shape, ClippingType clippingType)
        {
            foreach (var p in shape.Paths)
            {
                this.AddPath(p, clippingType);
            }
        }

        /// <summary>
        /// Adds the path.
        /// </summary>
        /// <param name="path">The path.</param>
        /// <param name="clippingType">Type of the poly.</param>
        /// <returns>True if the path was added.</returns>
        /// <exception cref="ClipperException">AddPath: Open paths have been disabled.</exception>
        internal bool AddPath(IPath path, ClippingType clippingType)
        {
            // every path we add lock the clipper to prevent state curruption
            lock (this.syncRoot)
            {
                this.resultsDirty = true;

                var points = path.Flatten();

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

                edges[0].Init(edges[1], edges[hi], points[0]);
                edges[hi].Init(edges[0], edges[hi - 1], points[hi]);
                for (int i = hi - 1; i >= 1; --i)
                {
                    edges[i].Init(edges[i + 1], edges[i - 1], points[i]);
                }

                Edge startEdge = edges[0];

                // 2. Remove duplicate vertices, and (when closed) collinear edges ...
                Edge edge = startEdge;
                Edge loopStop = startEdge;
                while (true)
                {
                    if (edge.Current == edge.NextEdge.Current)
                    {
                        // remove unneeded edges
                        if (edge == edge.NextEdge)
                        {
                            break;
                        }

                        if (edge == startEdge)
                        {
                            startEdge = edge.NextEdge;
                        }

                        edge = edge.RemoveSelfReturnNext();
                        loopStop = edge;
                        continue;
                    }

                    if (Helpers.SlopesEqual(edge.PreviousEdge.Current, edge.Current, edge.NextEdge.Current))
                    {
                        // Collinear edges are allowed for open paths but in closed paths
                        // the default is to merge adjacent collinear edges into a single edge.
                        // However, if the PreserveCollinear property is enabled, only overlapping
                        // collinear edges (ie spikes) will be removed from closed paths.
                        if (edge == startEdge)
                        {
                            startEdge = edge.NextEdge;
                        }

                        edge = edge.RemoveSelfReturnNext();
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
                    edge.InitClippingType(clippingType);
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
                    edge = edge.FindNextLocMin();
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
                    if (edge.OutIndex == Constants.Skip)
                    {
                        edge = this.ProcessBound(edge, leftBoundIsForward);
                    }

                    Edge edge2 = this.ProcessBound(locMin.RightBound, !leftBoundIsForward);
                    if (edge2.OutIndex == Constants.Skip)
                    {
                        edge2 = this.ProcessBound(edge2, !leftBoundIsForward);
                    }

                    if (locMin.LeftBound.OutIndex == Constants.Skip)
                    {
                        locMin.LeftBound = null;
                    }
                    else if (locMin.RightBound.OutIndex == Constants.Skip)
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
        }

        private void DisposeAllPolyPoints()
        {
            foreach (var polyout in this.polyOuts)
            {
                polyout.Points = null;
            }

            this.polyOuts.Clear();
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

                this.JoinCommonEdges();

                foreach (OutRec outRec in this.polyOuts)
                {
                    outRec.FixupOuts();
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
                    if (rb.IsHorizontal)
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
                if (op1 != null && rb.IsHorizontal &&
                 this.ghostJoins.Count > 0 && rb.WindingDelta != 0)
                {
                    for (int i = 0; i < this.ghostJoins.Count; i++)
                    {
                        // if the horizontal Rb and a 'ghost' horizontal overlap, then convert
                        // the 'ghost' join to a real join ready for later ...
                        Join j = this.ghostJoins[i];
                        if (j.HorizontalSegmentsOverlap(rb))
                        {
                            this.AddJoin(j.OutPoint1, op1, j.OffPoint);
                        }
                    }
                }

                if (lb.OutIndex >= 0 && lb.PreviousInAEL != null &&
                  lb.PreviousInAEL.Current.X == lb.Bottom.X &&
                  lb.PreviousInAEL.OutIndex >= 0 &&
                  Helpers.SlopesEqual(lb.PreviousInAEL.Current, lb.PreviousInAEL.Top, lb.Current, lb.Top) &&
                  lb.WindingDelta != 0 && lb.PreviousInAEL.WindingDelta != 0)
                {
                    OutPoint op2 = this.AddOutPt(lb.PreviousInAEL, lb.Bottom);
                    this.AddJoin(op1, op2, lb.Top);
                }

                if (lb.NextInAEL != rb)
                {
                    if (rb.OutIndex >= 0 && rb.PreviousInAEL.OutIndex >= 0 &&
                      Helpers.SlopesEqual(rb.PreviousInAEL.Current, rb.PreviousInAEL.Top, rb.Current, rb.Top) &&
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
                    return e2.Top.X < e1.TopX(e2.Top.Y);
                }
                else
                {
                    return e1.Top.X > e2.TopX(e1.Top.Y);
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

            if (edge.PolyType == ClippingType.Subject)
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
                e1.OutIndex = Constants.Unassigned;
                e2.OutIndex = Constants.Unassigned;
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
            if (e2.IsHorizontal || (e1.Dx > e2.Dx))
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
                float xPrev = prevE.TopX(pt.Y);
                float xE = e.TopX(pt.Y);
                if ((xPrev == xE) &&
                    (e.WindingDelta != 0) &&
                    (prevE.WindingDelta != 0) &&
                    Helpers.SlopesEqual(new Vector2(xPrev, pt.Y), prevE.Top, new Vector2(xE, pt.Y), e.Top))
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
                holeStateRec = outRec1.GetLowermostRec(outRec2);
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

            e1.OutIndex = Constants.Unassigned; // nb: safe because we only get here via AddLocalMaxPoly
            e2.OutIndex = Constants.Unassigned;

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
                    e1.SwitchSides(e2);
                    e1.SwitchIndexes(e2);
                }
            }
            else if (e1Contributing)
            {
                if (e2Wc == 0 || e2Wc == 1)
                {
                    this.AddOutPt(e1, pt);
                    e1.SwitchSides(e2);
                    e1.SwitchIndexes(e2);
                }
            }
            else if (e2Contributing)
            {
                if (e1Wc == 0 || e1Wc == 1)
                {
                    this.AddOutPt(e2, pt);
                    e1.SwitchSides(e2);
                    e1.SwitchIndexes(e2);
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
                    if (((e1.PolyType == ClippingType.Clip) && (e1Wc2 > 0) && (e2Wc2 > 0)) ||
                        ((e1.PolyType == ClippingType.Subject) && (e1Wc2 <= 0) && (e2Wc2 <= 0)))
                    {
                        this.AddLocalMinPoly(e1, e2, pt);
                    }
                }
                else
                {
                    e1.SwitchSides(e2);
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

        private void ProcessHorizontal(Edge horzEdge)
        {
            Direction dir;
            float horzLeft, horzRight;
            bool isOpen = horzEdge.WindingDelta == 0;

            horzEdge.GetHorzDirection(out dir, out horzLeft, out horzRight);

            Edge lastHorzEdge = horzEdge.LastHorizonalEdge();
            Edge maxPairEdge = null;

            if (lastHorzEdge.NextInLML == null)
            {
                maxPairEdge = lastHorzEdge?.GetMaximaPair();
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

                    if (currMax != null && currMax.X >= lastHorzEdge.Top.X)
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

                    if (currMax.X <= lastHorzEdge.Top.X)
                    {
                        currMax = null;
                    }
                }
            }

            OutPoint op1 = null;

            // loop through consec. horizontal edges
            while (true)
            {
                bool isLastHorz = horzEdge == lastHorzEdge;
                Edge e = horzEdge.GetNextInAEL(dir);
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
                              horzEdge.HorizontalSegmentsOverlap(eNextHorz))
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
                    if (e == maxPairEdge && isLastHorz)
                    {
                        if (horzEdge.OutIndex >= 0)
                        {
                            this.AddLocalMaxPoly(horzEdge, maxPairEdge, horzEdge.Top);
                        }

                        this.DeleteFromAEL(horzEdge);
                        this.DeleteFromAEL(maxPairEdge);
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

                    Edge eNext = e.GetNextInAEL(dir);
                    this.SwapPositionsInAEL(horzEdge, e);
                    e = eNext;
                } // end while(e != null)

                // Break out of loop if HorzEdge.NextInLML is not also horizontal ...
                if (horzEdge.NextInLML == null || !horzEdge.NextInLML.IsHorizontal)
                {
                    break;
                }

                this.UpdateEdgeIntoAEL(ref horzEdge);
                if (horzEdge.OutIndex >= 0)
                {
                    this.AddOutPt(horzEdge, horzEdge.Bottom);
                }

                horzEdge.GetHorzDirection(out dir, out horzLeft, out horzRight);
            }

            if (horzEdge.OutIndex >= 0 && op1 == null)
            {
                op1 = this.GetLastOutPt(horzEdge);
                Edge eNextHorz = this.sortedEdges;
                while (eNextHorz != null)
                {
                    if (eNextHorz.OutIndex >= 0 &&
                        horzEdge.HorizontalSegmentsOverlap(eNextHorz))
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
                    Edge prevEdge = horzEdge.PreviousInAEL;
                    Edge nextEdge = horzEdge.NextInAEL;
                    if (prevEdge != null && prevEdge.Current.X == horzEdge.Bottom.X &&
                      prevEdge.Current.Y == horzEdge.Bottom.Y && prevEdge.WindingDelta != 0 &&
                      (prevEdge.OutIndex >= 0 && prevEdge.Current.Y > prevEdge.Top.Y &&
                      horzEdge.SlopesEqual(prevEdge)))
                    {
                        OutPoint op2 = this.AddOutPt(prevEdge, horzEdge.Bottom);
                        this.AddJoin(op1, op2, horzEdge.Top);
                    }
                    else if (nextEdge != null && nextEdge.Current.X == horzEdge.Bottom.X &&
                      nextEdge.Current.Y == horzEdge.Bottom.Y && nextEdge.WindingDelta != 0 &&
                      nextEdge.OutIndex >= 0 && nextEdge.Current.Y > nextEdge.Top.Y &&
                      horzEdge.SlopesEqual(nextEdge))
                    {
                        OutPoint op2 = this.AddOutPt(nextEdge, horzEdge.Bottom);
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
                e.Current = new Vector2(e.TopX(topY), e.Current.Y);
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
                    Edge nextEdge = e.NextInSEL;
                    if (e.Current.X > nextEdge.Current.X)
                    {
                        Vector2 pt = e.IntersectPoint(nextEdge);
                        if (pt.Y < topY)
                        {
                            pt = new Vector2(e.TopX(topY), topY);
                        }

                        IntersectNode newNode = new IntersectNode();
                        newNode.Edge1 = e;
                        newNode.Edge2 = nextEdge;
                        newNode.Point = pt;
                        this.intersectList.Add(newNode);

                        this.SwapPositionsInSEL(e, nextEdge);
                        isModified = true;
                    }
                    else
                    {
                        e = nextEdge;
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
                if (!this.intersectList[i].EdgesAdjacent())
                {
                    int j = i + 1;
                    while (j < cnt && !this.intersectList[j].EdgesAdjacent())
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

        private void ProcessEdgesAtTopOfScanbeam(float topY)
        {
            // 1. process maxima, treating them as if they're 'bent' horizontal edges,
            //   but exclude maxima with horizontal edges. nb: e can't be a horizontal.
            this.ProcessMaxima(topY);

            // 3. Process horizontals at the Top of the scanbeam ...
            this.ProcessHorizontals();
            this.maxima = null;

            // 4. Promote intermediate vertices ...
            this.PromoteIntermediateVertices(topY);
        }

        private void PromoteIntermediateVertices(float topY)
        {
            var e = this.activeEdges;
            while (e != null)
            {
                if (e.IsIntermediate(topY))
                {
                    OutPoint op = null;
                    if (e.OutIndex >= 0)
                    {
                        op = this.AddOutPt(e, e.Top);
                    }

                    this.UpdateEdgeIntoAEL(ref e);

                    // if output polygons share an edge, they'll need joining later ...
                    Edge prevEdge = e.PreviousInAEL;
                    Edge nextEdge = e.NextInAEL;
                    if (prevEdge != null && prevEdge.Current.X == e.Bottom.X &&
                      prevEdge.Current.Y == e.Bottom.Y && op != null &&
                      prevEdge.OutIndex >= 0 && prevEdge.Current.Y > prevEdge.Top.Y &&
                      Helpers.SlopesEqual(e.Current, e.Top, prevEdge.Current, prevEdge.Top) &&
                      (e.WindingDelta != 0) && (prevEdge.WindingDelta != 0))
                    {
                        OutPoint op2 = this.AddOutPt(prevEdge, e.Bottom);
                        this.AddJoin(op, op2, e.Top);
                    }
                    else if (nextEdge != null && nextEdge.Current.X == e.Bottom.X &&
                      nextEdge.Current.Y == e.Bottom.Y && op != null &&
                      nextEdge.OutIndex >= 0 && nextEdge.Current.Y > nextEdge.Top.Y &&
                      Helpers.SlopesEqual(e.Current, e.Top, nextEdge.Current, nextEdge.Top) &&
                      (e.WindingDelta != 0) && (nextEdge.WindingDelta != 0))
                    {
                        OutPoint op2 = this.AddOutPt(nextEdge, e.Bottom);
                        this.AddJoin(op, op2, e.Top);
                    }
                }

                e = e.NextInAEL;
            }
        }

        private void ProcessMaxima(float topY)
        {
            Edge e = this.activeEdges;
            while (e != null)
            {
                bool isMaximaEdge = e.IsMaxima(topY);

                if (isMaximaEdge)
                {
                    Edge eMaxPair = e.GetMaximaPairEx();
                    isMaximaEdge = eMaxPair == null || !eMaxPair.IsHorizontal;
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
                    if (e.IsIntermediate(topY) && e.NextInLML.IsHorizontal)
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
                        e.Current = new Vector2(e.TopX(topY), topY);
                    }

                    e = e.NextInAEL;
                }
            }
        }

        private void DoMaxima(Edge e)
        {
            Edge maxPair = e?.GetMaximaPairEx();
            if (maxPair == null)
            {
                if (e.OutIndex >= 0)
                {
                    this.AddOutPt(e, e.Top);
                }

                this.DeleteFromAEL(e);
                return;
            }

            Edge nextEdge = e.NextInAEL;
            while (nextEdge != null && nextEdge != maxPair)
            {
                this.IntersectEdges(e, nextEdge, e.Top);
                this.SwapPositionsInAEL(e, nextEdge);
                nextEdge = e.NextInAEL;
            }

            if (e.OutIndex == Constants.Unassigned && maxPair.OutIndex == Constants.Unassigned)
            {
                this.DeleteFromAEL(e);
                this.DeleteFromAEL(maxPair);
            }
            else if (e.OutIndex >= 0 && maxPair.OutIndex >= 0)
            {
                if (e.OutIndex >= 0)
                {
                    this.AddLocalMaxPoly(e, maxPair, e.Top);
                }

                this.DeleteFromAEL(e);
                this.DeleteFromAEL(maxPair);
            }
            else
            {
                throw new ClipperException("DoMaxima error");
            }
        }

        private ImmutableArray<IShape> BuildResult()
        {
            List<IShape> shapes = new List<IShape>(this.polyOuts.Count);

            // add each output polygon/contour to polytree ...
            for (int i = 0; i < this.polyOuts.Count; i++)
            {
                OutRec outRec = this.polyOuts[i];
                if (outRec.Points == null)
                {
                    continue;
                }

                int cnt = outRec.Points.Count();
                if ((outRec.IsOpen && cnt < 2) ||
                  (!outRec.IsOpen && cnt < 3))
                {
                    continue;
                }

                outRec.FixHoleLinkage();
                var shape = outRec.SourcePath as IShape;
                if (shape != null)
                {
                    shapes.Add(shape);
                }
                else
                {
                    var wrapped = outRec.SourcePath as IWrapperPath;
                    if (wrapped != null)
                    {
                        shapes.Add(wrapped.AsShape());
                    }
                    else
                    {
                        var points = new Vector2[cnt];
                        OutPoint op = outRec.Points.Previous;
                        for (int j = 0; j < cnt; j++)
                        {
                            points[j] = op.Point;
                            op = op.Previous;
                        }

                        shapes.Add(new Polygon(new LinearLineSegment(points)));
                    }
                }
            }

            return shapes.ToImmutableArray();
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

                op1b = op1.Duplicate(!discardLeft);
                if (op1b.Point != pt)
                {
                    op1 = op1b;
                    op1.Point = pt;
                    op1b = op1.Duplicate(!discardLeft);
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

                op1b = op1.Duplicate(discardLeft);
                if (op1b.Point != pt)
                {
                    op1 = op1b;
                    op1.Point = pt;
                    op1b = op1.Duplicate(discardLeft);
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

                op2b = op2.Duplicate(!discardLeft);
                if (op2b.Point != pt)
                {
                    op2 = op2b;
                    op2.Point = pt;
                    op2b = op2.Duplicate(!discardLeft);
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

                op2b = op2.Duplicate(discardLeft);
                if (op2b.Point != pt)
                {
                    op2 = op2b;
                    op2.Point = pt;
                    op2b = op2.Duplicate(discardLeft);
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
                    op1b = op1.Duplicate(false);
                    op2b = op2.Duplicate(true);
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
                    op1b = op1.Duplicate(true);
                    op2b = op2.Duplicate(false);
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
                if (!Helpers.GetOverlap(op1.Point.X, op1b.Point.X, op2.Point.X, op2b.Point.X, out left, out right))
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

                bool reverse1 = (op1b.Point.Y > op1.Point.Y) || !Helpers.SlopesEqual(op1.Point, op1b.Point, j.OffPoint);
                if (reverse1)
                {
                    op1b = op1.Previous;
                    while ((op1b.Point == op1.Point) && (op1b != op1))
                    {
                        op1b = op1b.Previous;
                    }

                    if ((op1b.Point.Y > op1.Point.Y) ||
                      !Helpers.SlopesEqual(op1.Point, op1b.Point, j.OffPoint))
                    {
                        return false;
                    }
                }

                op2b = op2.Next;
                while ((op2b.Point == op2.Point) && (op2b != op2))
                {
                    op2b = op2b.Next;
                }

                bool reverse2 = (op2b.Point.Y > op2.Point.Y) || !Helpers.SlopesEqual(op2.Point, op2b.Point, j.OffPoint);
                if (reverse2)
                {
                    op2b = op2.Previous;
                    while ((op2b.Point == op2.Point) && (op2b != op2))
                    {
                        op2b = op2b.Previous;
                    }

                    if ((op2b.Point.Y > op2.Point.Y) ||
                      !Helpers.SlopesEqual(op2.Point, op2b.Point, j.OffPoint))
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
                    op1b = op1.Duplicate(false);
                    op2b = op2.Duplicate(true);
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
                    op1b = op1.Duplicate(true);
                    op2b = op2.Duplicate(false);
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
                OutRec firstLeft = outRec.GetFirstLeft();
                if (outRec.Points != null && firstLeft == oldOutRec)
                {
                    if (newOutRec.Points.Contains(outRec.Points))
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

                OutRec firstLeft = outRec.GetFirstLeft();
                if (firstLeft != orfl && firstLeft != innerOutRec && firstLeft != outerOutRec)
                {
                    continue;
                }

                if (innerOutRec.Points.Contains(outRec.Points))
                {
                    outRec.FirstLeft = innerOutRec;
                }
                else if (outerOutRec.Points.Contains(outRec.Points))
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
                OutRec firstLeft = outRec.GetFirstLeft();
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
                    holeStateRec = outRec1.GetLowermostRec(outRec2);
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
                    outRec2.SyncroniseOutpointIndexes();

                    if (outRec1.Points.Contains(outRec2.Points))
                    {
                        // outRec1 contains outRec2 ...
                        outRec2.IsHole = !outRec1.IsHole;
                        outRec2.FirstLeft = outRec1;
                        this.FixupFirstLefts2(outRec2, outRec1);
                    }
                    else if (outRec2.Points.Contains(outRec1.Points))
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
                    e.OutIndex = Constants.Unassigned;
                }

                e = lm.RightBound;
                if (e != null)
                {
                    e.Current = e.Bottom;
                    e.OutIndex = Constants.Unassigned;
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
            var result = new OutRec(this.polyOuts.Count);
            this.polyOuts.Add(result);
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
            if (!e.IsHorizontal)
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

        private Edge ProcessBound(Edge edge, bool leftBoundIsForward)
        {
            Edge result = edge;

            if (result.OutIndex == Constants.Skip)
            {
                // check if there are edges beyond the skip edge in the bound and if so
                // create another LocMin and calling ProcessBound once more ...
                edge = result.NextBoundEdge(leftBoundIsForward);

                if (edge == result)
                {
                    result = edge.GetNextEdge(leftBoundIsForward);
                }
                else
                {
                    // there are more edges in the bound beyond result starting with E
                    edge = result.GetNextEdge(leftBoundIsForward);

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

            return edge.FixHorizontals(leftBoundIsForward);
        }
    }
}