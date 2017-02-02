// <copyright file="Clipper.cs" company="Scott Williams">
// Copyright (c) Scott Williams and contributors.
// Licensed under the Apache License, Version 2.0.
// </copyright>
namespace SixLabors.Shapes.PolygonClipper
{
    using System;
    using System.Collections.Generic;
    using System.Collections.Immutable;
    using System.Diagnostics;
    using System.Numerics;

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

        private Maxima maxima;
        private Edge sortedEdges;
        private LocalMinima minimaList;
        private LocalMinima currentLocalMinima;
        private Scanbeam scanbeam;
        private Edge activeEdges;
        private bool resultsDirty = true;
        private ImmutableArray<IShape> results;

        /// <summary>
        /// Initializes a new instance of the <see cref="Clipper"/> class.
        /// </summary>
        /// <param name="shapes">The shapes.</param>
        public Clipper(IEnumerable<ClipableShape> shapes)
        {
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
        /// <param name="shapes">The shapes.</param>
        public void AddShapes(IEnumerable<ClipableShape> shapes)
        {
            Guard.NotNull(shapes, nameof(shapes));
            foreach (var p in shapes)
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
            Guard.NotNull(shapes, nameof(shapes));
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
            Guard.NotNull(shape, nameof(shape));
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
            Guard.NotNull(path, nameof(path));

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
                List<Edge> newEdges = new List<Edge>(hi + 1);
                for (int i = 0; i <= hi; i++)
                {
                    newEdges.Add(new Edge() { SourcePath = path });
                }

                bool isFlat = true;

                // 1. Basic (first) edge initialization ...
                newEdges[1].Current = points[1];

                newEdges[0].Init(newEdges[1], newEdges[hi], points[0]);
                newEdges[hi].Init(newEdges[0], newEdges[hi - 1], points[hi]);
                for (int i = hi - 1; i >= 1; --i)
                {
                    newEdges[i].Init(newEdges[i + 1], newEdges[i - 1], points[i]);
                }

                Edge startEdge = newEdges[0];

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

                this.edges.Add(newEdges);
                Edge loopBreakerEdge = null;

                // workaround to avoid an endless loop in the while loop below when
                // open paths have matching start and end points ...
                if (edge.PreviousEdge.Bottom == edge.PreviousEdge.Top)
                {
                    edge = edge.NextEdge;
                }

                while (true)
                {
                    edge = edge.FindNextLocalMin();
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
                        leftBoundIsForward = false; // Q.NextInLml = Q.prev
                    }
                    else
                    {
                        locMin.LeftBound = edge;
                        locMin.RightBound = edge.PreviousEdge;
                        leftBoundIsForward = true; // Q.NextInLml = Q.next
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

                this.InsertLocalMinimaIntoAel(botY);
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
                    this.InsertLocalMinimaIntoAel(botY);
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

        private void InsertLocalMinimaIntoAel(float botY)
        {
            LocalMinima lm;
            while (this.PopLocalMinima(botY, out lm))
            {
                Edge lb = lm.LeftBound;
                Edge rb = lm.RightBound;

                OutPoint op1 = null;
                if (lb == null)
                {
                    this.InsertEdgeIntoAel(rb, null);
                    this.SetWindingCount(rb);
                    if (this.IsContributing(rb))
                    {
                        op1 = this.AddOutPt(rb, rb.Bottom);
                    }
                }
                else if (rb == null)
                {
                    this.InsertEdgeIntoAel(lb, null);
                    this.SetWindingCount(lb);
                    if (this.IsContributing(lb))
                    {
                        op1 = this.AddOutPt(lb, lb.Bottom);
                    }

                    this.InsertScanbeam(lb.Top.Y);
                }
                else
                {
                    this.InsertEdgeIntoAel(lb, null);
                    this.InsertEdgeIntoAel(rb, lb);
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
                        if (rb.NextInLml != null)
                        {
                            this.InsertScanbeam(rb.NextInLml.Top.Y);
                        }

                        this.AddEdgeToSortedEdgeList(rb);
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

                if (lb.OutIndex >= 0 && lb.PreviousInAel != null &&
                  lb.PreviousInAel.Current.X == lb.Bottom.X &&
                  lb.PreviousInAel.OutIndex >= 0 &&
                  Helpers.SlopesEqual(lb.PreviousInAel.Current, lb.PreviousInAel.Top, lb.Current, lb.Top) &&
                  lb.WindingDelta != 0 && lb.PreviousInAel.WindingDelta != 0)
                {
                    OutPoint op2 = this.AddOutPt(lb.PreviousInAel, lb.Bottom);
                    this.AddJoin(op1, op2, lb.Top);
                }

                if (lb.NextInAel != rb)
                {
                    if (rb.OutIndex >= 0 && rb.PreviousInAel.OutIndex >= 0 &&
                      Helpers.SlopesEqual(rb.PreviousInAel.Current, rb.PreviousInAel.Top, rb.Current, rb.Top) &&
                      rb.WindingDelta != 0 && rb.PreviousInAel.WindingDelta != 0)
                    {
                        OutPoint op2 = this.AddOutPt(rb.PreviousInAel, rb.Bottom);
                        this.AddJoin(op1, op2, rb.Top);
                    }

                    Edge e = lb.NextInAel;
                    if (e != null)
                    {
                        while (e != rb)
                        {
                            // nb: For calculating winding counts etc, IntersectEdges() assumes
                            // that param1 will be to the right of param2 ABOVE the intersection ...
                            this.IntersectEdges(rb, e, lb.Current); // order important here
                            e = e.NextInAel;
                        }
                    }
                }
            }
        }

        private void InsertEdgeIntoAel(Edge edge, Edge startEdge)
        {
            if (this.activeEdges == null)
            {
                edge.PreviousInAel = null;
                edge.NextInAel = null;
                this.activeEdges = edge;
            }
            else if (startEdge == null && this.E2InsertsBeforeE1(this.activeEdges, edge))
            {
                edge.PreviousInAel = null;
                edge.NextInAel = this.activeEdges;
                this.activeEdges.PreviousInAel = edge;
                this.activeEdges = edge;
            }
            else
            {
                if (startEdge == null)
                {
                    startEdge = this.activeEdges;
                }

                while (startEdge.NextInAel != null &&
                  !this.E2InsertsBeforeE1(startEdge.NextInAel, edge))
                {
                    startEdge = startEdge.NextInAel;
                }

                edge.NextInAel = startEdge.NextInAel;
                if (startEdge.NextInAel != null)
                {
                    startEdge.NextInAel.PreviousInAel = edge;
                }

                edge.PreviousInAel = startEdge;
                startEdge.NextInAel = edge;
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
            Edge e = edge.PreviousInAel;

            // find the edge of the same polytype that immediately preceeds 'edge' in AEL
            while (e != null && ((e.PolyType != edge.PolyType) || (e.WindingDelta == 0)))
            {
                e = e.PreviousInAel;
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
                e = e.NextInAel; // ie get ready to calc WindCnt2
            }
            else
            {
                // EvenOdd filling ...
                if (edge.WindingDelta == 0)
                {
                    // are we inside a subj polygon ...
                    bool inside = true;
                    Edge e2 = e.PreviousInAel;
                    while (e2 != null)
                    {
                        if (e2.PolyType == e.PolyType && e2.WindingDelta != 0)
                        {
                            inside = !inside;
                        }

                        e2 = e2.PreviousInAel;
                    }

                    edge.WindingCount = inside ? 0 : 1;
                }
                else
                {
                    edge.WindingCount = edge.WindingDelta;
                }

                edge.WindingCountInOppositePolyType = e.WindingCountInOppositePolyType;
                e = e.NextInAel; // ie get ready to calc WindCnt2
            }

            // update WindCnt2 ...
            // EvenOdd filling ...
            while (e != edge)
            {
                if (e.WindingDelta != 0)
                {
                    edge.WindingCountInOppositePolyType = edge.WindingCountInOppositePolyType == 0 ? 1 : 0;
                }

                e = e.NextInAel;
            }
        }

        private void AddEdgeToSortedEdgeList(Edge edge)
        {
            // SEL pointers in PEdge are use to build transient lists of horizontal edges.
            // However, since we don't need to worry about processing order, all additions
            // are made to the front of the list ...
            if (this.sortedEdges == null)
            {
                this.sortedEdges = edge;
                edge.PreviousInSortedEdgeList = null;
                edge.NextInSortedEdgeList = null;
            }
            else
            {
                edge.NextInSortedEdgeList = this.sortedEdges;
                edge.PreviousInSortedEdgeList = null;
                this.sortedEdges.PreviousInSortedEdgeList = edge;
                this.sortedEdges = edge;
            }
        }

        private bool PopEdgeFromSortedEdgeList(out Edge e)
        {
            // Pop edge from front of SEL (ie SEL is a FILO list)
            e = this.sortedEdges;
            if (e == null)
            {
                return false;
            }

            Edge oldE = e;
            this.sortedEdges = e.NextInSortedEdgeList;
            if (this.sortedEdges != null)
            {
                this.sortedEdges.PreviousInSortedEdgeList = null;
            }

            oldE.NextInSortedEdgeList = null;
            oldE.PreviousInSortedEdgeList = null;
            return true;
        }

        private void CopyAelToSortedEdgeList()
        {
            Edge e = this.activeEdges;
            this.sortedEdges = e;
            while (e != null)
            {
                e.PreviousInSortedEdgeList = e.PreviousInAel;
                e.NextInSortedEdgeList = e.NextInAel;
                e = e.NextInAel;
            }
        }

        private void SwapPositionsInSortedEdgeList(Edge edge1, Edge edge2)
        {
            if (edge1.NextInSortedEdgeList == null && edge1.PreviousInSortedEdgeList == null)
            {
                return;
            }

            if (edge2.NextInSortedEdgeList == null && edge2.PreviousInSortedEdgeList == null)
            {
                return;
            }

            if (edge1.NextInSortedEdgeList == edge2)
            {
                Edge next = edge2.NextInSortedEdgeList;
                if (next != null)
                {
                    next.PreviousInSortedEdgeList = edge1;
                }

                Edge prev = edge1.PreviousInSortedEdgeList;
                if (prev != null)
                {
                    prev.NextInSortedEdgeList = edge2;
                }

                edge2.PreviousInSortedEdgeList = prev;
                edge2.NextInSortedEdgeList = edge1;
                edge1.PreviousInSortedEdgeList = edge2;
                edge1.NextInSortedEdgeList = next;
            }
            else if (edge2.NextInSortedEdgeList == edge1)
            {
                Edge next = edge1.NextInSortedEdgeList;
                if (next != null)
                {
                    next.PreviousInSortedEdgeList = edge2;
                }

                Edge prev = edge2.PreviousInSortedEdgeList;
                if (prev != null)
                {
                    prev.NextInSortedEdgeList = edge1;
                }

                edge1.PreviousInSortedEdgeList = prev;
                edge1.NextInSortedEdgeList = edge2;
                edge2.PreviousInSortedEdgeList = edge1;
                edge2.NextInSortedEdgeList = next;
            }
            else
            {
                Edge next = edge1.NextInSortedEdgeList;
                Edge prev = edge1.PreviousInSortedEdgeList;
                edge1.NextInSortedEdgeList = edge2.NextInSortedEdgeList;
                if (edge1.NextInSortedEdgeList != null)
                {
                    edge1.NextInSortedEdgeList.PreviousInSortedEdgeList = edge1;
                }

                edge1.PreviousInSortedEdgeList = edge2.PreviousInSortedEdgeList;
                if (edge1.PreviousInSortedEdgeList != null)
                {
                    edge1.PreviousInSortedEdgeList.NextInSortedEdgeList = edge1;
                }

                edge2.NextInSortedEdgeList = next;
                if (edge2.NextInSortedEdgeList != null)
                {
                    edge2.NextInSortedEdgeList.PreviousInSortedEdgeList = edge2;
                }

                edge2.PreviousInSortedEdgeList = prev;
                if (edge2.PreviousInSortedEdgeList != null)
                {
                    edge2.PreviousInSortedEdgeList.NextInSortedEdgeList = edge2;
                }
            }

            if (edge1.PreviousInSortedEdgeList == null)
            {
                this.sortedEdges = edge1;
            }
            else if (edge2.PreviousInSortedEdgeList == null)
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
                if (e.PreviousInAel == e2)
                {
                    prevE = e2.PreviousInAel;
                }
                else
                {
                    prevE = e.PreviousInAel;
                }
            }
            else
            {
                result = this.AddOutPt(e2, pt);
                e1.OutIndex = e2.OutIndex;
                e1.Side = EdgeSide.Right;
                e2.Side = EdgeSide.Left;
                e = e2;
                if (e.PreviousInAel == e1)
                {
                    prevE = e1.PreviousInAel;
                }
                else
                {
                    prevE = e.PreviousInAel;
                }
            }

            if (prevE != null && prevE.OutIndex >= 0)
            {
                float prevX = prevE.TopX(pt.Y);
                float edgeX = e.TopX(pt.Y);
                if ((prevX == edgeX) &&
                    (e.WindingDelta != 0) &&
                    (prevE.WindingDelta != 0) &&
                    Helpers.SlopesEqual(new Vector2(prevX, pt.Y), prevE.Top, new Vector2(edgeX, pt.Y), e.Top))
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
            Edge e2 = e.PreviousInAel;
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

                e2 = e2.PreviousInAel;
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
            OutPoint point1Lft = outRec1.Points;
            OutPoint point1Rt = point1Lft.Previous;
            OutPoint point2Lft = outRec2.Points;
            OutPoint point2Rt = point2Lft.Previous;

            // join e2 poly onto e1 poly and delete pointers to e2 ...
            if (e1.Side == EdgeSide.Left)
            {
                if (e2.Side == EdgeSide.Left)
                {
                    // z y x a b c
                    this.ReversePolyPtLinks(point2Lft);
                    point2Lft.Next = point1Lft;
                    point1Lft.Previous = point2Lft;
                    point1Rt.Next = point2Rt;
                    point2Rt.Previous = point1Rt;
                    outRec1.Points = point2Rt;
                }
                else
                {
                    // x y z a b c
                    point2Rt.Next = point1Lft;
                    point1Lft.Previous = point2Rt;
                    point2Lft.Previous = point1Rt;
                    point1Rt.Next = point2Lft;
                    outRec1.Points = point2Lft;
                }
            }
            else
            {
                if (e2.Side == EdgeSide.Right)
                {
                    // a b c z y x
                    this.ReversePolyPtLinks(point2Lft);
                    point1Rt.Next = point2Rt;
                    point2Rt.Previous = point1Rt;
                    point2Lft.Next = point1Lft;
                    point1Lft.Previous = point2Lft;
                }
                else
                {
                    // a b c x y z
                    point1Rt.Next = point2Lft;
                    point2Lft.Previous = point1Rt;
                    point1Lft.Previous = point2Rt;
                    point2Rt.Next = point1Lft;
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

            int idx = e1.OutIndex;
            int obsoleteIdx = e2.OutIndex;

            e1.OutIndex = Constants.Unassigned; // nb: safe because we only get here via AddLocalMaxPoly
            e2.OutIndex = Constants.Unassigned;

            Edge e = this.activeEdges;
            while (e != null)
            {
                if (e.OutIndex == obsoleteIdx)
                {
                    e.OutIndex = idx;
                    e.Side = e1.Side;
                    break;
                }

                e = e.NextInAel;
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
            bool edge1Contributing = e1.OutIndex >= 0;
            bool edge2Contributing = e2.OutIndex >= 0;

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

            int edge1Wc = Math.Abs(e1.WindingCount);
            int edge2Wc = Math.Abs(e2.WindingCount);

            if (edge1Contributing && edge2Contributing)
            {
                if ((edge1Wc != 0 && edge1Wc != 1) || (edge2Wc != 0 && edge2Wc != 1) ||
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
            else if (edge1Contributing)
            {
                if (edge2Wc == 0 || edge2Wc == 1)
                {
                    this.AddOutPt(e1, pt);
                    e1.SwitchSides(e2);
                    e1.SwitchIndexes(e2);
                }
            }
            else if (edge2Contributing)
            {
                if (edge1Wc == 0 || edge1Wc == 1)
                {
                    this.AddOutPt(e2, pt);
                    e1.SwitchSides(e2);
                    e1.SwitchIndexes(e2);
                }
            }
            else if ((edge1Wc == 0 || edge1Wc == 1) && (edge2Wc == 0 || edge2Wc == 1))
            {
                // neither edge is currently contributing ...
                float edge1Wc2 = Math.Abs(e1.WindingCountInOppositePolyType);
                float edge2Wc2 = Math.Abs(e2.WindingCountInOppositePolyType);

                if (e1.PolyType != e2.PolyType)
                {
                    this.AddLocalMinPoly(e1, e2, pt);
                }
                else if (edge1Wc == 1 && edge2Wc == 1)
                {
                    if (((e1.PolyType == ClippingType.Clip) && (edge1Wc2 > 0) && (edge2Wc2 > 0)) ||
                        ((e1.PolyType == ClippingType.Subject) && (edge1Wc2 <= 0) && (edge2Wc2 <= 0)))
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
            while (this.PopEdgeFromSortedEdgeList(out horzEdge))
            {
                this.ProcessHorizontal(horzEdge);
            }
        }

        private void ProcessHorizontal(Edge horzEdge)
        {
            Direction dir;
            float horzLeft, horzRight;
            bool isOpen = horzEdge.WindingDelta == 0;

            dir = horzEdge.GetHorizontalDirection(out horzLeft, out horzRight);

            Edge lastHorzEdge = horzEdge.LastHorizontalEdge();
            Edge maxPairEdge = null;

            if (lastHorzEdge.NextInLml == null)
            {
                maxPairEdge = lastHorzEdge.GetMaximaPair();
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
                Edge e = horzEdge.GetNextInAel(dir);
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
                    if (e.Current.X == horzEdge.Top.X && horzEdge.NextInLml != null &&
                      e.Dx < horzEdge.NextInLml.Dx)
                    {
                        break;
                    }

                    // note: may be done multiple times
                    if (horzEdge.OutIndex >= 0 && !isOpen)
                    {
                        op1 = this.AddOutPt(horzEdge, e.Current);
                        Edge nextHorz = this.sortedEdges;
                        while (nextHorz != null)
                        {
                            if (nextHorz.OutIndex >= 0 &&
                              horzEdge.HorizontalSegmentsOverlap(nextHorz))
                            {
                                OutPoint op2 = this.GetLastOutPt(nextHorz);
                                this.AddJoin(op2, op1, nextHorz.Top);
                            }

                            nextHorz = nextHorz.NextInSortedEdgeList;
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

                        this.DeleteFromAel(horzEdge);
                        this.DeleteFromAel(maxPairEdge);
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

                    Edge next = e.GetNextInAel(dir);
                    this.SwapPositionsInAel(horzEdge, e);
                    e = next;
                }

                // Break out of loop if HorzEdge.NextInLml is not also horizontal ...
                if (horzEdge.NextInLml == null || !horzEdge.NextInLml.IsHorizontal)
                {
                    break;
                }

                this.UpdateEdgeIntoAel(ref horzEdge);
                if (horzEdge.OutIndex >= 0)
                {
                    this.AddOutPt(horzEdge, horzEdge.Bottom);
                }

                dir = horzEdge.GetHorizontalDirection(out horzLeft, out horzRight);
            }

            if (horzEdge.OutIndex >= 0 && op1 == null)
            {
                op1 = this.GetLastOutPt(horzEdge);
                Edge nextHorz = this.sortedEdges;
                while (nextHorz != null)
                {
                    if (nextHorz.OutIndex >= 0 &&
                        horzEdge.HorizontalSegmentsOverlap(nextHorz))
                    {
                        OutPoint op2 = this.GetLastOutPt(nextHorz);
                        this.AddJoin(op2, op1, nextHorz.Top);
                    }

                    nextHorz = nextHorz.NextInSortedEdgeList;
                }

                this.AddGhostJoin(op1, horzEdge.Top);
            }

            if (horzEdge.NextInLml != null)
            {
                if (horzEdge.OutIndex >= 0)
                {
                    op1 = this.AddOutPt(horzEdge, horzEdge.Top);

                    this.UpdateEdgeIntoAel(ref horzEdge);
                    if (horzEdge.WindingDelta == 0)
                    {
                        return;
                    }

                    // nb: HorzEdge is no longer horizontal here
                    Edge prevEdge = horzEdge.PreviousInAel;
                    Edge nextEdge = horzEdge.NextInAel;
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
                    this.UpdateEdgeIntoAel(ref horzEdge);
                }
            }
            else
            {
                if (horzEdge.OutIndex >= 0)
                {
                    this.AddOutPt(horzEdge, horzEdge.Top);
                }

                this.DeleteFromAel(horzEdge);
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
                e.PreviousInSortedEdgeList = e.PreviousInAel;
                e.NextInSortedEdgeList = e.NextInAel;
                e.Current = new Vector2(e.TopX(topY), e.Current.Y);
                e = e.NextInAel;
            }

            // bubblesort ...
            bool isModified = true;
            while (isModified && this.sortedEdges != null)
            {
                isModified = false;
                e = this.sortedEdges;
                while (e.NextInSortedEdgeList != null)
                {
                    Edge nextEdge = e.NextInSortedEdgeList;
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

                        this.SwapPositionsInSortedEdgeList(e, nextEdge);
                        isModified = true;
                    }
                    else
                    {
                        e = nextEdge;
                    }
                }

                if (e.PreviousInSortedEdgeList != null)
                {
                    e.PreviousInSortedEdgeList.NextInSortedEdgeList = null;
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

            this.CopyAelToSortedEdgeList();
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

                this.SwapPositionsInSortedEdgeList(this.intersectList[i].Edge1, this.intersectList[i].Edge2);
            }

            return true;
        }

        private void ProcessIntersectList()
        {
            for (int i = 0; i < this.intersectList.Count; i++)
            {
                IntersectNode node = this.intersectList[i];
                this.IntersectEdges(node.Edge1, node.Edge2, node.Point);
                this.SwapPositionsInAel(node.Edge1, node.Edge2);
            }

            this.intersectList.Clear();
        }

        private void ProcessEdgesAtTopOfScanbeam(float topY)
        {
            // 1. process maxima, treating them as if they're 'bent' horizontal edges,
            // but exclude maxima with horizontal edges. nb: e can't be a horizontal.
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

                    this.UpdateEdgeIntoAel(ref e);

                    // if output polygons share an edge, they'll need joining later ...
                    Edge prevEdge = e.PreviousInAel;
                    Edge nextEdge = e.NextInAel;
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

                e = e.NextInAel;
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
                    Edge maxPair = e.GetMaximaPairEx();
                    isMaximaEdge = maxPair == null || !maxPair.IsHorizontal;
                }

                if (isMaximaEdge)
                {
                    Edge prev = e.PreviousInAel;
                    this.DoMaxima(e);
                    if (prev == null)
                    {
                        e = this.activeEdges;
                    }
                    else
                    {
                        e = prev.NextInAel;
                    }
                }
                else
                {
                    // 2. promote horizontal edges, otherwise update Curr.X and Curr.Y ...
                    if (e.IsIntermediate(topY) && e.NextInLml.IsHorizontal)
                    {
                        this.UpdateEdgeIntoAel(ref e);
                        if (e.OutIndex >= 0)
                        {
                            this.AddOutPt(e, e.Bottom);
                        }

                        this.AddEdgeToSortedEdgeList(e);
                    }
                    else
                    {
                        e.Current = new Vector2(e.TopX(topY), topY);
                    }

                    e = e.NextInAel;
                }
            }
        }

        private void DoMaxima(Edge e)
        {
            Edge maxPair = e?.GetMaximaPairEx();
            if (maxPair == null)
            {
                Debug.Assert(e != null, "e != null");
                if (e.OutIndex >= 0)
                {
                    this.AddOutPt(e, e.Top);
                }

                this.DeleteFromAel(e);
                return;
            }

            Edge nextEdge = e.NextInAel;
            while (nextEdge != null && nextEdge != maxPair)
            {
                this.IntersectEdges(e, nextEdge, e.Top);
                this.SwapPositionsInAel(e, nextEdge);
                nextEdge = e.NextInAel;
            }

            if (e.OutIndex == Constants.Unassigned && maxPair.OutIndex == Constants.Unassigned)
            {
                this.DeleteFromAel(e);
                this.DeleteFromAel(maxPair);
            }
            else if (e.OutIndex >= 0 && maxPair.OutIndex >= 0)
            {
                if (e.OutIndex >= 0)
                {
                    this.AddLocalMaxPoly(e, maxPair, e.Top);
                }

                this.DeleteFromAel(e);
                this.DeleteFromAel(maxPair);
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

        private bool JoinHorz(OutPoint op1, OutPoint op1B, OutPoint op2, OutPoint op2B, Vector2 pt, bool discardLeft)
        {
            Direction dir1 = op1.Point.X > op1B.Point.X ? Direction.RightToLeft : Direction.LeftToRight;
            Direction dir2 = op2.Point.X > op2B.Point.X ? Direction.RightToLeft : Direction.LeftToRight;
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

                op1B = op1.Duplicate(!discardLeft);
                if (op1B.Point != pt)
                {
                    op1 = op1B;
                    op1.Point = pt;
                    op1B = op1.Duplicate(!discardLeft);
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

                op1B = op1.Duplicate(discardLeft);
                if (op1B.Point != pt)
                {
                    op1 = op1B;
                    op1.Point = pt;
                    op1B = op1.Duplicate(discardLeft);
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

                op2B = op2.Duplicate(!discardLeft);
                if (op2B.Point != pt)
                {
                    op2 = op2B;
                    op2.Point = pt;
                    op2B = op2.Duplicate(!discardLeft);
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

                op2B = op2.Duplicate(discardLeft);
                if (op2B.Point != pt)
                {
                    op2 = op2B;
                    op2.Point = pt;
                    op2B = op2.Duplicate(discardLeft);
                }
            }

            if ((dir1 == Direction.LeftToRight) == discardLeft)
            {
                op1.Previous = op2;
                op2.Next = op1;
                op1B.Next = op2B;
                op2B.Previous = op1B;
            }
            else
            {
                op1.Next = op2;
                op2.Previous = op1;
                op1B.Previous = op2B;
                op2B.Next = op1B;
            }

            return true;
        }

        private bool JoinPoints(Join j, OutRec outRec1, OutRec outRec2)
        {
            OutPoint op1 = j.OutPoint1;
            OutPoint op1B;
            OutPoint op2 = j.OutPoint2;
            OutPoint op2B;

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

                op1B = j.OutPoint1.Next;
                while (op1B != op1 && (op1B.Point == j.OffPoint))
                {
                    op1B = op1B.Next;
                }

                bool reverse1 = op1B.Point.Y > j.OffPoint.Y;
                op2B = j.OutPoint2.Next;
                while (op2B != op2 && (op2B.Point == j.OffPoint))
                {
                    op2B = op2B.Next;
                }

                bool reverse2 = op2B.Point.Y > j.OffPoint.Y;
                if (reverse1 == reverse2)
                {
                    return false;
                }

                if (reverse1)
                {
                    op1B = op1.Duplicate(false);
                    op2B = op2.Duplicate(true);
                    op1.Previous = op2;
                    op2.Next = op1;
                    op1B.Next = op2B;
                    op2B.Previous = op1B;
                    j.OutPoint1 = op1;
                    j.OutPoint2 = op1B;
                    return true;
                }
                else
                {
                    op1B = op1.Duplicate(true);
                    op2B = op2.Duplicate(false);
                    op1.Next = op2;
                    op2.Previous = op1;
                    op1B.Previous = op2B;
                    op2B.Next = op1B;
                    j.OutPoint1 = op1;
                    j.OutPoint2 = op1B;
                    return true;
                }
            }
            else if (isHorizontal)
            {
                // treat horizontal joins differently to non-horizontal joins since with
                // them we're not yet sure where the overlapping is. OutPt1.Pt & OutPt2.Pt
                // may be anywhere along the horizontal edge.
                op1B = op1;
                while (op1.Previous.Point.Y == op1.Point.Y && op1.Previous != op1B && op1.Previous != op2)
                {
                    op1 = op1.Previous;
                }

                while (op1B.Next.Point.Y == op1B.Point.Y && op1B.Next != op1 && op1B.Next != op2)
                {
                    op1B = op1B.Next;
                }

                if (op1B.Next == op1 || op1B.Next == op2)
                {
                    return false; // a flat 'polygon'
                }

                op2B = op2;
                while (op2.Previous.Point.Y == op2.Point.Y && op2.Previous != op2B && op2.Previous != op1B)
                {
                    op2 = op2.Previous;
                }

                while (op2B.Next.Point.Y == op2B.Point.Y && op2B.Next != op2 && op2B.Next != op1)
                {
                    op2B = op2B.Next;
                }

                if (op2B.Next == op2 || op2B.Next == op1)
                {
                    return false; // a flat 'polygon'
                }

                float left, right;

                // Op1 -. Op1b & Op2 -. Op2b are the extremites of the horizontal edges
                if (!Helpers.GetOverlap(op1.Point.X, op1B.Point.X, op2.Point.X, op2B.Point.X, out left, out right))
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
                    discardLeftSide = op1.Point.X > op1B.Point.X;
                }
                else if (op2.Point.X >= left && op2.Point.X <= right)
                {
                    pt = op2.Point;
                    discardLeftSide = op2.Point.X > op2B.Point.X;
                }
                else if (op1B.Point.X >= left && op1B.Point.X <= right)
                {
                    pt = op1B.Point;
                    discardLeftSide = op1B.Point.X > op1.Point.X;
                }
                else
                {
                    pt = op2B.Point;
                    discardLeftSide = op2B.Point.X > op2.Point.X;
                }

                j.OutPoint1 = op1;
                j.OutPoint2 = op2;
                return this.JoinHorz(op1, op1B, op2, op2B, pt, discardLeftSide);
            }
            else
            {
                // nb: For non-horizontal joins ...
                // 1. Jr.OutPt1.Pt.Y == Jr.OutPt2.Pt.Y
                // 2. Jr.OutPt1.Pt > Jr.OffPt.Y

                // make sure the polygons are correctly oriented ...
                op1B = op1.Next;
                while ((op1B.Point == op1.Point) && (op1B != op1))
                {
                    op1B = op1B.Next;
                }

                bool reverse1 = (op1B.Point.Y > op1.Point.Y) || !Helpers.SlopesEqual(op1.Point, op1B.Point, j.OffPoint);
                if (reverse1)
                {
                    op1B = op1.Previous;
                    while ((op1B.Point == op1.Point) && (op1B != op1))
                    {
                        op1B = op1B.Previous;
                    }

                    if ((op1B.Point.Y > op1.Point.Y) ||
                      !Helpers.SlopesEqual(op1.Point, op1B.Point, j.OffPoint))
                    {
                        return false;
                    }
                }

                op2B = op2.Next;
                while ((op2B.Point == op2.Point) && (op2B != op2))
                {
                    op2B = op2B.Next;
                }

                bool reverse2 = (op2B.Point.Y > op2.Point.Y) || !Helpers.SlopesEqual(op2.Point, op2B.Point, j.OffPoint);
                if (reverse2)
                {
                    op2B = op2.Previous;
                    while ((op2B.Point == op2.Point) && (op2B != op2))
                    {
                        op2B = op2B.Previous;
                    }

                    if ((op2B.Point.Y > op2.Point.Y) ||
                      !Helpers.SlopesEqual(op2.Point, op2B.Point, j.OffPoint))
                    {
                        return false;
                    }
                }

                if ((op1B == op1) || (op2B == op2) || (op1B == op2B) ||
                  ((outRec1 == outRec2) && (reverse1 == reverse2)))
                {
                    return false;
                }

                if (reverse1)
                {
                    op1B = op1.Duplicate(false);
                    op2B = op2.Duplicate(true);
                    op1.Previous = op2;
                    op2.Next = op1;
                    op1B.Next = op2B;
                    op2B.Previous = op1B;
                    j.OutPoint1 = op1;
                    j.OutPoint2 = op1B;
                    return true;
                }
                else
                {
                    op1B = op1.Duplicate(true);
                    op2B = op2.Duplicate(false);
                    op1.Next = op2;
                    op2.Previous = op1;
                    op1B.Previous = op2B;
                    op2B.Next = op1B;
                    j.OutPoint1 = op1;
                    j.OutPoint2 = op1B;
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

        private void UpdateEdgeIntoAel(ref Edge e)
        {
            if (e.NextInLml == null)
            {
                throw new ClipperException("UpdateEdgeIntoAEL: invalid call");
            }

            Edge aelPrev = e.PreviousInAel;
            Edge aelNext = e.NextInAel;
            e.NextInLml.OutIndex = e.OutIndex;
            if (aelPrev != null)
            {
                aelPrev.NextInAel = e.NextInLml;
            }
            else
            {
                this.activeEdges = e.NextInLml;
            }

            if (aelNext != null)
            {
                aelNext.PreviousInAel = e.NextInLml;
            }

            e.NextInLml.Side = e.Side;
            e.NextInLml.WindingDelta = e.WindingDelta;
            e.NextInLml.WindingCount = e.WindingCount;
            e.NextInLml.WindingCountInOppositePolyType = e.WindingCountInOppositePolyType;
            e = e.NextInLml;
            e.Current = e.Bottom;
            e.PreviousInAel = aelPrev;
            e.NextInAel = aelNext;
            if (!e.IsHorizontal)
            {
                this.InsertScanbeam(e.Top.Y);
            }
        }

        private void SwapPositionsInAel(Edge edge1, Edge edge2)
        {
            // check that one or other edge hasn't already been removed from AEL ...
            if (edge1.NextInAel == edge1.PreviousInAel ||
              edge2.NextInAel == edge2.PreviousInAel)
            {
                return;
            }

            if (edge1.NextInAel == edge2)
            {
                Edge next = edge2.NextInAel;
                if (next != null)
                {
                    next.PreviousInAel = edge1;
                }

                Edge prev = edge1.PreviousInAel;
                if (prev != null)
                {
                    prev.NextInAel = edge2;
                }

                edge2.PreviousInAel = prev;
                edge2.NextInAel = edge1;
                edge1.PreviousInAel = edge2;
                edge1.NextInAel = next;
            }
            else if (edge2.NextInAel == edge1)
            {
                Edge next = edge1.NextInAel;
                if (next != null)
                {
                    next.PreviousInAel = edge2;
                }

                Edge prev = edge2.PreviousInAel;
                if (prev != null)
                {
                    prev.NextInAel = edge1;
                }

                edge1.PreviousInAel = prev;
                edge1.NextInAel = edge2;
                edge2.PreviousInAel = edge1;
                edge2.NextInAel = next;
            }
            else
            {
                Edge next = edge1.NextInAel;
                Edge prev = edge1.PreviousInAel;
                edge1.NextInAel = edge2.NextInAel;
                if (edge1.NextInAel != null)
                {
                    edge1.NextInAel.PreviousInAel = edge1;
                }

                edge1.PreviousInAel = edge2.PreviousInAel;
                if (edge1.PreviousInAel != null)
                {
                    edge1.PreviousInAel.NextInAel = edge1;
                }

                edge2.NextInAel = next;
                if (edge2.NextInAel != null)
                {
                    edge2.NextInAel.PreviousInAel = edge2;
                }

                edge2.PreviousInAel = prev;
                if (edge2.PreviousInAel != null)
                {
                    edge2.PreviousInAel.NextInAel = edge2;
                }
            }

            if (edge1.PreviousInAel == null)
            {
                this.activeEdges = edge1;
            }
            else if (edge2.PreviousInAel == null)
            {
                this.activeEdges = edge2;
            }
        }

        private void DeleteFromAel(Edge e)
        {
            Edge aelPrev = e.PreviousInAel;
            Edge aelNext = e.NextInAel;
            if (aelPrev == null && aelNext == null && (e != this.activeEdges))
            {
                return; // already deleted
            }

            if (aelPrev != null)
            {
                aelPrev.NextInAel = aelNext;
            }
            else
            {
                this.activeEdges = aelNext;
            }

            if (aelNext != null)
            {
                aelNext.PreviousInAel = aelPrev;
            }

            e.NextInAel = null;
            e.PreviousInAel = null;
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