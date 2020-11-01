// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Buffers;
using System.Runtime.CompilerServices;
using SixLabors.ImageSharp.Drawing.Utilities;

namespace SixLabors.ImageSharp.Drawing.Shapes.Scan
{
    /// <summary>
    /// The list of active edges as index buffer to <see cref="ScanEdge"/>-s.
    /// </summary>
    internal ref struct ActiveEdgeList
    {
        private const int EnteringEdgeFlag = 1 << 30;
        private const int LeavingEdgeFlag = 1 << 31;
        private const int MaxEdges = EnteringEdgeFlag - 1;

        private const int StripMask = ~(EnteringEdgeFlag | LeavingEdgeFlag);

        private int count;
        internal readonly Span<int> Buffer;

        public ActiveEdgeList(Span<int> buffer)
        {
            this.count = 0;
            this.Buffer = buffer;
        }

        private Span<int> ActiveEdges => this.Buffer.Slice(0, this.count);

        public void EnterEdge(int edgeIdx)
        {
            this.Buffer[this.count++] = edgeIdx | EnteringEdgeFlag;
        }

        public void LeaveEdge(int edgeIdx)
        {
            Span<int> active = this.ActiveEdges;
            for (int i = 0; i < active.Length; i++)
            {
                if (active[i] == edgeIdx)
                {
                    active[i] |= LeavingEdgeFlag;
                    return;
                }
            }

            throw new ArgumentOutOfRangeException(nameof(edgeIdx));
        }

        public Span<float> ScanOddEven(float y, Span<ScanEdge> edges, Span<float> intersections)
        {
            DebugGuard.MustBeLessThanOrEqualTo(edges.Length, MaxEdges, "edges.Length");

            int intersectionCounter = 0;
            int offset = 0;

            Span<int> active = this.ActiveEdges;

            for (int i = 0; i < active.Length; i++)
            {
                int flaggedIdx = active[i];
                int edgeIdx = Strip(flaggedIdx);
                ref ScanEdge edge = ref edges[edgeIdx];
                float x = edge.GetX(y);
                if (IsEntering(flaggedIdx))
                {
                    Emit(x, edge.EmitV0, intersections, ref intersectionCounter);
                }
                else if (IsLeaving(flaggedIdx))
                {
                    Emit(x, edge.EmitV1, intersections, ref intersectionCounter);

                    offset++;

                    // Do not offset:
                    continue;
                }
                else
                {
                    // Emit once:
                    intersections[intersectionCounter++] = x;
                }

                // Unmask and offset:
                active[i - offset] = edgeIdx;
            }

            this.count -= offset;

            intersections = intersections.Slice(0, intersectionCounter);
            SortUtility.Sort(intersections);
            return intersections;
        }

        public Span<float> ScanNonZero(float y, Span<ScanEdge> edges, Span<float> intersections, Span<bool> edgeUpAtIntersections)
        {
            DebugGuard.MustBeLessThanOrEqualTo(edges.Length, MaxEdges, "edges.Length");

            int intersectionCounter = 0;
            int offset = 0;

            Span<int> active = this.ActiveEdges;

            for (int i = 0; i < active.Length; i++)
            {
                int flaggedIdx = active[i];
                int edgeIdx = Strip(flaggedIdx);
                ref ScanEdge edge = ref edges[edgeIdx];
                bool edgeUp = edge.EdgeUp;
                float x = edge.GetX(y);
                if (IsEntering(flaggedIdx))
                {
                    Emit(x, edge.EmitV0, edgeUp, intersections, edgeUpAtIntersections, ref intersectionCounter);
                }
                else if (IsLeaving(flaggedIdx))
                {
                    Emit(x, edge.EmitV1, edgeUp, intersections, edgeUpAtIntersections, ref intersectionCounter);

                    offset++;

                    // Do not offset:
                    continue;
                }
                else
                {
                    // Emit once:
                    edgeUpAtIntersections[intersectionCounter] = edgeUp;
                    intersections[intersectionCounter++] = x;
                }

                // Unmask and offset:
                active[i - offset] = edgeIdx;
            }

            this.count -= offset;

            intersections = intersections.Slice(0, intersectionCounter);
            edgeUpAtIntersections = edgeUpAtIntersections.Slice(0, intersectionCounter);
            SortUtility.Sort(intersections, edgeUpAtIntersections);

            // Apply nonzero intersection rule:
            offset = 0;
            int tracker = 0;

            for (int i = 0; i < edgeUpAtIntersections.Length; i++)
            {
                int diff = edgeUpAtIntersections[i] ? 1 : -1;
                bool emit = (tracker == 0 && diff > 0) || (tracker == 1 && diff < 0);
                tracker += diff;

                if (emit)
                {
                    intersections[i - offset] = intersections[i];
                }
                else
                {
                    offset++;
                }
            }

            return intersections.Slice(0, intersections.Length - offset);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void Emit(float x, int times, Span<float> emitSpan, ref int emitCounter)
        {
            if (times > 1)
            {
                emitSpan[emitCounter++] = x;
            }

            if (times > 0)
            {
                emitSpan[emitCounter++] = x;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void Emit(float x, int times, bool edgeUp, Span<float> emitSpan, Span<bool> edgeUpSpan, ref int emitCounter)
        {
            if (times > 1)
            {
                edgeUpSpan[emitCounter] = edgeUp;
                emitSpan[emitCounter++] = x;
            }

            if (times > 0)
            {
                edgeUpSpan[emitCounter] = edgeUp;
                emitSpan[emitCounter++] = x;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int Strip(int flaggedIdx) => flaggedIdx & StripMask;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsEntering(int flaggedIdx) => (flaggedIdx & EnteringEdgeFlag) == EnteringEdgeFlag;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsLeaving(int flaggedIdx) => (flaggedIdx & LeavingEdgeFlag) == LeavingEdgeFlag;
    }
}