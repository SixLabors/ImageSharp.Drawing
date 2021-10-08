// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Runtime.CompilerServices;
using SixLabors.ImageSharp.Drawing.Utilities;

namespace SixLabors.ImageSharp.Drawing.Shapes.Rasterization
{
    internal enum NonZeroIntersectionType
    {
        Down,
        Up,
        Corner,
        CornerDummy
    }

    /// <summary>
    /// The list of active edges as an index buffer into <see cref="ScanEdgeCollection.Edges"/>.
    /// </summary>
    internal ref struct ActiveEdgeList
    {
        private const int EnteringEdgeFlag = 1 << 30;
        private const int LeavingEdgeFlag = 1 << 31;
        private const int MaxEdges = EnteringEdgeFlag - 1;

        private const int StripMask = ~(EnteringEdgeFlag | LeavingEdgeFlag);

        private const float NonzeroSortingHelperEpsilon = 1e-4f;

        private int count;
        internal readonly Span<int> Buffer;

        public ActiveEdgeList(Span<int> buffer)
        {
            this.count = 0;
            this.Buffer = buffer;
        }

        private Span<int> ActiveEdges => this.Buffer.Slice(0, this.count);

        public void EnterEdge(int edgeIdx) => this.Buffer[this.count++] = edgeIdx | EnteringEdgeFlag;

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

        public void RemoveLeavingEdges()
        {
            int offset = 0;

            Span<int> active = this.ActiveEdges;

            for (int i = 0; i < active.Length; i++)
            {
                int flaggedIdx = active[i];
                int edgeIdx = Strip(flaggedIdx);
                if (IsLeaving(flaggedIdx))
                {
                    offset++;
                }
                else
                {
                    // Unmask and offset:
                    active[i - offset] = edgeIdx;
                }
            }

            this.count -= offset;
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

        public Span<float> ScanNonZero(
            float y,
            Span<ScanEdge> edges,
            Span<float> intersections,
            Span<NonZeroIntersectionType> intersectionTypes)
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
                    EmitNonZero(x, edge.EmitV0, edgeUp, intersections, intersectionTypes, ref intersectionCounter);
                }
                else if (IsLeaving(flaggedIdx))
                {
                    EmitNonZero(x, edge.EmitV1, edgeUp, intersections, intersectionTypes, ref intersectionCounter);

                    offset++;

                    // Do not offset:
                    continue;
                }
                else
                {
                    // Emit once:
                    if (edgeUp)
                    {
                        intersectionTypes[intersectionCounter] = NonZeroIntersectionType.Up;
                        intersections[intersectionCounter++] = x + NonzeroSortingHelperEpsilon;
                    }
                    else
                    {
                        intersectionTypes[intersectionCounter] = NonZeroIntersectionType.Down;
                        intersections[intersectionCounter++] = x - NonzeroSortingHelperEpsilon;
                    }
                }

                // Unmask and offset:
                active[i - offset] = edgeIdx;
            }

            this.count -= offset;

            intersections = intersections.Slice(0, intersectionCounter);
            intersectionTypes = intersectionTypes.Slice(0, intersectionCounter);
            SortUtility.Sort(intersections, intersectionTypes);

            return ApplyNonzeroRule(intersections, intersectionTypes);
        }

        private static Span<float> ApplyNonzeroRule(Span<float> intersections, Span<NonZeroIntersectionType> intersectionTypes)
        {
            int offset = 0;
            int tracker = 0;

            for (int i = 0; i < intersectionTypes.Length; i++)
            {
                NonZeroIntersectionType type = intersectionTypes[i];
                if (type == NonZeroIntersectionType.CornerDummy)
                {
                    // we skip this one so we can emit twice on actual "Corner"
                    offset++;
                }
                else if (type == NonZeroIntersectionType.Corner)
                {
                    // Assume a Down, Up serie
                    NonzeroEmitIfNeeded(intersections, i, -1, intersections[i], ref tracker, ref offset);
                    offset -= 1;
                    NonzeroEmitIfNeeded(intersections, i, 1, intersections[i], ref tracker, ref offset);
                }
                else
                {
                    int diff = type == NonZeroIntersectionType.Up ? 1 : -1;
                    float emitVal = intersections[i] + (NonzeroSortingHelperEpsilon * diff * -1);
                    NonzeroEmitIfNeeded(intersections, i, diff, emitVal, ref tracker, ref offset);
                }
            }

            return intersections.Slice(0, intersections.Length - offset);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void NonzeroEmitIfNeeded(Span<float> intersections, int i, int diff, float emitVal, ref int tracker, ref int offset)
        {
            bool emit = (tracker == 0 && diff != 0) || tracker * diff == -1;
            tracker += diff;

            if (emit)
            {
                intersections[i - offset] = emitVal;
            }
            else
            {
                offset++;
            }
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
        private static void EmitNonZero(float x, int times, bool edgeUp, Span<float> emitSpan, Span<NonZeroIntersectionType> intersectionTypes, ref int emitCounter)
        {
            if (times == 2)
            {
                intersectionTypes[emitCounter] = NonZeroIntersectionType.CornerDummy;
                emitSpan[emitCounter++] = x - NonzeroSortingHelperEpsilon; // To make sure the "dummy" point precedes the actual one

                intersectionTypes[emitCounter] = NonZeroIntersectionType.Corner;
                emitSpan[emitCounter++] = x;
            }
            else if (times == 1)
            {
                if (edgeUp)
                {
                    intersectionTypes[emitCounter] = NonZeroIntersectionType.Up;
                    emitSpan[emitCounter++] = x + NonzeroSortingHelperEpsilon;
                }
                else
                {
                    intersectionTypes[emitCounter] = NonZeroIntersectionType.Down;
                    emitSpan[emitCounter++] = x - NonzeroSortingHelperEpsilon;
                }
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
