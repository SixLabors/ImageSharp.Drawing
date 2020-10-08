// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Buffers;
using System.Runtime.CompilerServices;

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
            DebugGuard.MustBeLessThan(edgeIdx, this.count, nameof(edgeIdx));

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

        public void ScanOddEven(float y, Span<ScanEdge> edges, Span<float> emitSpan, ref int emitCounter)
        {
            DebugGuard.MustBeLessThanOrEqualTo(edges.Length, MaxEdges, "edges.Length");

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
                    Emit(x, edge.EmitV0, emitSpan, ref emitCounter);
                }
                else if (IsLeaving(flaggedIdx))
                {
                    Emit(x, edge.EmitV1, emitSpan, ref emitCounter);

                    offset++;

                    // Do not offset:
                    continue;
                }
                else
                {
                    // Emit once:
                    emitSpan[emitCounter++] = x;
                }

                // Do offset if not leaving:
                if (offset > 0)
                {
                    active[i - offset] = active[i];
                }
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
        private static int Strip(int flaggedIdx) => flaggedIdx & StripMask;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsEntering(int flaggedIdx) => (flaggedIdx & EnteringEdgeFlag) == EnteringEdgeFlag;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsLeaving(int flaggedIdx) => (flaggedIdx & LeavingEdgeFlag) == LeavingEdgeFlag;
    }
}