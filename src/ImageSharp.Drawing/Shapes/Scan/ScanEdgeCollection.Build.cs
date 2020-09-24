// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Buffers;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using SixLabors.ImageSharp.Memory;

namespace SixLabors.ImageSharp.Drawing.Shapes.Scan
{
    internal partial class ScanEdgeCollection
    {
        private enum EdgeCategory
        {
            Up = 0, // Non-horizontal
            Down, // Non-horizontal
            Left, // Horizontal
            Right, // Horizontal
        }

        // An pair of EdgeCategories defined as (fromEdge.Category, toEdge.Category)
        // Eliminates the nested switch-case and also good for perf,
        // since JIT should create a single jump table from a switch-case on this
        private enum VertexCategory
        {
            Up_Up = 0,
            Up_Down,
            Up_Left,
            Up_Right,

            Down_Up,
            Down_Down,
            Down_Left,
            Down_Right,

            Left_Up,
            Left_Down,
            Left_Left,
            Left_Right,

            Right_Up,
            Right_Down,
            Right_Left,
            Right_Right,
        }

        private ref struct RingWalker
        {
            private Span<ScanEdge> output;
            public int EdgeCounter;

            public EdgeData PreviousEdge;
            public EdgeData CurrentEdge;
            public EdgeData NextEdge;

            public RingWalker(Span<ScanEdge> output)
            {
                this.output = output;
                this.EdgeCounter = 0;
                this.PreviousEdge = default;
                this.CurrentEdge = default;
                this.NextEdge = default;
            }

            public void Move(bool emitPreviousEdge)
            {
                VertexCategory startVertexCategory =
                    CreateVertexCategory(this.PreviousEdge.EdgeCategory, this.CurrentEdge.EdgeCategory);
                VertexCategory endVertexCategory =
                    CreateVertexCategory(this.CurrentEdge.EdgeCategory, this.NextEdge.EdgeCategory);

                EdgeData.ApplyVertexCategory(startVertexCategory, ref this.PreviousEdge, ref this.CurrentEdge);
                EdgeData.ApplyVertexCategory(endVertexCategory, ref this.CurrentEdge, ref this.NextEdge);

                if (emitPreviousEdge)
                {
                    this.PreviousEdge.EmitScanEdge(this.output, ref this.EdgeCounter);
                }

                this.PreviousEdge = this.CurrentEdge;
                this.CurrentEdge = this.NextEdge;
            }
        }

        private struct EdgeData
        {
            public PointF Start;
            public PointF End;
            public EdgeCategory EdgeCategory;
            public int EmitStart;
            public int EmitEnd;

            public EdgeData(PointF start, PointF end, in TolerantComparer comparer)
            {
                this.Start = start;
                this.End = end;
                if (comparer.AreEqual(this.Start.Y, this.End.Y))
                {
                    this.EdgeCategory = this.Start.X < this.End.X ? EdgeCategory.Right : EdgeCategory.Left;
                }
                else
                {
                    this.EdgeCategory = this.Start.Y < this.End.Y ? EdgeCategory.Down : EdgeCategory.Up;
                }

                this.EmitStart = 0;
                this.EmitEnd = 0;
            }

            public void EmitScanEdge(Span<ScanEdge> edges, ref int edgeCounter)
            {
                if (this.EdgeCategory == EdgeCategory.Left || this.EdgeCategory == EdgeCategory.Right)
                {
                    return;
                }

                edges[edgeCounter++] = this.ToScanEdge();
            }

