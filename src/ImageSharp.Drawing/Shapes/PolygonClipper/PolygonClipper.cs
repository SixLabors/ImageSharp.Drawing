// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace SixLabors.ImageSharp.Drawing.Shapes.PolygonClipper
{
    /// <summary>
    /// Contains functions that cover most polygon boolean and offsetting needs.
    /// Ported from <see href="https://github.com/AngusJohnson/Clipper2"/> and originally licensed
    /// under <see href="http://www.boost.org/LICENSE_1_0.txt"/>
    /// </summary>
    internal sealed class PolygonClipper
    {
        private ClippingOperation clipType;
        private FillRule fillRule;
        private Active actives;
        private Active flaggedHorizontal;
        private readonly List<LocalMinima> minimaList;
        private readonly List<IntersectNode> intersectList;
        private readonly List<Vertex> vertexList;
        private readonly List<OutRec> outrecList;
        private readonly List<float> scanlineList;
        private readonly List<HorzSegment> horzSegList;
        private readonly List<HorzJoin> horzJoinList;
        private int currentLocMin;
        private float currentBotY;
        private bool isSortedMinimaList;
        private bool hasOpenPaths;

        public PolygonClipper()
        {
            this.minimaList = new List<LocalMinima>();
            this.intersectList = new List<IntersectNode>();
            this.vertexList = new List<Vertex>();
            this.outrecList = new List<OutRec>();
            this.scanlineList = new List<float>();
            this.horzSegList = new List<HorzSegment>();
            this.horzJoinList = new List<HorzJoin>();
            this.PreserveCollinear = true;
        }

        public bool PreserveCollinear { get; set; }

        public bool ReverseSolution { get; set; }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AddSubject(PathsF paths) => this.AddPaths(paths, ClippingType.Subject);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AddPath(PathF path, ClippingType polytype, bool isOpen = false)
        {
            PathsF tmp = new(1) { path };
            this.AddPaths(tmp, polytype, isOpen);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AddPaths(PathsF paths, ClippingType polytype, bool isOpen = false)
        {
            if (isOpen)
            {
                this.hasOpenPaths = true;
            }

            this.isSortedMinimaList = false;
            this.AddPathsToVertexList(paths, polytype, isOpen);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Execute(ClippingOperation clipType, FillRule fillRule, PathsF solutionClosed)
            => this.Execute(clipType, fillRule, solutionClosed, new PathsF());

        public void Execute(ClippingOperation clipType, FillRule fillRule, PathsF solutionClosed, PathsF solutionOpen)
        {
            solutionClosed.Clear();
            solutionOpen.Clear();

            try
            {
                this.ExecuteInternal(clipType, fillRule);
                this.BuildPaths(solutionClosed, solutionOpen);
            }
            catch (Exception ex)
            {
                throw new ClipperException("An error occurred while attempting to clip the polygon. See the inner exception for details.", ex);
            }
            finally
            {
                this.ClearSolutionOnly();
            }
        }

        private void ExecuteInternal(ClippingOperation ct, FillRule fillRule)
        {
            if (ct == ClippingOperation.None)
            {
                return;
            }

            this.fillRule = fillRule;
            this.clipType = ct;
            this.Reset();
            if (!this.PopScanline(out float y))
            {
                return;
            }

            while (true)
            {
                this.InsertLocalMinimaIntoAEL(y);
                Active ae;
                while (this.PopHorz(out ae))
                {
                    this.DoHorizontal(ae);
                }

                if (this.horzSegList.Count > 0)
                {
                    this.ConvertHorzSegsToJoins();
                    this.horzSegList.Clear();
                }

                this.currentBotY = y; // bottom of scanbeam
                if (!this.PopScanline(out y))
                {
                    break; // y new top of scanbeam
                }

                this.DoIntersections(y);
                this.DoTopOfScanbeam(y);
                while (this.PopHorz(out ae))
                {
                    this.DoHorizontal(ae!);
                }
            }

            this.ProcessHorzJoins();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void DoIntersections(float topY)
        {
            if (this.BuildIntersectList(topY))
            {
                this.ProcessIntersectList();
                this.DisposeIntersectNodes();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void DisposeIntersectNodes()
            => this.intersectList.Clear();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void AddNewIntersectNode(Active ae1, Active ae2, float topY)
        {
            if (!ClipperUtils.GetIntersectPt(ae1.Bot, ae1.Top, ae2.Bot, ae2.Top, out Vector2 ip))
            {
                ip = new Vector2(ae1.CurX, topY);
            }

            if (ip.Y > this.currentBotY || ip.Y < topY)
            {
                float absDx1 = MathF.Abs(ae1.Dx);
                float absDx2 = MathF.Abs(ae2.Dx);

                // TODO: Check threshold here once we remove upscaling.
                if (absDx1 > 100 && absDx2 > 100)
                {
                    if (absDx1 > absDx2)
                    {
                        ip = ClipperUtils.GetClosestPtOnSegment(ip, ae1.Bot, ae1.Top);
                    }
                    else
                    {
                        ip = ClipperUtils.GetClosestPtOnSegment(ip, ae2.Bot, ae2.Top);
                    }
                }
                else if (absDx1 > 100)
                {
                    ip = ClipperUtils.GetClosestPtOnSegment(ip, ae1.Bot, ae1.Top);
                }
                else if (absDx2 > 100)
                {
                    ip = ClipperUtils.GetClosestPtOnSegment(ip, ae2.Bot, ae2.Top);
                }
                else
                {
                    if (ip.Y < topY)
                    {
                        ip.Y = topY;
                    }
                    else
                    {
                        ip.Y = this.currentBotY;
                    }

                    if (absDx1 < absDx2)
                    {
                        ip.X = TopX(ae1, ip.Y);
                    }
                    else
                    {
                        ip.X = TopX(ae2, ip.Y);
                    }
                }
            }

            IntersectNode node = new(ip, ae1, ae2);
            this.intersectList.Add(node);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool SetHorzSegHeadingForward(HorzSegment hs, OutPt opP, OutPt opN)
        {
            if (opP.Point.X == opN.Point.X)
            {
                return false;
            }

            if (opP.Point.X < opN.Point.X)
            {
                hs.LeftOp = opP;
                hs.RightOp = opN;
                hs.LeftToRight = true;
            }
            else
            {
                hs.LeftOp = opN;
                hs.RightOp = opP;
                hs.LeftToRight = false;
            }

            return true;
        }

        private static bool UpdateHorzSegment(HorzSegment hs)
        {
            OutPt op = hs.LeftOp;
            OutRec outrec = GetRealOutRec(op.OutRec);
            bool outrecHasEdges = outrec.FrontEdge != null;
            float curr_y = op.Point.Y;
            OutPt opP = op, opN = op;
            if (outrecHasEdges)
            {
                OutPt opA = outrec.Pts!, opZ = opA.Next;
                while (opP != opZ && opP.Prev.Point.Y == curr_y)
                {
                    opP = opP.Prev;
                }

                while (opN != opA && opN.Next.Point.Y == curr_y)
                {
                    opN = opN.Next;
                }
            }
            else
            {
                while (opP.Prev != opN && opP.Prev.Point.Y == curr_y)
                {
                    opP = opP.Prev;
                }

                while (opN.Next != opP && opN.Next.Point.Y == curr_y)
                {
                    opN = opN.Next;
                }
            }

            bool result = SetHorzSegHeadingForward(hs, opP, opN) && hs.LeftOp.HorizSegment == null;

            if (result)
            {
                hs.LeftOp.HorizSegment = hs;
            }
            else
            {
                hs.RightOp = null; // (for sorting)
            }

            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static OutPt DuplicateOp(OutPt op, bool insert_after)
        {
            OutPt result = new(op.Point, op.OutRec);
            if (insert_after)
            {
                result.Next = op.Next;
                result.Next.Prev = result;
                result.Prev = op;
                op.Next = result;
            }
            else
            {
                result.Prev = op.Prev;
                result.Prev.Next = result;
                result.Next = op;
                op.Prev = result;
            }

            return result;
        }

        private void ConvertHorzSegsToJoins()
        {
            int k = 0;
            foreach (HorzSegment hs in this.horzSegList)
            {
                if (UpdateHorzSegment(hs))
                {
                    k++;
                }
            }

            if (k < 2)
            {
                return;
            }

            this.horzSegList.Sort(default(HorzSegSorter));

            for (int i = 0; i < k - 1; i++)
            {
                HorzSegment hs1 = this.horzSegList[i];

                // for each HorzSegment, find others that overlap
                for (int j = i + 1; j < k; j++)
                {
                    HorzSegment hs2 = this.horzSegList[j];
                    if ((hs2.LeftOp.Point.X >= hs1.RightOp.Point.X) ||
                        (hs2.LeftToRight == hs1.LeftToRight) ||
                        (hs2.RightOp.Point.X <= hs1.LeftOp.Point.X))
                    {
                        continue;
                    }

                    float curr_y = hs1.LeftOp.Point.Y;
                    if (hs1.LeftToRight)
                    {
                        while (hs1.LeftOp.Next.Point.Y == curr_y &&
                          hs1.LeftOp.Next.Point.X <= hs2.LeftOp.Point.X)
                        {
                            hs1.LeftOp = hs1.LeftOp.Next;
                        }

                        while (hs2.LeftOp.Prev.Point.Y == curr_y &&
                          hs2.LeftOp.Prev.Point.X <= hs1.LeftOp.Point.X)
                        {
                            hs2.LeftOp = hs2.LeftOp.Prev;
                        }

                        HorzJoin join = new(DuplicateOp(hs1.LeftOp, true), DuplicateOp(hs2.LeftOp, false));
                        this.horzJoinList.Add(join);
                    }
                    else
                    {
                        while (hs1.LeftOp.Prev.Point.Y == curr_y &&
                          hs1.LeftOp.Prev.Point.X <= hs2.LeftOp.Point.X)
                        {
                            hs1.LeftOp = hs1.LeftOp.Prev;
                        }

                        while (hs2.LeftOp.Next.Point.Y == curr_y &&
                          hs2.LeftOp.Next.Point.X <= hs1.LeftOp.Point.X)
                        {
                            hs2.LeftOp = hs2.LeftOp.Next;
                        }

                        HorzJoin join = new(DuplicateOp(hs2.LeftOp, true), DuplicateOp(hs1.LeftOp, false));
                        this.horzJoinList.Add(join);
                    }
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ClearSolutionOnly()
        {
            while (this.actives != null)
            {
                this.DeleteFromAEL(this.actives);
            }

            this.scanlineList.Clear();
            this.DisposeIntersectNodes();
            this.outrecList.Clear();
            this.horzSegList.Clear();
            this.horzJoinList.Clear();
        }

        private bool BuildPaths(PathsF solutionClosed, PathsF solutionOpen)
        {
            solutionClosed.Clear();
            solutionOpen.Clear();
            solutionClosed.Capacity = this.outrecList.Count;
            solutionOpen.Capacity = this.outrecList.Count;

            int i = 0;

            // _outrecList.Count is not static here because
            // CleanCollinear can indirectly add additional OutRec
            while (i < this.outrecList.Count)
            {
                OutRec outrec = this.outrecList[i++];
                if (outrec.Pts == null)
                {
                    continue;
                }

                PathF path = new();
                if (outrec.IsOpen)
                {
                    if (BuildPath(outrec.Pts, this.ReverseSolution, true, path))
                    {
                        solutionOpen.Add(path);
                    }
                }
                else
                {
                    this.CleanCollinear(outrec);

                    // closed paths should always return a Positive orientation
                    // except when ReverseSolution == true
                    if (BuildPath(outrec.Pts, this.ReverseSolution, false, path))
                    {
                        solutionClosed.Add(path);
                    }
                }
            }

            return true;
        }

        private static bool BuildPath(OutPt op, bool reverse, bool isOpen, PathF path)
        {
            if (op == null || op.Next == op || (!isOpen && op.Next == op.Prev))
            {
                return false;
            }

            path.Clear();

            Vector2 lastPt;
            OutPt op2;
            if (reverse)
            {
                lastPt = op.Point;
                op2 = op.Prev;
            }
            else
            {
                op = op.Next;
                lastPt = op.Point;
                op2 = op.Next;
            }

            path.Add(lastPt);

            while (op2 != op)
            {
                if (op2.Point != lastPt)
                {
                    lastPt = op2.Point;
                    path.Add(lastPt);
                }

                if (reverse)
                {
                    op2 = op2.Prev;
                }
                else
                {
                    op2 = op2.Next;
                }
            }

            return path.Count != 3 || !IsVerySmallTriangle(op2);
        }

        private void DoHorizontal(Active horz)
        /*******************************************************************************
         * Notes: Horizontal edges (HEs) at scanline intersections (i.e. at the top or    *
         * bottom of a scanbeam) are processed as if layered.The order in which HEs     *
         * are processed doesn't matter. HEs intersect with the bottom vertices of      *
         * other HEs[#] and with non-horizontal edges [*]. Once these intersections     *
         * are completed, intermediate HEs are 'promoted' to the next edge in their     *
         * bounds, and they in turn may be intersected[%] by other HEs.                 *
         *                                                                              *
         * eg: 3 horizontals at a scanline:    /   |                     /           /  *
         *              |                     /    |     (HE3)o ========%========== o   *
         *              o ======= o(HE2)     /     |         /         /                *
         *          o ============#=========*======*========#=========o (HE1)           *
         *         /              |        /       |       /                            *
         *******************************************************************************/
        {
            Vector2 pt;
            bool horzIsOpen = IsOpen(horz);
            float y = horz.Bot.Y;

            Vertex vertex_max = horzIsOpen ? GetCurrYMaximaVertex_Open(horz) : GetCurrYMaximaVertex(horz);

            // remove 180 deg.spikes and also simplify
            // consecutive horizontals when PreserveCollinear = true
            if (vertex_max != null &&
              !horzIsOpen && vertex_max != horz.VertexTop)
            {
                TrimHorz(horz, this.PreserveCollinear);
            }

            bool isLeftToRight = ResetHorzDirection(horz, vertex_max, out float leftX, out float rightX);

            if (IsHotEdge(horz))
            {
                OutPt op = AddOutPt(horz, new Vector2(horz.CurX, y));
                this.AddToHorzSegList(op);
            }

            OutRec currOutrec = horz.Outrec;

            while (true)
            {
                // loops through consec. horizontal edges (if open)
                Active ae = isLeftToRight ? horz.NextInAEL : horz.PrevInAEL;

                while (ae != null)
                {
                    if (ae.VertexTop == vertex_max)
                    {
                        // do this first!!
                        if (IsHotEdge(horz) && IsJoined(ae!))
                        {
                            this.Split(ae, ae.Top);
                        }

                        if (IsHotEdge(horz))
                        {
                            while (horz.VertexTop != vertex_max)
                            {
                                AddOutPt(horz, horz.Top);
                                this.UpdateEdgeIntoAEL(horz);
                            }

                            if (isLeftToRight)
                            {
                                this.AddLocalMaxPoly(horz, ae, horz.Top);
                            }
                            else
                            {
                                this.AddLocalMaxPoly(ae, horz, horz.Top);
                            }
                        }

                        this.DeleteFromAEL(ae);
                        this.DeleteFromAEL(horz);
                        return;
                    }

                    // if horzEdge is a maxima, keep going until we reach
                    // its maxima pair, otherwise check for break conditions
                    if (vertex_max != horz.VertexTop || IsOpenEnd(horz))
                    {
                        // otherwise stop when 'ae' is beyond the end of the horizontal line
                        if ((isLeftToRight && ae.CurX > rightX) || (!isLeftToRight && ae.CurX < leftX))
                        {
                            break;
                        }

                        if (ae.CurX == horz.Top.X && !IsHorizontal(ae))
                        {
                            pt = NextVertex(horz).Point;

                            // to maximize the possibility of putting open edges into
                            // solutions, we'll only break if it's past HorzEdge's end
                            if (IsOpen(ae) && !IsSamePolyType(ae, horz) && !IsHotEdge(ae))
                            {
                                if ((isLeftToRight && (TopX(ae, pt.Y) > pt.X)) ||
                                  (!isLeftToRight && (TopX(ae, pt.Y) < pt.X)))
                                {
                                    break;
                                }
                            }

                            // otherwise for edges at horzEdge's end, only stop when horzEdge's
                            // outslope is greater than e's slope when heading right or when
                            // horzEdge's outslope is less than e's slope when heading left.
                            else if ((isLeftToRight && (TopX(ae, pt.Y) >= pt.X)) || (!isLeftToRight && (TopX(ae, pt.Y) <= pt.X)))
                            {
                                break;
                            }
                        }
                    }

                    pt = new Vector2(ae.CurX, y);

                    if (isLeftToRight)
                    {
                        this.IntersectEdges(horz, ae, pt);
                        this.SwapPositionsInAEL(horz, ae);
                        horz.CurX = ae.CurX;
                        ae = horz.NextInAEL;
                    }
                    else
                    {
                        this.IntersectEdges(ae, horz, pt);
                        this.SwapPositionsInAEL(ae, horz);
                        horz.CurX = ae.CurX;
                        ae = horz.PrevInAEL;
                    }

                    if (IsHotEdge(horz) && (horz.Outrec != currOutrec))
                    {
                        currOutrec = horz.Outrec;
                        this.AddToHorzSegList(this.GetLastOp(horz));
                    }

                    // we've reached the end of this horizontal
                }

                // check if we've finished looping
                // through consecutive horizontals
                // ie open at top
                if (horzIsOpen && IsOpenEnd(horz))
                {
                    if (IsHotEdge(horz))
                    {
                        AddOutPt(horz, horz.Top);
                        if (IsFront(horz))
                        {
                            horz.Outrec.FrontEdge = null;
                        }
                        else
                        {
                            horz.Outrec.BackEdge = null;
                        }

                        horz.Outrec = null;
                    }

                    this.DeleteFromAEL(horz);
                    return;
                }
                else if (NextVertex(horz).Point.Y != horz.Top.Y)
                {
                    break;
                }

                // still more horizontals in bound to process ...
                if (IsHotEdge(horz))
                {
                    AddOutPt(horz, horz.Top);
                }

                this.UpdateEdgeIntoAEL(horz);

                if (this.PreserveCollinear && !horzIsOpen && HorzIsSpike(horz))
                {
                    TrimHorz(horz, true);
                }

                isLeftToRight = ResetHorzDirection(horz, vertex_max, out leftX, out rightX);

                // end for loop and end of (possible consecutive) horizontals
            }

            if (IsHotEdge(horz))
            {
                this.AddToHorzSegList(AddOutPt(horz, horz.Top));
            }

            this.UpdateEdgeIntoAEL(horz); // this is the end of an intermediate horiz.
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void DoTopOfScanbeam(float y)
        {
            this.flaggedHorizontal = null; // sel_ is reused to flag horizontals (see PushHorz below)
            Active ae = this.actives;
            while (ae != null)
            {
                // NB 'ae' will never be horizontal here
                if (ae.Top.Y == y)
                {
                    ae.CurX = ae.Top.X;
                    if (IsMaxima(ae))
                    {
                        ae = this.DoMaxima(ae); // TOP OF BOUND (MAXIMA)
                        continue;
                    }

                    // INTERMEDIATE VERTEX ...
                    if (IsHotEdge(ae))
                    {
                        AddOutPt(ae, ae.Top);
                    }

                    this.UpdateEdgeIntoAEL(ae);
                    if (IsHorizontal(ae))
                    {
                        this.PushHorz(ae); // horizontals are processed later
                    }
                }
                else
                {
                    // i.e. not the top of the edge
                    ae.CurX = TopX(ae, y);
                }

                ae = ae.NextInAEL;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private Active DoMaxima(Active ae)
        {
            Active prevE;
            Active nextE, maxPair;
            prevE = ae.PrevInAEL;
            nextE = ae.NextInAEL;

            if (IsOpenEnd(ae))
            {
                if (IsHotEdge(ae))
                {
                    AddOutPt(ae, ae.Top);
                }

                if (!IsHorizontal(ae))
                {
                    if (IsHotEdge(ae))
                    {
                        if (IsFront(ae))
                        {
                            ae.Outrec.FrontEdge = null;
                        }
                        else
                        {
                            ae.Outrec.BackEdge = null;
                        }

                        ae.Outrec = null;
                    }

                    this.DeleteFromAEL(ae);
                }

                return nextE;
            }

            maxPair = GetMaximaPair(ae);
            if (maxPair == null)
            {
                return nextE; // eMaxPair is horizontal
            }

            if (IsJoined(ae))
            {
                this.Split(ae, ae.Top);
            }

            if (IsJoined(maxPair))
            {
                this.Split(maxPair, maxPair.Top);
            }

            // only non-horizontal maxima here.
            // process any edges between maxima pair ...
            while (nextE != maxPair)
            {
                this.IntersectEdges(ae, nextE!, ae.Top);
                this.SwapPositionsInAEL(ae, nextE!);
                nextE = ae.NextInAEL;
            }

            if (IsOpen(ae))
            {
                if (IsHotEdge(ae))
                {
                    this.AddLocalMaxPoly(ae, maxPair, ae.Top);
                }

                this.DeleteFromAEL(maxPair);
                this.DeleteFromAEL(ae);
                return prevE != null ? prevE.NextInAEL : this.actives;
            }

            // here ae.nextInAel == ENext == EMaxPair ...
            if (IsHotEdge(ae))
            {
                this.AddLocalMaxPoly(ae, maxPair, ae.Top);
            }

            this.DeleteFromAEL(ae);
            this.DeleteFromAEL(maxPair);
            return prevE != null ? prevE.NextInAEL : this.actives;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void TrimHorz(Active horzEdge, bool preserveCollinear)
        {
            bool wasTrimmed = false;
            Vector2 pt = NextVertex(horzEdge).Point;

            while (pt.Y == horzEdge.Top.Y)
            {
                // always trim 180 deg. spikes (in closed paths)
                // but otherwise break if preserveCollinear = true
                if (preserveCollinear && (pt.X < horzEdge.Top.X) != (horzEdge.Bot.X < horzEdge.Top.X))
                {
                    break;
                }

                horzEdge.VertexTop = NextVertex(horzEdge);
                horzEdge.Top = pt;
                wasTrimmed = true;
                if (IsMaxima(horzEdge))
                {
                    break;
                }

                pt = NextVertex(horzEdge).Point;
            }

            if (wasTrimmed)
            {
                SetDx(horzEdge); // +/-infinity
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void AddToHorzSegList(OutPt op)
        {
            if (op.OutRec.IsOpen)
            {
                return;
            }

            this.horzSegList.Add(new HorzSegment(op));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private OutPt GetLastOp(Active hotEdge)
        {
            OutRec outrec = hotEdge.Outrec;
            return (hotEdge == outrec.FrontEdge) ? outrec.Pts : outrec.Pts.Next;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vertex GetCurrYMaximaVertex_Open(Active ae)
        {
            Vertex result = ae.VertexTop;
            if (ae.WindDx > 0)
            {
                while (result.Next.Point.Y == result.Point.Y && ((result.Flags & (VertexFlags.OpenEnd | VertexFlags.LocalMax)) == VertexFlags.None))
                {
                    result = result.Next;
                }
            }
            else
            {
                while (result.Prev.Point.Y == result.Point.Y && ((result.Flags & (VertexFlags.OpenEnd | VertexFlags.LocalMax)) == VertexFlags.None))
                {
                    result = result.Prev;
                }
            }

            if (!IsMaxima(result))
            {
                result = null; // not a maxima
            }

            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vertex GetCurrYMaximaVertex(Active ae)
        {
            Vertex result = ae.VertexTop;
            if (ae.WindDx > 0)
            {
                while (result.Next.Point.Y == result.Point.Y)
                {
                    result = result.Next;
                }
            }
            else
            {
                while (result.Prev.Point.Y == result.Point.Y)
                {
                    result = result.Prev;
                }
            }

            if (!IsMaxima(result))
            {
                result = null; // not a maxima
            }

            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsVerySmallTriangle(OutPt op)
            => op.Next.Next == op.Prev
            && (PtsReallyClose(op.Prev.Point, op.Next.Point)
            || PtsReallyClose(op.Point, op.Next.Point)
            || PtsReallyClose(op.Point, op.Prev.Point));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsValidClosedPath(OutPt op)
            => op != null && op.Next != op && (op.Next != op.Prev || !IsVerySmallTriangle(op));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static OutPt DisposeOutPt(OutPt op)
        {
            OutPt result = op.Next == op ? null : op.Next;
            op.Prev.Next = op.Next;
            op.Next.Prev = op.Prev;

            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static BoundsF GetBounds(OutPt op)
        {
            BoundsF result = new(op.Point.X, op.Point.Y, op.Point.X, op.Point.Y);
            OutPt op2 = op.Next;
            while (op2 != op)
            {
                if (op2.Point.X < result.Left)
                {
                    result.Left = op2.Point.X;
                }
                else if (op2.Point.X > result.Right)
                {
                    result.Right = op2.Point.X;
                }

                if (op2.Point.Y < result.Top)
                {
                    result.Top = op2.Point.Y;
                }
                else if (op2.Point.Y > result.Bottom)
                {
                    result.Bottom = op2.Point.Y;
                }

                op2 = op2.Next;
            }

            return result;
        }

        private static PointInPolygonResult PointInOpPolygon(Vector2 pt, OutPt op)
        {
            if (op == op.Next || op.Prev == op.Next)
            {
                return PointInPolygonResult.IsOutside;
            }

            OutPt op2 = op;
            do
            {
                if (op.Point.Y != pt.Y)
                {
                    break;
                }

                op = op.Next;
            }
            while (op != op2);

            // not a proper polygon
            if (op.Point.Y == pt.Y)
            {
                return PointInPolygonResult.IsOutside;
            }

            // must be above or below to get here
            bool isAbove = op.Point.Y < pt.Y, startingAbove = isAbove;
            int val = 0;

            op2 = op.Next;
            while (op2 != op)
            {
                if (isAbove)
                {
                    while (op2 != op && op2.Point.Y < pt.Y)
                    {
                        op2 = op2.Next;
                    }
                }
                else
                {
                    while (op2 != op && op2.Point.Y > pt.Y)
                    {
                        op2 = op2.Next;
                    }
                }

                if (op2 == op)
                {
                    break;
                }

                // must have touched or crossed the pt.Y horizonal
                // and this must happen an even number of times
                // touching the horizontal
                if (op2.Point.Y == pt.Y)
                {
                    if (op2.Point.X == pt.X || (op2.Point.Y == op2.Prev.Point.Y
                        && (pt.X < op2.Prev.Point.X) != (pt.X < op2.Point.X)))
                    {
                        return PointInPolygonResult.IsOn;
                    }

                    op2 = op2.Next;
                    if (op2 == op)
                    {
                        break;
                    }

                    continue;
                }

                if (op2.Point.X <= pt.X || op2.Prev.Point.X <= pt.X)
                {
                    if (op2.Prev.Point.X < pt.X && op2.Point.X < pt.X)
                    {
                        val = 1 - val; // toggle val
                    }
                    else
                    {
                        float d = ClipperUtils.CrossProduct(op2.Prev.Point, op2.Point, pt);
                        if (d == 0)
                        {
                            return PointInPolygonResult.IsOn;
                        }

                        if ((d < 0) == isAbove)
                        {
                            val = 1 - val;
                        }
                    }
                }

                isAbove = !isAbove;
                op2 = op2.Next;
            }

            if (isAbove != startingAbove)
            {
                float d = ClipperUtils.CrossProduct(op2.Prev.Point, op2.Point, pt);
                if (d == 0)
                {
                    return PointInPolygonResult.IsOn;
                }

                if ((d < 0) == isAbove)
                {
                    val = 1 - val;
                }
            }

            if (val == 0)
            {
                return PointInPolygonResult.IsOutside;
            }
            else
            {
                return PointInPolygonResult.IsInside;
            }
        }

        private void ProcessHorzJoins()
        {
            foreach (HorzJoin j in this.horzJoinList)
            {
                OutRec or1 = GetRealOutRec(j.Op1.OutRec);
                OutRec or2 = GetRealOutRec(j.Op2.OutRec);

                OutPt op1b = j.Op1.Next;
                OutPt op2b = j.Op2.Prev;
                j.Op1.Next = j.Op2;
                j.Op2.Prev = j.Op1;
                op1b.Prev = op2b;
                op2b.Next = op1b;

                if (or1 == or2)
                {
                    or2 = new OutRec
                    {
                        Pts = op1b
                    };

                    FixOutRecPts(or2);

                    if (or1.Pts.OutRec == or2)
                    {
                        or1.Pts = j.Op1;
                        or1.Pts.OutRec = or1;
                    }

                    or2.Owner = or1;

                    this.outrecList.Add(or2);
                }
                else
                {
                    or2.Pts = null;
                    or2.Owner = or1;
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool PtsReallyClose(Vector2 pt1, Vector2 pt2)

            // TODO: Check scale once we can remove upscaling.
            => (Math.Abs(pt1.X - pt2.X) < 2F) && (Math.Abs(pt1.Y - pt2.Y) < 2F);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void CleanCollinear(OutRec outrec)
        {
            outrec = GetRealOutRec(outrec);

            if (outrec?.IsOpen != false)
            {
                return;
            }

            if (!IsValidClosedPath(outrec.Pts))
            {
                outrec.Pts = null;
                return;
            }

            OutPt startOp = outrec.Pts;
            OutPt op2 = startOp;
            do
            {
                // NB if preserveCollinear == true, then only remove 180 deg. spikes
                if ((ClipperUtils.CrossProduct(op2.Prev.Point, op2.Point, op2.Next.Point) == 0)
                    && ((op2.Point == op2.Prev.Point) || (op2.Point == op2.Next.Point) || !this.PreserveCollinear || (ClipperUtils.DotProduct(op2.Prev.Point, op2.Point, op2.Next.Point) < 0)))
                {
                    if (op2 == outrec.Pts)
                    {
                        outrec.Pts = op2.Prev;
                    }

                    op2 = DisposeOutPt(op2);
                    if (!IsValidClosedPath(op2))
                    {
                        outrec.Pts = null;
                        return;
                    }

                    startOp = op2;
                    continue;
                }

                op2 = op2.Next;
            }
            while (op2 != startOp);

            this.FixSelfIntersects(outrec);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void DoSplitOp(OutRec outrec, OutPt splitOp)
        {
            // splitOp.prev <=> splitOp &&
            // splitOp.next <=> splitOp.next.next are intersecting
            OutPt prevOp = splitOp.Prev;
            OutPt nextNextOp = splitOp.Next.Next;
            outrec.Pts = prevOp;
            OutPt result = prevOp;

            ClipperUtils.GetIntersectPoint(
                prevOp.Point, splitOp.Point, splitOp.Next.Point, nextNextOp.Point, out Vector2 ip);

            float area1 = Area(prevOp);
            float absArea1 = Math.Abs(area1);

            if (absArea1 < 2)
            {
                outrec.Pts = null;
                return;
            }

            // nb: area1 is the path's area *before* splitting, whereas area2 is
            // the area of the triangle containing splitOp & splitOp.next.
            // So the only way for these areas to have the same sign is if
            // the split triangle is larger than the path containing prevOp or
            // if there's more than one self=intersection.
            float area2 = AreaTriangle(ip, splitOp.Point, splitOp.Next.Point);
            float absArea2 = Math.Abs(area2);

            // de-link splitOp and splitOp.next from the path
            // while inserting the intersection point
            if (ip == prevOp.Point || ip == nextNextOp.Point)
            {
                nextNextOp.Prev = prevOp;
                prevOp.Next = nextNextOp;
            }
            else
            {
                OutPt newOp2 = new(ip, outrec)
                {
                    Prev = prevOp,
                    Next = nextNextOp
                };

                nextNextOp.Prev = newOp2;
                prevOp.Next = newOp2;
            }

            if (absArea2 > 1 && (absArea2 > absArea1 || ((area2 > 0) == (area1 > 0))))
            {
                OutRec newOutRec = this.NewOutRec();
                newOutRec.Owner = outrec.Owner;
                splitOp.OutRec = newOutRec;
                splitOp.Next.OutRec = newOutRec;

                OutPt newOp = new(ip, newOutRec) { Prev = splitOp.Next, Next = splitOp };
                newOutRec.Pts = newOp;
                splitOp.Prev = newOp;
                splitOp.Next.Next = newOp;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void FixSelfIntersects(OutRec outrec)
        {
            OutPt op2 = outrec.Pts;

            // triangles can't self-intersect
            while (op2.Prev != op2.Next.Next)
            {
                if (ClipperUtils.SegsIntersect(op2.Prev.Point, op2.Point, op2.Next.Point, op2.Next.Next.Point))
                {
                    this.DoSplitOp(outrec, op2);
                    if (outrec.Pts == null)
                    {
                        return;
                    }

                    op2 = outrec.Pts;
                    continue;
                }
                else
                {
                    op2 = op2.Next;
                }

                if (op2 == outrec.Pts)
                {
                    break;
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void Reset()
        {
            if (!this.isSortedMinimaList)
            {
                this.minimaList.Sort(default(LocMinSorter));
                this.isSortedMinimaList = true;
            }

            this.scanlineList.Capacity = this.minimaList.Count;
            for (int i = this.minimaList.Count - 1; i >= 0; i--)
            {
                this.scanlineList.Add(this.minimaList[i].Vertex.Point.Y);
            }

            this.currentBotY = 0;
            this.currentLocMin = 0;
            this.actives = null;
            this.flaggedHorizontal = null;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void InsertScanline(float y)
        {
            int index = this.scanlineList.BinarySearch(y);
            if (index >= 0)
            {
                return;
            }

            index = ~index;
            this.scanlineList.Insert(index, y);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool PopScanline(out float y)
        {
            int cnt = this.scanlineList.Count - 1;
            if (cnt < 0)
            {
                y = 0;
                return false;
            }

            y = this.scanlineList[cnt];
            this.scanlineList.RemoveAt(cnt--);
            while (cnt >= 0 && y == this.scanlineList[cnt])
            {
                this.scanlineList.RemoveAt(cnt--);
            }

            return true;
        }

        private void InsertLocalMinimaIntoAEL(float botY)
        {
            LocalMinima localMinima;
            Active leftBound, rightBound;

            // Add any local minima (if any) at BotY
            // NB horizontal local minima edges should contain locMin.vertex.prev
            while (this.HasLocMinAtY(botY))
            {
                localMinima = this.PopLocalMinima();
                if ((localMinima.Vertex.Flags & VertexFlags.OpenStart) != VertexFlags.None)
                {
                    leftBound = null;
                }
                else
                {
                    leftBound = new Active
                    {
                        Bot = localMinima.Vertex.Point,
                        CurX = localMinima.Vertex.Point.X,
                        WindDx = -1,
                        VertexTop = localMinima.Vertex.Prev,
                        Top = localMinima.Vertex.Prev.Point,
                        Outrec = null,
                        LocalMin = localMinima
                    };
                    SetDx(leftBound);
                }

                if ((localMinima.Vertex.Flags & VertexFlags.OpenEnd) != VertexFlags.None)
                {
                    rightBound = null;
                }
                else
                {
                    rightBound = new Active
                    {
                        Bot = localMinima.Vertex.Point,
                        CurX = localMinima.Vertex.Point.X,
                        WindDx = 1,
                        VertexTop = localMinima.Vertex.Next, // i.e. ascending
                        Top = localMinima.Vertex.Next.Point,
                        Outrec = null,
                        LocalMin = localMinima
                    };
                    SetDx(rightBound);
                }

                // Currently LeftB is just the descending bound and RightB is the ascending.
                // Now if the LeftB isn't on the left of RightB then we need swap them.
                if (leftBound != null && rightBound != null)
                {
                    if (IsHorizontal(leftBound))
                    {
                        if (IsHeadingRightHorz(leftBound))
                        {
                            SwapActives(ref leftBound, ref rightBound);
                        }
                    }
                    else if (IsHorizontal(rightBound))
                    {
                        if (IsHeadingLeftHorz(rightBound))
                        {
                            SwapActives(ref leftBound, ref rightBound);
                        }
                    }
                    else if (leftBound.Dx < rightBound.Dx)
                    {
                        SwapActives(ref leftBound, ref rightBound);
                    }

                    // so when leftBound has windDx == 1, the polygon will be oriented
                    // counter-clockwise in Cartesian coords (clockwise with inverted Y).
                }
                else if (leftBound == null)
                {
                    leftBound = rightBound;
                    rightBound = null;
                }

                bool contributing;
                leftBound.IsLeftBound = true;
                this.InsertLeftEdge(leftBound);

                if (IsOpen(leftBound))
                {
                    this.SetWindCountForOpenPathEdge(leftBound);
                    contributing = this.IsContributingOpen(leftBound);
                }
                else
                {
                    this.SetWindCountForClosedPathEdge(leftBound);
                    contributing = this.IsContributingClosed(leftBound);
                }

                if (rightBound != null)
                {
                    rightBound.WindCount = leftBound.WindCount;
                    rightBound.WindCount2 = leftBound.WindCount2;
                    InsertRightEdge(leftBound, rightBound); ///////

                    if (contributing)
                    {
                        this.AddLocalMinPoly(leftBound, rightBound, leftBound.Bot, true);
                        if (!IsHorizontal(leftBound))
                        {
                            this.CheckJoinLeft(leftBound, leftBound.Bot);
                        }
                    }

                    while (rightBound.NextInAEL != null && IsValidAelOrder(rightBound.NextInAEL, rightBound))
                    {
                        this.IntersectEdges(rightBound, rightBound.NextInAEL, rightBound.Bot);
                        this.SwapPositionsInAEL(rightBound, rightBound.NextInAEL);
                    }

                    if (IsHorizontal(rightBound))
                    {
                        this.PushHorz(rightBound);
                    }
                    else
                    {
                        this.CheckJoinRight(rightBound, rightBound.Bot);
                        this.InsertScanline(rightBound.Top.Y);
                    }
                }
                else if (contributing)
                {
                    this.StartOpenPath(leftBound, leftBound.Bot);
                }

                if (IsHorizontal(leftBound))
                {
                    this.PushHorz(leftBound);
                }
                else
                {
                    this.InsertScanline(leftBound.Top.Y);
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Active ExtractFromSEL(Active ae)
        {
            Active res = ae.NextInSEL;
            if (res != null)
            {
                res.PrevInSEL = ae.PrevInSEL;
            }

            ae.PrevInSEL.NextInSEL = res;
            return res;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void Insert1Before2InSEL(Active ae1, Active ae2)
        {
            ae1.PrevInSEL = ae2.PrevInSEL;
            if (ae1.PrevInSEL != null)
            {
                ae1.PrevInSEL.NextInSEL = ae1;
            }

            ae1.NextInSEL = ae2;
            ae2.PrevInSEL = ae1;
        }

        private bool BuildIntersectList(float topY)
        {
            if (this.actives == null || this.actives.NextInAEL == null)
            {
                return false;
            }

            // Calculate edge positions at the top of the current scanbeam, and from this
            // we will determine the intersections required to reach these new positions.
            this.AdjustCurrXAndCopyToSEL(topY);

            // Find all edge intersections in the current scanbeam using a stable merge
            // sort that ensures only adjacent edges are intersecting. Intersect info is
            // stored in FIntersectList ready to be processed in ProcessIntersectList.
            // Re merge sorts see https://stackoverflow.com/a/46319131/359538
            Active left = this.flaggedHorizontal;
            Active right;
            Active lEnd;
            Active rEnd;
            Active currBase;
            Active prevBase;
            Active tmp;

            while (left.Jump != null)
            {
                prevBase = null;
                while (left?.Jump != null)
                {
                    currBase = left;
                    right = left.Jump;
                    lEnd = right;
                    rEnd = right.Jump;
                    left.Jump = rEnd;
                    while (left != lEnd && right != rEnd)
                    {
                        if (right.CurX < left.CurX)
                        {
                            tmp = right.PrevInSEL;
                            while (true)
                            {
                                this.AddNewIntersectNode(tmp, right, topY);
                                if (tmp == left)
                                {
                                    break;
                                }

                                tmp = tmp.PrevInSEL;
                            }

                            tmp = right;
                            right = ExtractFromSEL(tmp);
                            lEnd = right;
                            Insert1Before2InSEL(tmp, left);
                            if (left == currBase)
                            {
                                currBase = tmp;
                                currBase.Jump = rEnd;
                                if (prevBase == null)
                                {
                                    this.flaggedHorizontal = currBase;
                                }
                                else
                                {
                                    prevBase.Jump = currBase;
                                }
                            }
                        }
                        else
                        {
                            left = left.NextInSEL;
                        }
                    }

                    prevBase = currBase;
                    left = rEnd;
                }

                left = this.flaggedHorizontal;
            }

            return this.intersectList.Count > 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ProcessIntersectList()
        {
            // We now have a list of intersections required so that edges will be
            // correctly positioned at the top of the scanbeam. However, it's important
            // that edge intersections are processed from the bottom up, but it's also
            // crucial that intersections only occur between adjacent edges.

            // First we do a quicksort so intersections proceed in a bottom up order ...
            this.intersectList.Sort(default(IntersectListSort));

            // Now as we process these intersections, we must sometimes adjust the order
            // to ensure that intersecting edges are always adjacent ...
            for (int i = 0; i < this.intersectList.Count; ++i)
            {
                if (!EdgesAdjacentInAEL(this.intersectList[i]))
                {
                    int j = i + 1;
                    while (!EdgesAdjacentInAEL(this.intersectList[j]))
                    {
                        j++;
                    }

                    // swap
                    (this.intersectList[j], this.intersectList[i]) =
                      (this.intersectList[i], this.intersectList[j]);
                }

                IntersectNode node = this.intersectList[i];
                this.IntersectEdges(node.Edge1, node.Edge2, node.Point);
                this.SwapPositionsInAEL(node.Edge1, node.Edge2);

                node.Edge1.CurX = node.Point.X;
                node.Edge2.CurX = node.Point.X;
                this.CheckJoinLeft(node.Edge2, node.Point, true);
                this.CheckJoinRight(node.Edge1, node.Point, true);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SwapPositionsInAEL(Active ae1, Active ae2)
        {
            // preconditon: ae1 must be immediately to the left of ae2
            Active next = ae2.NextInAEL;
            if (next != null)
            {
                next.PrevInAEL = ae1;
            }

            Active prev = ae1.PrevInAEL;
            if (prev != null)
            {
                prev.NextInAEL = ae2;
            }

            ae2.PrevInAEL = prev;
            ae2.NextInAEL = ae1;
            ae1.PrevInAEL = ae2;
            ae1.NextInAEL = next;
            if (ae2.PrevInAEL == null)
            {
                this.actives = ae2;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool ResetHorzDirection(Active horz, Vertex vertexMax, out float leftX, out float rightX)
        {
            if (horz.Bot.X == horz.Top.X)
            {
                // the horizontal edge is going nowhere ...
                leftX = horz.CurX;
                rightX = horz.CurX;
                Active ae = horz.NextInAEL;
                while (ae != null && ae.VertexTop != vertexMax)
                {
                    ae = ae.NextInAEL;
                }

                return ae != null;
            }

            if (horz.CurX < horz.Top.X)
            {
                leftX = horz.CurX;
                rightX = horz.Top.X;
                return true;
            }

            leftX = horz.Top.X;
            rightX = horz.CurX;
            return false; // right to left
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool HorzIsSpike(Active horz)
        {
            Vector2 nextPt = NextVertex(horz).Point;
            return (horz.Bot.X < horz.Top.X) != (horz.Top.X < nextPt.X);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Active FindEdgeWithMatchingLocMin(Active e)
        {
            Active result = e.NextInAEL;
            while (result != null)
            {
                if (result.LocalMin == e.LocalMin)
                {
                    return result;
                }

                if (!IsHorizontal(result) && e.Bot != result.Bot)
                {
                    result = null;
                }
                else
                {
                    result = result.NextInAEL;
                }
            }

            result = e.PrevInAEL;
            while (result != null)
            {
                if (result.LocalMin == e.LocalMin)
                {
                    return result;
                }

                if (!IsHorizontal(result) && e.Bot != result.Bot)
                {
                    return null;
                }

                result = result.PrevInAEL;
            }

            return result;
        }

        private OutPt IntersectEdges(Active ae1, Active ae2, Vector2 pt)
        {
            OutPt resultOp = null;

            // MANAGE OPEN PATH INTERSECTIONS SEPARATELY ...
            if (this.hasOpenPaths && (IsOpen(ae1) || IsOpen(ae2)))
            {
                if (IsOpen(ae1) && IsOpen(ae2))
                {
                    return null;
                }

                // the following line avoids duplicating quite a bit of code
                if (IsOpen(ae2))
                {
                    SwapActives(ref ae1, ref ae2);
                }

                if (IsJoined(ae2))
                {
                    this.Split(ae2, pt); // needed for safety
                }

                if (this.clipType == ClippingOperation.Union)
                {
                    if (!IsHotEdge(ae2))
                    {
                        return null;
                    }
                }
                else if (ae2.LocalMin.Polytype == ClippingType.Subject)
                {
                    return null;
                }

                switch (this.fillRule)
                {
                    case FillRule.Positive:
                        if (ae2.WindCount != 1)
                        {
                            return null;
                        }

                        break;
                    case FillRule.Negative:
                        if (ae2.WindCount != -1)
                        {
                            return null;
                        }

                        break;
                    default:
                        if (Math.Abs(ae2.WindCount) != 1)
                        {
                            return null;
                        }

                        break;
                }

                // toggle contribution ...
                if (IsHotEdge(ae1))
                {
                    resultOp = AddOutPt(ae1, pt);
                    if (IsFront(ae1))
                    {
                        ae1.Outrec.FrontEdge = null;
                    }
                    else
                    {
                        ae1.Outrec.BackEdge = null;
                    }

                    ae1.Outrec = null;
                }

                // horizontal edges can pass under open paths at a LocMins
                else if (pt == ae1.LocalMin.Vertex.Point && !IsOpenEnd(ae1.LocalMin.Vertex))
                {
                    // find the other side of the LocMin and
                    // if it's 'hot' join up with it ...
                    Active ae3 = FindEdgeWithMatchingLocMin(ae1);
                    if (ae3 != null && IsHotEdge(ae3))
                    {
                        ae1.Outrec = ae3.Outrec;
                        if (ae1.WindDx > 0)
                        {
                            SetSides(ae3.Outrec!, ae1, ae3);
                        }
                        else
                        {
                            SetSides(ae3.Outrec!, ae3, ae1);
                        }

                        return ae3.Outrec.Pts;
                    }

                    resultOp = this.StartOpenPath(ae1, pt);
                }
                else
                {
                    resultOp = this.StartOpenPath(ae1, pt);
                }

                return resultOp;
            }

            // MANAGING CLOSED PATHS FROM HERE ON
            if (IsJoined(ae1))
            {
                this.Split(ae1, pt);
            }

            if (IsJoined(ae2))
            {
                this.Split(ae2, pt);
            }

            // UPDATE WINDING COUNTS...
            int oldE1WindCount, oldE2WindCount;
            if (ae1.LocalMin.Polytype == ae2.LocalMin.Polytype)
            {
                if (this.fillRule == FillRule.EvenOdd)
                {
                    oldE1WindCount = ae1.WindCount;
                    ae1.WindCount = ae2.WindCount;
                    ae2.WindCount = oldE1WindCount;
                }
                else
                {
                    if (ae1.WindCount + ae2.WindDx == 0)
                    {
                        ae1.WindCount = -ae1.WindCount;
                    }
                    else
                    {
                        ae1.WindCount += ae2.WindDx;
                    }

                    if (ae2.WindCount - ae1.WindDx == 0)
                    {
                        ae2.WindCount = -ae2.WindCount;
                    }
                    else
                    {
                        ae2.WindCount -= ae1.WindDx;
                    }
                }
            }
            else
            {
                if (this.fillRule != FillRule.EvenOdd)
                {
                    ae1.WindCount2 += ae2.WindDx;
                }
                else
                {
                    ae1.WindCount2 = ae1.WindCount2 == 0 ? 1 : 0;
                }

                if (this.fillRule != FillRule.EvenOdd)
                {
                    ae2.WindCount2 -= ae1.WindDx;
                }
                else
                {
                    ae2.WindCount2 = ae2.WindCount2 == 0 ? 1 : 0;
                }
            }

            switch (this.fillRule)
            {
                case FillRule.Positive:
                    oldE1WindCount = ae1.WindCount;
                    oldE2WindCount = ae2.WindCount;
                    break;
                case FillRule.Negative:
                    oldE1WindCount = -ae1.WindCount;
                    oldE2WindCount = -ae2.WindCount;
                    break;
                default:
                    oldE1WindCount = Math.Abs(ae1.WindCount);
                    oldE2WindCount = Math.Abs(ae2.WindCount);
                    break;
            }

            bool e1WindCountIs0or1 = oldE1WindCount is 0 or 1;
            bool e2WindCountIs0or1 = oldE2WindCount is 0 or 1;

            if ((!IsHotEdge(ae1) && !e1WindCountIs0or1) || (!IsHotEdge(ae2) && !e2WindCountIs0or1))
            {
                return null;
            }

            // NOW PROCESS THE INTERSECTION ...

            // if both edges are 'hot' ...
            if (IsHotEdge(ae1) && IsHotEdge(ae2))
            {
                if ((oldE1WindCount != 0 && oldE1WindCount != 1) || (oldE2WindCount != 0 && oldE2WindCount != 1) ||
                    (ae1.LocalMin.Polytype != ae2.LocalMin.Polytype && this.clipType != ClippingOperation.Xor))
                {
                    resultOp = this.AddLocalMaxPoly(ae1, ae2, pt);
                }
                else if (IsFront(ae1) || (ae1.Outrec == ae2.Outrec))
                {
                    // this 'else if' condition isn't strictly needed but
                    // it's sensible to split polygons that ony touch at
                    // a common vertex (not at common edges).
                    resultOp = this.AddLocalMaxPoly(ae1, ae2, pt);
                    this.AddLocalMinPoly(ae1, ae2, pt);
                }
                else
                {
                    // can't treat as maxima & minima
                    resultOp = AddOutPt(ae1, pt);
                    AddOutPt(ae2, pt);
                    SwapOutrecs(ae1, ae2);
                }
            }

            // if one or other edge is 'hot' ...
            else if (IsHotEdge(ae1))
            {
                resultOp = AddOutPt(ae1, pt);
                SwapOutrecs(ae1, ae2);
            }
            else if (IsHotEdge(ae2))
            {
                resultOp = AddOutPt(ae2, pt);
                SwapOutrecs(ae1, ae2);
            }

            // neither edge is 'hot'
            else
            {
                float e1Wc2, e2Wc2;
                switch (this.fillRule)
                {
                    case FillRule.Positive:
                        e1Wc2 = ae1.WindCount2;
                        e2Wc2 = ae2.WindCount2;
                        break;
                    case FillRule.Negative:
                        e1Wc2 = -ae1.WindCount2;
                        e2Wc2 = -ae2.WindCount2;
                        break;
                    default:
                        e1Wc2 = Math.Abs(ae1.WindCount2);
                        e2Wc2 = Math.Abs(ae2.WindCount2);
                        break;
                }

                if (!IsSamePolyType(ae1, ae2))
                {
                    resultOp = this.AddLocalMinPoly(ae1, ae2, pt);
                }
                else if (oldE1WindCount == 1 && oldE2WindCount == 1)
                {
                    resultOp = null;
                    switch (this.clipType)
                    {
                        case ClippingOperation.Union:
                            if (e1Wc2 > 0 && e2Wc2 > 0)
                            {
                                return null;
                            }

                            resultOp = this.AddLocalMinPoly(ae1, ae2, pt);
                            break;

                        case ClippingOperation.Difference:
                            if (((GetPolyType(ae1) == ClippingType.Clip) && (e1Wc2 > 0) && (e2Wc2 > 0))
                                || ((GetPolyType(ae1) == ClippingType.Subject) && (e1Wc2 <= 0) && (e2Wc2 <= 0)))
                            {
                                resultOp = this.AddLocalMinPoly(ae1, ae2, pt);
                            }

                            break;

                        case ClippingOperation.Xor:
                            resultOp = this.AddLocalMinPoly(ae1, ae2, pt);
                            break;

                        default: // ClipType.Intersection:
                            if (e1Wc2 <= 0 || e2Wc2 <= 0)
                            {
                                return null;
                            }

                            resultOp = this.AddLocalMinPoly(ae1, ae2, pt);
                            break;
                    }
                }
            }

            return resultOp;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void DeleteFromAEL(Active ae)
        {
            Active prev = ae.PrevInAEL;
            Active next = ae.NextInAEL;
            if (prev == null && next == null && (ae != this.actives))
            {
                return; // already deleted
            }

            if (prev != null)
            {
                prev.NextInAEL = next;
            }
            else
            {
                this.actives = next;
            }

            if (next != null)
            {
                next.PrevInAEL = prev;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void AdjustCurrXAndCopyToSEL(float topY)
        {
            Active ae = this.actives;
            this.flaggedHorizontal = ae;
            while (ae != null)
            {
                ae.PrevInSEL = ae.PrevInAEL;
                ae.NextInSEL = ae.NextInAEL;
                ae.Jump = ae.NextInSEL;
                if (ae.JoinWith == JoinWith.Left)
                {
                    ae.CurX = ae.PrevInAEL.CurX; // this also avoids complications
                }
                else
                {
                    ae.CurX = TopX(ae, topY);
                }

                // NB don't update ae.curr.Y yet (see AddNewIntersectNode)
                ae = ae.NextInAEL;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool HasLocMinAtY(float y)
            => this.currentLocMin < this.minimaList.Count && this.minimaList[this.currentLocMin].Vertex.Point.Y == y;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private LocalMinima PopLocalMinima()
            => this.minimaList[this.currentLocMin++];

        private void AddPathsToVertexList(PathsF paths, ClippingType polytype, bool isOpen)
        {
            int totalVertCnt = 0;
            for (int i = 0; i < paths.Count; i++)
            {
                PathF path = paths[i];
                totalVertCnt += path.Count;
            }

            this.vertexList.Capacity = this.vertexList.Count + totalVertCnt;

            foreach (PathF path in paths)
            {
                Vertex v0 = null, prev_v = null, curr_v;
                foreach (Vector2 pt in path)
                {
                    if (v0 == null)
                    {
                        v0 = new Vertex(pt, VertexFlags.None, null);
                        this.vertexList.Add(v0);
                        prev_v = v0;
                    }
                    else if (prev_v.Point != pt)
                    {
                        // ie skips duplicates
                        curr_v = new Vertex(pt, VertexFlags.None, prev_v);
                        this.vertexList.Add(curr_v);
                        prev_v.Next = curr_v;
                        prev_v = curr_v;
                    }
                }

                if (prev_v == null || prev_v.Prev == null)
                {
                    continue;
                }

                if (!isOpen && prev_v.Point == v0.Point)
                {
                    prev_v = prev_v.Prev;
                }

                prev_v.Next = v0;
                v0.Prev = prev_v;
                if (!isOpen && prev_v.Next == prev_v)
                {
                    continue;
                }

                // OK, we have a valid path
                bool going_up, going_up0;
                if (isOpen)
                {
                    curr_v = v0.Next;
                    while (curr_v != v0 && curr_v.Point.Y == v0.Point.Y)
                    {
                        curr_v = curr_v.Next;
                    }

                    going_up = curr_v.Point.Y <= v0.Point.Y;
                    if (going_up)
                    {
                        v0.Flags = VertexFlags.OpenStart;
                        this.AddLocMin(v0, polytype, true);
                    }
                    else
                    {
                        v0.Flags = VertexFlags.OpenStart | VertexFlags.LocalMax;
                    }
                }
                else
                {
                    // closed path
                    prev_v = v0.Prev;
                    while (prev_v != v0 && prev_v.Point.Y == v0.Point.Y)
                    {
                        prev_v = prev_v.Prev;
                    }

                    if (prev_v == v0)
                    {
                        continue; // only open paths can be completely flat
                    }

                    going_up = prev_v.Point.Y > v0.Point.Y;
                }

                going_up0 = going_up;
                prev_v = v0;
                curr_v = v0.Next;
                while (curr_v != v0)
                {
                    if (curr_v.Point.Y > prev_v.Point.Y && going_up)
                    {
                        prev_v.Flags |= VertexFlags.LocalMax;
                        going_up = false;
                    }
                    else if (curr_v.Point.Y < prev_v.Point.Y && !going_up)
                    {
                        going_up = true;
                        this.AddLocMin(prev_v, polytype, isOpen);
                    }

                    prev_v = curr_v;
                    curr_v = curr_v.Next;
                }

                if (isOpen)
                {
                    prev_v.Flags |= VertexFlags.OpenEnd;
                    if (going_up)
                    {
                        prev_v.Flags |= VertexFlags.LocalMax;
                    }
                    else
                    {
                        this.AddLocMin(prev_v, polytype, isOpen);
                    }
                }
                else if (going_up != going_up0)
                {
                    if (going_up0)
                    {
                        this.AddLocMin(prev_v, polytype, false);
                    }
                    else
                    {
                        prev_v.Flags |= VertexFlags.LocalMax;
                    }
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void AddLocMin(Vertex vert, ClippingType polytype, bool isOpen)
        {
            // make sure the vertex is added only once.
            if ((vert.Flags & VertexFlags.LocalMin) != VertexFlags.None)
            {
                return;
            }

            vert.Flags |= VertexFlags.LocalMin;

            LocalMinima lm = new(vert, polytype, isOpen);
            this.minimaList.Add(lm);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void PushHorz(Active ae)
        {
            ae.NextInSEL = this.flaggedHorizontal;
            this.flaggedHorizontal = ae;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool PopHorz(out Active ae)
        {
            ae = this.flaggedHorizontal;
            if (this.flaggedHorizontal == null)
            {
                return false;
            }

            this.flaggedHorizontal = this.flaggedHorizontal.NextInSEL;
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private OutPt AddLocalMinPoly(Active ae1, Active ae2, Vector2 pt, bool isNew = false)
        {
            OutRec outrec = this.NewOutRec();
            ae1.Outrec = outrec;
            ae2.Outrec = outrec;

            if (IsOpen(ae1))
            {
                outrec.Owner = null;
                outrec.IsOpen = true;
                if (ae1.WindDx > 0)
                {
                    SetSides(outrec, ae1, ae2);
                }
                else
                {
                    SetSides(outrec, ae2, ae1);
                }
            }
            else
            {
                outrec.IsOpen = false;
                Active prevHotEdge = GetPrevHotEdge(ae1);

                // e.windDx is the winding direction of the **input** paths
                // and unrelated to the winding direction of output polygons.
                // Output orientation is determined by e.outrec.frontE which is
                // the ascending edge (see AddLocalMinPoly).
                if (prevHotEdge != null)
                {
                    outrec.Owner = prevHotEdge.Outrec;
                    if (OutrecIsAscending(prevHotEdge) == isNew)
                    {
                        SetSides(outrec, ae2, ae1);
                    }
                    else
                    {
                        SetSides(outrec, ae1, ae2);
                    }
                }
                else
                {
                    outrec.Owner = null;
                    if (isNew)
                    {
                        SetSides(outrec, ae1, ae2);
                    }
                    else
                    {
                        SetSides(outrec, ae2, ae1);
                    }
                }
            }

            OutPt op = new(pt, outrec);
            outrec.Pts = op;
            return op;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void SetDx(Active ae)
            => ae.Dx = GetDx(ae.Bot, ae.Top);

        /*******************************************************************************
        *  Dx:                             0(90deg)                                    *
        *                                  |                                           *
        *               +inf (180deg) <--- o --. -inf (0deg)                          *
        *******************************************************************************/

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float GetDx(Vector2 pt1, Vector2 pt2)
        {
            float dy = pt2.Y - pt1.Y;
            if (dy != 0)
            {
                return (pt2.X - pt1.X) / dy;
            }

            if (pt2.X > pt1.X)
            {
                return float.NegativeInfinity;
            }

            return float.PositiveInfinity;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float TopX(Active ae, float currentY)
        {
            Vector2 top = ae.Top;
            Vector2 bottom = ae.Bot;

            if ((currentY == top.Y) || (top.X == bottom.X))
            {
                return top.X;
            }

            if (currentY == bottom.Y)
            {
                return bottom.X;
            }

            return bottom.X + (ae.Dx * (currentY - bottom.Y));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsHorizontal(Active ae)
            => ae.Top.Y == ae.Bot.Y;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsHeadingRightHorz(Active ae)
            => float.IsNegativeInfinity(ae.Dx);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsHeadingLeftHorz(Active ae)
            => float.IsPositiveInfinity(ae.Dx);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void SwapActives(ref Active ae1, ref Active ae2)
            => (ae2, ae1) = (ae1, ae2);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ClippingType GetPolyType(Active ae)
            => ae.LocalMin.Polytype;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsSamePolyType(Active ae1, Active ae2)
            => ae1.LocalMin.Polytype == ae2.LocalMin.Polytype;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool IsContributingClosed(Active ae)
        {
            switch (this.fillRule)
            {
                case FillRule.Positive:
                    if (ae.WindCount != 1)
                    {
                        return false;
                    }

                    break;
                case FillRule.Negative:
                    if (ae.WindCount != -1)
                    {
                        return false;
                    }

                    break;
                case FillRule.NonZero:
                    if (Math.Abs(ae.WindCount) != 1)
                    {
                        return false;
                    }

                    break;
            }

            switch (this.clipType)
            {
                case ClippingOperation.Intersection:
                    return this.fillRule switch
                    {
                        FillRule.Positive => ae.WindCount2 > 0,
                        FillRule.Negative => ae.WindCount2 < 0,
                        _ => ae.WindCount2 != 0,
                    };

                case ClippingOperation.Union:
                    return this.fillRule switch
                    {
                        FillRule.Positive => ae.WindCount2 <= 0,
                        FillRule.Negative => ae.WindCount2 >= 0,
                        _ => ae.WindCount2 == 0,
                    };

                case ClippingOperation.Difference:
                    bool result = this.fillRule switch
                    {
                        FillRule.Positive => ae.WindCount2 <= 0,
                        FillRule.Negative => ae.WindCount2 >= 0,
                        _ => ae.WindCount2 == 0,
                    };
                    return (GetPolyType(ae) == ClippingType.Subject) ? result : !result;

                case ClippingOperation.Xor:
                    return true; // XOr is always contributing unless open

                default:
                    return false;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool IsContributingOpen(Active ae)
        {
            bool isInClip, isInSubj;
            switch (this.fillRule)
            {
                case FillRule.Positive:
                    isInSubj = ae.WindCount > 0;
                    isInClip = ae.WindCount2 > 0;
                    break;
                case FillRule.Negative:
                    isInSubj = ae.WindCount < 0;
                    isInClip = ae.WindCount2 < 0;
                    break;
                default:
                    isInSubj = ae.WindCount != 0;
                    isInClip = ae.WindCount2 != 0;
                    break;
            }

            bool result = this.clipType switch
            {
                ClippingOperation.Intersection => isInClip,
                ClippingOperation.Union => !isInSubj && !isInClip,
                _ => !isInClip
            };
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetWindCountForClosedPathEdge(Active ae)
        {
            // Wind counts refer to polygon regions not edges, so here an edge's WindCnt
            // indicates the higher of the wind counts for the two regions touching the
            // edge. (nb: Adjacent regions can only ever have their wind counts differ by
            // one. Also, open paths have no meaningful wind directions or counts.)
            Active ae2 = ae.PrevInAEL;

            // find the nearest closed path edge of the same PolyType in AEL (heading left)
            ClippingType pt = GetPolyType(ae);
            while (ae2 != null && (GetPolyType(ae2) != pt || IsOpen(ae2)))
            {
                ae2 = ae2.PrevInAEL;
            }

            if (ae2 == null)
            {
                ae.WindCount = ae.WindDx;
                ae2 = this.actives;
            }
            else if (this.fillRule == FillRule.EvenOdd)
            {
                ae.WindCount = ae.WindDx;
                ae.WindCount2 = ae2.WindCount2;
                ae2 = ae2.NextInAEL;
            }
            else
            {
                // NonZero, positive, or negative filling here ...
                // when e2's WindCnt is in the SAME direction as its WindDx,
                // then polygon will fill on the right of 'e2' (and 'e' will be inside)
                // nb: neither e2.WindCnt nor e2.WindDx should ever be 0.
                if (ae2.WindCount * ae2.WindDx < 0)
                {
                    // opposite directions so 'ae' is outside 'ae2' ...
                    if (Math.Abs(ae2.WindCount) > 1)
                    {
                        // outside prev poly but still inside another.
                        if (ae2.WindDx * ae.WindDx < 0)
                        {
                            // reversing direction so use the same WC
                            ae.WindCount = ae2.WindCount;
                        }
                        else
                        {
                            // otherwise keep 'reducing' the WC by 1 (i.e. towards 0) ...
                            ae.WindCount = ae2.WindCount + ae.WindDx;
                        }
                    }
                    else
                    {
                        // now outside all polys of same polytype so set own WC ...
                        ae.WindCount = IsOpen(ae) ? 1 : ae.WindDx;
                    }
                }
                else
                {
                    // 'ae' must be inside 'ae2'
                    if (ae2.WindDx * ae.WindDx < 0)
                    {
                        // reversing direction so use the same WC
                        ae.WindCount = ae2.WindCount;
                    }
                    else
                    {
                        // otherwise keep 'increasing' the WC by 1 (i.e. away from 0) ...
                        ae.WindCount = ae2.WindCount + ae.WindDx;
                    }
                }

                ae.WindCount2 = ae2.WindCount2;
                ae2 = ae2.NextInAEL; // i.e. get ready to calc WindCnt2
            }

            // update windCount2 ...
            if (this.fillRule == FillRule.EvenOdd)
            {
                while (ae2 != ae)
                {
                    if (GetPolyType(ae2!) != pt && !IsOpen(ae2!))
                    {
                        ae.WindCount2 = ae.WindCount2 == 0 ? 1 : 0;
                    }

                    ae2 = ae2.NextInAEL;
                }
            }
            else
            {
                while (ae2 != ae)
                {
                    if (GetPolyType(ae2!) != pt && !IsOpen(ae2!))
                    {
                        ae.WindCount2 += ae2.WindDx;
                    }

                    ae2 = ae2.NextInAEL;
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetWindCountForOpenPathEdge(Active ae)
        {
            Active ae2 = this.actives;
            if (this.fillRule == FillRule.EvenOdd)
            {
                int cnt1 = 0, cnt2 = 0;
                while (ae2 != ae)
                {
                    if (GetPolyType(ae2!) == ClippingType.Clip)
                    {
                        cnt2++;
                    }
                    else if (!IsOpen(ae2!))
                    {
                        cnt1++;
                    }

                    ae2 = ae2.NextInAEL;
                }

                ae.WindCount = IsOdd(cnt1) ? 1 : 0;
                ae.WindCount2 = IsOdd(cnt2) ? 1 : 0;
            }
            else
            {
                while (ae2 != ae)
                {
                    if (GetPolyType(ae2!) == ClippingType.Clip)
                    {
                        ae.WindCount2 += ae2.WindDx;
                    }
                    else if (!IsOpen(ae2!))
                    {
                        ae.WindCount += ae2.WindDx;
                    }

                    ae2 = ae2.NextInAEL;
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsValidAelOrder(Active resident, Active newcomer)
        {
            if (newcomer.CurX != resident.CurX)
            {
                return newcomer.CurX > resident.CurX;
            }

            // get the turning direction  a1.top, a2.bot, a2.top
            float d = ClipperUtils.CrossProduct(resident.Top, newcomer.Bot, newcomer.Top);
            if (d != 0)
            {
                return d < 0;
            }

            // edges must be collinear to get here

            // for starting open paths, place them according to
            // the direction they're about to turn
            if (!IsMaxima(resident) && (resident.Top.Y > newcomer.Top.Y))
            {
                return ClipperUtils.CrossProduct(newcomer.Bot, resident.Top, NextVertex(resident).Point) <= 0;
            }

            if (!IsMaxima(newcomer) && (newcomer.Top.Y > resident.Top.Y))
            {
                return ClipperUtils.CrossProduct(newcomer.Bot, newcomer.Top, NextVertex(newcomer).Point) >= 0;
            }

            float y = newcomer.Bot.Y;
            bool newcomerIsLeft = newcomer.IsLeftBound;

            if (resident.Bot.Y != y || resident.LocalMin.Vertex.Point.Y != y)
            {
                return newcomer.IsLeftBound;
            }

            // resident must also have just been inserted
            if (resident.IsLeftBound != newcomerIsLeft)
            {
                return newcomerIsLeft;
            }

            if (ClipperUtils.CrossProduct(PrevPrevVertex(resident).Point, resident.Bot, resident.Top) == 0)
            {
                return true;
            }

            // compare turning direction of the alternate bound
            return (ClipperUtils.CrossProduct(PrevPrevVertex(resident).Point, newcomer.Bot, PrevPrevVertex(newcomer).Point) > 0) == newcomerIsLeft;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void InsertLeftEdge(Active ae)
        {
            Active ae2;

            if (this.actives == null)
            {
                ae.PrevInAEL = null;
                ae.NextInAEL = null;
                this.actives = ae;
            }
            else if (!IsValidAelOrder(this.actives, ae))
            {
                ae.PrevInAEL = null;
                ae.NextInAEL = this.actives;
                this.actives.PrevInAEL = ae;
                this.actives = ae;
            }
            else
            {
                ae2 = this.actives;
                while (ae2.NextInAEL != null && IsValidAelOrder(ae2.NextInAEL, ae))
                {
                    ae2 = ae2.NextInAEL;
                }

                // don't separate joined edges
                if (ae2.JoinWith == JoinWith.Right)
                {
                    ae2 = ae2.NextInAEL;
                }

                ae.NextInAEL = ae2.NextInAEL;
                if (ae2.NextInAEL != null)
                {
                    ae2.NextInAEL.PrevInAEL = ae;
                }

                ae.PrevInAEL = ae2;
                ae2.NextInAEL = ae;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void InsertRightEdge(Active ae, Active ae2)
        {
            ae2.NextInAEL = ae.NextInAEL;
            if (ae.NextInAEL != null)
            {
                ae.NextInAEL.PrevInAEL = ae2;
            }

            ae2.PrevInAEL = ae;
            ae.NextInAEL = ae2;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vertex NextVertex(Active ae)
        {
            if (ae.WindDx > 0)
            {
                return ae.VertexTop.Next;
            }

            return ae.VertexTop.Prev;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vertex PrevPrevVertex(Active ae)
        {
            if (ae.WindDx > 0)
            {
                return ae.VertexTop.Prev.Prev;
            }

            return ae.VertexTop.Next.Next;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsMaxima(Vertex vertex)
            => (vertex.Flags & VertexFlags.LocalMax) != VertexFlags.None;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsMaxima(Active ae)
            => IsMaxima(ae.VertexTop);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Active GetMaximaPair(Active ae)
        {
            Active ae2;
            ae2 = ae.NextInAEL;
            while (ae2 != null)
            {
                if (ae2.VertexTop == ae.VertexTop)
                {
                    return ae2; // Found!
                }

                ae2 = ae2.NextInAEL;
            }

            return null;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsOdd(int val)
            => (val & 1) != 0;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsHotEdge(Active ae)
            => ae.Outrec != null;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsOpen(Active ae)
            => ae.LocalMin.IsOpen;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsOpenEnd(Active ae)
            => ae.LocalMin.IsOpen && IsOpenEnd(ae.VertexTop);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsOpenEnd(Vertex v)
            => (v.Flags & (VertexFlags.OpenStart | VertexFlags.OpenEnd)) != VertexFlags.None;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Active GetPrevHotEdge(Active ae)
        {
            Active prev = ae.PrevInAEL;
            while (prev != null && (IsOpen(prev) || !IsHotEdge(prev)))
            {
                prev = prev.PrevInAEL;
            }

            return prev;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void JoinOutrecPaths(Active ae1, Active ae2)
        {
            // join ae2 outrec path onto ae1 outrec path and then delete ae2 outrec path
            // pointers. (NB Only very rarely do the joining ends share the same coords.)
            OutPt p1Start = ae1.Outrec.Pts;
            OutPt p2Start = ae2.Outrec.Pts;
            OutPt p1End = p1Start.Next;
            OutPt p2End = p2Start.Next;
            if (IsFront(ae1))
            {
                p2End.Prev = p1Start;
                p1Start.Next = p2End;
                p2Start.Next = p1End;
                p1End.Prev = p2Start;
                ae1.Outrec.Pts = p2Start;

                // nb: if IsOpen(e1) then e1 & e2 must be a 'maximaPair'
                ae1.Outrec.FrontEdge = ae2.Outrec.FrontEdge;
                if (ae1.Outrec.FrontEdge != null)
                {
                    ae1.Outrec.FrontEdge.Outrec = ae1.Outrec;
                }
            }
            else
            {
                p1End.Prev = p2Start;
                p2Start.Next = p1End;
                p1Start.Next = p2End;
                p2End.Prev = p1Start;

                ae1.Outrec.BackEdge = ae2.Outrec.BackEdge;
                if (ae1.Outrec.BackEdge != null)
                {
                    ae1.Outrec.BackEdge.Outrec = ae1.Outrec;
                }
            }

            // after joining, the ae2.OutRec must contains no vertices ...
            ae2.Outrec.FrontEdge = null;
            ae2.Outrec.BackEdge = null;
            ae2.Outrec.Pts = null;
            SetOwner(ae2.Outrec, ae1.Outrec);

            if (IsOpenEnd(ae1))
            {
                ae2.Outrec.Pts = ae1.Outrec.Pts;
                ae1.Outrec.Pts = null;
            }

            // and ae1 and ae2 are maxima and are about to be dropped from the Actives list.
            ae1.Outrec = null;
            ae2.Outrec = null;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static OutPt AddOutPt(Active ae, Vector2 pt)
        {
            // Outrec.OutPts: a circular doubly-linked-list of POutPt where ...
            // opFront[.Prev]* ~~~> opBack & opBack == opFront.Next
            OutRec outrec = ae.Outrec;
            bool toFront = IsFront(ae);
            OutPt opFront = outrec.Pts;
            OutPt opBack = opFront.Next;

            if (toFront && (pt == opFront.Point))
            {
                return opFront;
            }
            else if (!toFront && (pt == opBack.Point))
            {
                return opBack;
            }

            OutPt newOp = new(pt, outrec);
            opBack.Prev = newOp;
            newOp.Prev = opFront;
            newOp.Next = opBack;
            opFront.Next = newOp;
            if (toFront)
            {
                outrec.Pts = newOp;
            }

            return newOp;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private OutRec NewOutRec()
        {
            OutRec result = new()
            {
                Idx = this.outrecList.Count
            };
            this.outrecList.Add(result);
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private OutPt StartOpenPath(Active ae, Vector2 pt)
        {
            OutRec outrec = this.NewOutRec();
            outrec.IsOpen = true;
            if (ae.WindDx > 0)
            {
                outrec.FrontEdge = ae;
                outrec.BackEdge = null;
            }
            else
            {
                outrec.FrontEdge = null;
                outrec.BackEdge = ae;
            }

            ae.Outrec = outrec;
            OutPt op = new(pt, outrec);
            outrec.Pts = op;
            return op;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void UpdateEdgeIntoAEL(Active ae)
        {
            ae.Bot = ae.Top;
            ae.VertexTop = NextVertex(ae);
            ae.Top = ae.VertexTop.Point;
            ae.CurX = ae.Bot.X;
            SetDx(ae);

            if (IsJoined(ae))
            {
                this.Split(ae, ae.Bot);
            }

            if (IsHorizontal(ae))
            {
                return;
            }

            this.InsertScanline(ae.Top.Y);

            this.CheckJoinLeft(ae, ae.Bot);
            this.CheckJoinRight(ae, ae.Bot, true);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void SetSides(OutRec outrec, Active startEdge, Active endEdge)
        {
            outrec.FrontEdge = startEdge;
            outrec.BackEdge = endEdge;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void SwapOutrecs(Active ae1, Active ae2)
        {
            OutRec or1 = ae1.Outrec; // at least one edge has
            OutRec or2 = ae2.Outrec; // an assigned outrec
            if (or1 == or2)
            {
                (or1.BackEdge, or1.FrontEdge) = (or1.FrontEdge, or1.BackEdge);
                return;
            }

            if (or1 != null)
            {
                if (ae1 == or1.FrontEdge)
                {
                    or1.FrontEdge = ae2;
                }
                else
                {
                    or1.BackEdge = ae2;
                }
            }

            if (or2 != null)
            {
                if (ae2 == or2.FrontEdge)
                {
                    or2.FrontEdge = ae1;
                }
                else
                {
                    or2.BackEdge = ae1;
                }
            }

            ae1.Outrec = or2;
            ae2.Outrec = or1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void SetOwner(OutRec outrec, OutRec newOwner)
        {
            // precondition1: new_owner is never null
            while (newOwner.Owner != null && newOwner.Owner.Pts == null)
            {
                newOwner.Owner = newOwner.Owner.Owner;
            }

            // make sure that outrec isn't an owner of newOwner
            OutRec tmp = newOwner;
            while (tmp != null && tmp != outrec)
            {
                tmp = tmp.Owner;
            }

            if (tmp != null)
            {
                newOwner.Owner = outrec.Owner;
            }

            outrec.Owner = newOwner;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float Area(OutPt op)
        {
            // https://en.wikipedia.org/wiki/Shoelace_formula
            float area = 0;
            OutPt op2 = op;
            do
            {
                area += (op2.Prev.Point.Y + op2.Point.Y) * (op2.Prev.Point.X - op2.Point.X);
                op2 = op2.Next;
            }
            while (op2 != op);
            return area * .5F;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float AreaTriangle(Vector2 pt1, Vector2 pt2, Vector2 pt3)
            => ((pt3.Y + pt1.Y) * (pt3.X - pt1.X))
            + ((pt1.Y + pt2.Y) * (pt1.X - pt2.X))
            + ((pt2.Y + pt3.Y) * (pt2.X - pt3.X));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static OutRec GetRealOutRec(OutRec outRec)
        {
            while ((outRec != null) && (outRec.Pts == null))
            {
                outRec = outRec.Owner;
            }

            return outRec;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void UncoupleOutRec(Active ae)
        {
            OutRec outrec = ae.Outrec;
            if (outrec == null)
            {
                return;
            }

            outrec.FrontEdge.Outrec = null;
            outrec.BackEdge.Outrec = null;
            outrec.FrontEdge = null;
            outrec.BackEdge = null;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool OutrecIsAscending(Active hotEdge)
            => hotEdge == hotEdge.Outrec.FrontEdge;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void SwapFrontBackSides(OutRec outrec)
        {
            // while this proc. is needed for open paths
            // it's almost never needed for closed paths
            (outrec.BackEdge, outrec.FrontEdge) = (outrec.FrontEdge, outrec.BackEdge);
            outrec.Pts = outrec.Pts.Next;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool EdgesAdjacentInAEL(IntersectNode inode)
            => (inode.Edge1.NextInAEL == inode.Edge2) || (inode.Edge1.PrevInAEL == inode.Edge2);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void CheckJoinLeft(Active e, Vector2 pt, bool checkCurrX = false)
        {
            Active prev = e.PrevInAEL;
            if (prev == null
                || IsOpen(e)
                || IsOpen(prev)
                || !IsHotEdge(e)
                || !IsHotEdge(prev))
            {
                return;
            }

            // Avoid trivial joins
            if ((pt.Y < e.Top.Y + 2 || pt.Y < prev.Top.Y + 2)
                && ((e.Bot.Y > pt.Y) || (prev.Bot.Y > pt.Y)))
            {
                return;
            }

            if (checkCurrX)
            {
                if (ClipperUtils.PerpendicDistFromLineSqrd(pt, prev.Bot, prev.Top) > 0.25)
                {
                    return;
                }
            }
            else if (e.CurX != prev.CurX)
            {
                return;
            }

            if (ClipperUtils.CrossProduct(e.Top, pt, prev.Top) != 0)
            {
                return;
            }

            if (e.Outrec.Idx == prev.Outrec.Idx)
            {
                this.AddLocalMaxPoly(prev, e, pt);
            }
            else if (e.Outrec.Idx < prev.Outrec.Idx)
            {
                JoinOutrecPaths(e, prev);
            }
            else
            {
                JoinOutrecPaths(prev, e);
            }

            prev.JoinWith = JoinWith.Right;
            e.JoinWith = JoinWith.Left;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void CheckJoinRight(Active e, Vector2 pt, bool checkCurrX = false)
        {
            Active next = e.NextInAEL;
            if (IsOpen(e)
                || !IsHotEdge(e)
                || IsJoined(e)
                || next == null
                || IsOpen(next)
                || !IsHotEdge(next))
            {
                return;
            }

            // Avoid trivial joins
            if ((pt.Y < e.Top.Y + 2 || pt.Y < next.Top.Y + 2)
                && ((e.Bot.Y > pt.Y) || (next.Bot.Y > pt.Y)))
            {
                return;
            }

            if (checkCurrX)
            {
                if (ClipperUtils.PerpendicDistFromLineSqrd(pt, next.Bot, next.Top) > 0.25)
                {
                    return;
                }
            }
            else if (e.CurX != next.CurX)
            {
                return;
            }

            if (ClipperUtils.CrossProduct(e.Top, pt, next.Top) != 0)
            {
                return;
            }

            if (e.Outrec.Idx == next.Outrec.Idx)
            {
                this.AddLocalMaxPoly(e, next, pt);
            }
            else if (e.Outrec.Idx < next.Outrec.Idx)
            {
                JoinOutrecPaths(e, next);
            }
            else
            {
                JoinOutrecPaths(next, e);
            }

            e.JoinWith = JoinWith.Right;
            next.JoinWith = JoinWith.Left;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void FixOutRecPts(OutRec outrec)
        {
            OutPt op = outrec.Pts;
            do
            {
                op.OutRec = outrec;
                op = op.Next;
            }
            while (op != outrec.Pts);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private OutPt AddLocalMaxPoly(Active ae1, Active ae2, Vector2 pt)
        {
            if (IsJoined(ae1))
            {
                this.Split(ae1, pt);
            }

            if (IsJoined(ae2))
            {
                this.Split(ae2, pt);
            }

            if (IsFront(ae1) == IsFront(ae2))
            {
                if (IsOpenEnd(ae1))
                {
                    SwapFrontBackSides(ae1.Outrec!);
                }
                else if (IsOpenEnd(ae2))
                {
                    SwapFrontBackSides(ae2.Outrec!);
                }
                else
                {
                    return null;
                }
            }

            OutPt result = AddOutPt(ae1, pt);
            if (ae1.Outrec == ae2.Outrec)
            {
                OutRec outrec = ae1.Outrec;
                outrec.Pts = result;
                UncoupleOutRec(ae1);
            }

            // and to preserve the winding orientation of outrec ...
            else if (IsOpen(ae1))
            {
                if (ae1.WindDx < 0)
                {
                    JoinOutrecPaths(ae1, ae2);
                }
                else
                {
                    JoinOutrecPaths(ae2, ae1);
                }
            }
            else if (ae1.Outrec.Idx < ae2.Outrec.Idx)
            {
                JoinOutrecPaths(ae1, ae2);
            }
            else
            {
                JoinOutrecPaths(ae2, ae1);
            }

            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsJoined(Active e)
            => e.JoinWith != JoinWith.None;

        private void Split(Active e, Vector2 currPt)
        {
            if (e.JoinWith == JoinWith.Right)
            {
                e.JoinWith = JoinWith.None;
                e.NextInAEL.JoinWith = JoinWith.None;
                this.AddLocalMinPoly(e, e.NextInAEL, currPt, true);
            }
            else
            {
                e.JoinWith = JoinWith.None;
                e.PrevInAEL.JoinWith = JoinWith.None;
                this.AddLocalMinPoly(e.PrevInAEL, e, currPt, true);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsFront(Active ae)
            => ae == ae.Outrec.FrontEdge;

        private struct LocMinSorter : IComparer<LocalMinima>
        {
            public readonly int Compare(LocalMinima locMin1, LocalMinima locMin2)
                => locMin2.Vertex.Point.Y.CompareTo(locMin1.Vertex.Point.Y);
        }

        private readonly struct LocalMinima
        {
            public readonly Vertex Vertex;
            public readonly ClippingType Polytype;
            public readonly bool IsOpen;

            public LocalMinima(Vertex vertex, ClippingType polytype, bool isOpen = false)
            {
                this.Vertex = vertex;
                this.Polytype = polytype;
                this.IsOpen = isOpen;
            }

            public static bool operator ==(LocalMinima lm1, LocalMinima lm2)

                // TODO: Check this. Why ref equals.
                => ReferenceEquals(lm1.Vertex, lm2.Vertex);

            public static bool operator !=(LocalMinima lm1, LocalMinima lm2)
                => !(lm1 == lm2);

            public override bool Equals(object obj)
                => obj is LocalMinima minima && this == minima;

            public override int GetHashCode()
                => this.Vertex.GetHashCode();
        }

        // IntersectNode: a structure representing 2 intersecting edges.
        // Intersections must be sorted so they are processed from the largest
        // Y coordinates to the smallest while keeping edges adjacent.
        private readonly struct IntersectNode
        {
            public readonly Vector2 Point;
            public readonly Active Edge1;
            public readonly Active Edge2;

            public IntersectNode(Vector2 pt, Active edge1, Active edge2)
            {
                this.Point = pt;
                this.Edge1 = edge1;
                this.Edge2 = edge2;
            }
        }

        private struct HorzSegSorter : IComparer<HorzSegment>
        {
            public readonly int Compare(HorzSegment hs1, HorzSegment hs2)
            {
                if (hs1 == null || hs2 == null)
                {
                    return 0;
                }

                if (hs1.RightOp == null)
                {
                    return hs2.RightOp == null ? 0 : 1;
                }
                else if (hs2.RightOp == null)
                {
                    return -1;
                }
                else
                {
                    return hs1.LeftOp.Point.X.CompareTo(hs2.LeftOp.Point.X);
                }
            }
        }

        private struct IntersectListSort : IComparer<IntersectNode>
        {
            public readonly int Compare(IntersectNode a, IntersectNode b)
            {
                if (a.Point.Y == b.Point.Y)
                {
                    if (a.Point.X == b.Point.X)
                    {
                        return 0;
                    }

                    return (a.Point.X < b.Point.X) ? -1 : 1;
                }

                return (a.Point.Y > b.Point.Y) ? -1 : 1;
            }
        }

        private class HorzSegment
        {
            public HorzSegment(OutPt op)
            {
                this.LeftOp = op;
                this.RightOp = null;
                this.LeftToRight = true;
            }

            public OutPt LeftOp { get; set; }

            public OutPt RightOp { get; set; }

            public bool LeftToRight { get; set; }
        }

        private class HorzJoin
        {
            public HorzJoin(OutPt ltor, OutPt rtol)
            {
                this.Op1 = ltor;
                this.Op2 = rtol;
            }

            public OutPt Op1 { get; }

            public OutPt Op2 { get; }
        }

        // OutPt: vertex data structure for clipping solutions
        private class OutPt
        {
            public OutPt(Vector2 pt, OutRec outrec)
            {
                this.Point = pt;
                this.OutRec = outrec;
                this.Next = this;
                this.Prev = this;
                this.HorizSegment = null;
            }

            public Vector2 Point { get; }

            public OutPt Next { get; set; }

            public OutPt Prev { get; set; }

            public OutRec OutRec { get; set; }

            public HorzSegment HorizSegment { get; set; }
        }

        // OutRec: path data structure for clipping solutions
        private class OutRec
        {
            public int Idx { get; set; }

            public OutRec Owner { get; set; }

            public Active FrontEdge { get; set; }

            public Active BackEdge { get; set; }

            public OutPt Pts { get; set; }

            public PolyPathF PolyPath { get; set; }

            public BoundsF Bounds { get; set; }

            public PathF Path { get; set; } = new PathF();

            public bool IsOpen { get; set; }

            public List<int> Splits { get; set; }
        }

        private class Vertex
        {
            public Vertex(Vector2 pt, VertexFlags flags, Vertex prev)
            {
                this.Point = pt;
                this.Flags = flags;
                this.Next = null;
                this.Prev = prev;
            }

            public Vector2 Point { get; }

            public Vertex Next { get; set; }

            public Vertex Prev { get; set; }

            public VertexFlags Flags { get; set; }
        }

        private class Active
        {
            public Vector2 Bot { get; set; }

            public Vector2 Top { get; set; }

            public float CurX { get; set; } // current (updated at every new scanline)

            public float Dx { get; set; }

            public int WindDx { get; set; } // 1 or -1 depending on winding direction

            public int WindCount { get; set; }

            public int WindCount2 { get; set; } // winding count of the opposite polytype

            public OutRec Outrec { get; set; }

            // AEL: 'active edge list' (Vatti's AET - active edge table)
            //     a linked list of all edges (from left to right) that are present
            //     (or 'active') within the current scanbeam (a horizontal 'beam' that
            //     sweeps from bottom to top over the paths in the clipping operation).
            public Active PrevInAEL { get; set; }

            public Active NextInAEL { get; set; }

            // SEL: 'sorted edge list' (Vatti's ST - sorted table)
            //     linked list used when sorting edges into their new positions at the
            //     top of scanbeams, but also (re)used to process horizontals.
            public Active PrevInSEL { get; set; }

            public Active NextInSEL { get; set; }

            public Active Jump { get; set; }

            public Vertex VertexTop { get; set; }

            public LocalMinima LocalMin { get; set; } // the bottom of an edge 'bound' (also Vatti)

            public bool IsLeftBound { get; set; }

            public JoinWith JoinWith { get; set; }
        }
    }

    internal class PolyPathF : IEnumerable<PolyPathF>
    {
        private readonly PolyPathF parent;
        private readonly List<PolyPathF> items = new();

        public PolyPathF(PolyPathF parent = null)
            => this.parent = parent;

        public PathF Polygon { get; private set; } // polytree root's polygon == null

        public int Level => this.GetLevel();

        public bool IsHole => this.GetIsHole();

        public int Count => this.items.Count;

        public PolyPathF this[int index] => this.items[index];

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public PolyPathF AddChild(PathF p)
        {
            PolyPathF child = new(this)
            {
                Polygon = p
            };

            this.items.Add(child);
            return child;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float Area()
        {
            float result = this.Polygon == null ? 0 : ClipperUtils.Area(this.Polygon);
            for (int i = 0; i < this.items.Count; i++)
            {
                PolyPathF child = this.items[i];
                result += child.Area();
            }

            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Clear() => this.items.Clear();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool GetIsHole()
        {
            int lvl = this.Level;
            return lvl != 0 && (lvl & 1) == 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int GetLevel()
        {
            int result = 0;
            PolyPathF pp = this.parent;
            while (pp != null)
            {
                ++result;
                pp = pp.parent;
            }

            return result;
        }

        public IEnumerator<PolyPathF> GetEnumerator() => this.items.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => this.items.GetEnumerator();
    }

    internal class PolyTreeF : PolyPathF
    {
    }
}
