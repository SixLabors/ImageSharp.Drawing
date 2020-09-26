// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Buffers;

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

        private int count;
        public readonly Span<int> Buffer;
        private Span<ScanEdge> allEdges;

        public ActiveEdgeList(Span<int> buffer, Span<ScanEdge> allEdges)
        {
            DebugGuard.MustBeLessThanOrEqualTo(allEdges.Length, MaxEdges, "allEdges.Length");
            this.count = 0;
            this.Buffer = buffer;
            this.allEdges = allEdges;
        }

        public void Append(int edgeIdx)
        {
            this.Buffer[this.count] = edgeIdx | EnteringEdgeFlag;
        }
    }
}