            public static void ApplyVertexCategory(
                VertexCategory vertexCategory,
                ref EdgeData fromEdge,
                ref EdgeData toEdge)
            {
                switch (vertexCategory)
                {
                    case VertexCategory.Up_Up:
                        // 0, 1
                        toEdge.EmitStart = 1;
                        break;
                    case VertexCategory.Up_Down:
                        // 0, 0
                        break;
                    case VertexCategory.Up_Left:
                        // 2, 0
                        fromEdge.EmitEnd = 2;
                        break;
                    case VertexCategory.Up_Right:
                        // 1, 0
                        fromEdge.EmitEnd = 1;
                        break;
                    case VertexCategory.Down_Up:
                        // 0, 0
                        break;
                    case VertexCategory.Down_Down:
                        // 0, 1
                        toEdge.EmitStart = 1;
                        break;
                    case VertexCategory.Down_Left:
                        // 1, 0
                        fromEdge.EmitEnd = 1;
                        break;
                    case VertexCategory.Down_Right:
                        // 2, 0
                        fromEdge.EmitEnd = 2;
                        break;
                    case VertexCategory.Left_Up:
                        // 0, 1
                        toEdge.EmitStart = 1;
                        break;
                    case VertexCategory.Left_Down:
                        // 0, 2
                        toEdge.EmitStart = 2;
                        break;
                    case VertexCategory.Left_Left:
                        // INVALID
                        ThrowInvalidRing("Invalid ring: repeated horizontal edges (<- <-)");
                        break;
                    case VertexCategory.Left_Right:
                        // INVALID
                        ThrowInvalidRing("Invalid ring: repeated horizontal edges (<- ->)");
                        break;
                    case VertexCategory.Right_Up:
                        // 0, 2
                        toEdge.EmitStart = 2;
                        break;
                    case VertexCategory.Right_Down:
                        // 0, 1
                        toEdge.EmitStart = 1;
                        break;
                    case VertexCategory.Right_Left:
                        // INVALID
                        ThrowInvalidRing("Invalid ring: repeated horizontal edges (-> <-)");
                        break;
                    case VertexCategory.Right_Right:
                        // INVALID
                        ThrowInvalidRing("Invalid ring: repeated horizontal edges (-> ->)");
                        break;
                }
            }

            private ScanEdge ToScanEdge()
            {
                int up = this.EdgeCategory == EdgeCategory.Up ? 1 : 0;
                if (up == 1)
                {
                    Swap(ref this.Start, ref this.End);
                    Swap(ref this.EmitStart, ref this.EmitEnd);
                }

                int flags = up | (this.EmitStart << 1) | (this.EmitEnd << 3);
                return new ScanEdge(ref this.Start, ref this.End, flags);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private static void Swap<T>(ref T left, ref T right)
            {
                T tmp = left;
                left = right;
                right = tmp;
            }
        }

        private static ScanEdgeCollection Create(TessellatedMultipolygon multipolygon, MemoryAllocator allocator, in TolerantComparer comparer)
        {
            // Overallocate, since we don't know how many horizontal edges do we have:
            IMemoryOwner<ScanEdge> buffer = allocator.Allocate<ScanEdge>(multipolygon.TotalVertexCount);

            RingWalker walker = new RingWalker(buffer.Memory.Span);

            foreach (TessellatedMultipolygon.Ring ring in multipolygon)
            {
                if (ring.VertexCount < 3)
                {
                    ThrowInvalidRing("ScanEdgeCollection.Create Encountered a ring with VertexCount < 3!");
                }

                var vertices = ring.Vertices;

                walker.PreviousEdge = new EdgeData(vertices[vertices.Length - 2], vertices[vertices.Length - 1], comparer); // Last edge
                walker.CurrentEdge = new EdgeData(vertices[0], vertices[1], comparer); // First edge
                walker.NextEdge = new EdgeData(vertices[1], vertices[2], comparer); // Second edge
                walker.Move(false);

                for (int i = 1; i < vertices.Length - 2; i++)
                {
                    walker.NextEdge = new EdgeData(vertices[i + 1], vertices[i + 2], comparer);
                    walker.Move(true);
                }

                walker.NextEdge = new EdgeData(vertices[0], vertices[1], comparer); // First edge
                walker.Move(true); // Emit edge before last edge

                walker.NextEdge = new EdgeData(vertices[1], vertices[2], comparer);
                walker.Move(true); // Emit last edge
            }

            return new ScanEdgeCollection(buffer, walker.EdgeCounter);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static VertexCategory CreateVertexCategory(EdgeCategory previousCategory, EdgeCategory currentCategory)
        {
            var value = (VertexCategory)(((int)previousCategory << 2) | (int)currentCategory);
            VerifyVertexCategory(value);
            return value;
        }

        [Conditional("DEBUG")]
        private static void VerifyVertexCategory(VertexCategory vertexCategory)
        {
            int value = (int) vertexCategory;
            if (value < 0 || value >= 16)
            {
                throw new Exception("EdgeCategoryPair value shall be: 0 <= value < 16");
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ThrowInvalidRing(string message)
        {
            throw new InvalidOperationException(message);
        }
    }
}