// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Buffers;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.Arm;
using System.Runtime.Intrinsics.X86;
using SixLabors.ImageSharp.Memory;

namespace SixLabors.ImageSharp.Drawing.Shapes.Rasterization;

internal partial class ScanEdgeCollection
{
    private enum EdgeCategory
    {
        Up = 0, // Non-horizontal
        Down, // Non-horizontal
        Left, // Horizontal
        Right, // Horizontal
    }

    // A pair of EdgeCategories at a given vertex, defined as (fromEdge.EdgeCategory, toEdge.EdgeCategory)
    private enum VertexCategory
    {
        UpUp = 0,
        UpDown,
        UpLeft,
        UpRight,

        DownUp,
        DownDown,
        DownLeft,
        DownRight,

        LeftUp,
        LeftDown,
        LeftLeft,
        LeftRight,

        RightUp,
        RightDown,
        RightLeft,
        RightRight,
    }

    internal static ScanEdgeCollection Create(TessellatedMultipolygon multiPolygon, MemoryAllocator allocator, int subsampling)
    {
        // We allocate more than we need, since we don't know how many horizontal edges do we have:
        IMemoryOwner<ScanEdge> buffer = allocator.Allocate<ScanEdge>(multiPolygon.TotalVertexCount);

        RingWalker walker = new(buffer.Memory.Span);

        using IMemoryOwner<float> roundedYBuffer = allocator.Allocate<float>(multiPolygon.Max(r => r.Vertices.Length));
        Span<float> roundedY = roundedYBuffer.Memory.Span;

        foreach (TessellatedMultipolygon.Ring ring in multiPolygon)
        {
            if (ring.VertexCount < 3)
            {
                continue;
            }

            ReadOnlySpan<PointF> vertices = ring.Vertices;
            RoundY(vertices, roundedY, subsampling);

            walker.PreviousEdge = new EdgeData(vertices, roundedY, vertices.Length - 2); // Last edge
            walker.CurrentEdge = new EdgeData(vertices, roundedY, 0); // First edge
            walker.NextEdge = new EdgeData(vertices, roundedY, 1); // Second edge
            walker.Move(false);

            for (int i = 1; i < vertices.Length - 2; i++)
            {
                walker.NextEdge = new EdgeData(vertices, roundedY, i + 1);
                walker.Move(true);
            }

            walker.NextEdge = new EdgeData(vertices, roundedY, 0); // First edge
            walker.Move(true); // Emit edge before last edge

            walker.NextEdge = new EdgeData(vertices, roundedY, 1); // Second edge
            walker.Move(true); // Emit last edge
        }

        static void RoundY(ReadOnlySpan<PointF> vertices, Span<float> destination, float subsamplingRatio)
        {
            int ri = 0;
            if (Avx.IsSupported)
            {
                // If the length of the input buffer as a float array is a multiple of 16, we can use AVX instructions:
                int verticesLengthInFloats = vertices.Length * 2;
                int vector256FloatCount_x2 = Vector256<float>.Count * 2;
                int remainder = verticesLengthInFloats % vector256FloatCount_x2;
                int verticesLength = verticesLengthInFloats - remainder;

                if (verticesLength > 0)
                {
                    ri = vertices.Length - (remainder / 2);
                    nint maxIterations = verticesLength / (Vector256<float>.Count * 2);
                    ref Vector256<float> sourceBase = ref Unsafe.As<PointF, Vector256<float>>(ref MemoryMarshal.GetReference(vertices));
                    ref Vector256<float> destinationBase = ref Unsafe.As<float, Vector256<float>>(ref MemoryMarshal.GetReference(destination));

                    Vector256<float> ssRatio = Vector256.Create(subsamplingRatio);
                    Vector256<float> inverseSsRatio = Vector256.Create(1F / subsamplingRatio);
                    Vector256<float> half = Vector256.Create(.5F);

                    // For every 1 vector we add to the destination we read 2 from the vertices.
                    for (nint i = 0, j = 0; i < maxIterations; i++, j += 2)
                    {
                        // Load 8 PointF
                        Vector256<float> points1 = Unsafe.Add(ref sourceBase, j);
                        Vector256<float> points2 = Unsafe.Add(ref sourceBase, j + 1);

                        // Shuffle the points to group the Y properties
                        Vector128<float> points1Y = Sse.Shuffle(points1.GetLower(), points1.GetUpper(), 0b11_01_11_01);
                        Vector128<float> points2Y = Sse.Shuffle(points2.GetLower(), points2.GetUpper(), 0b11_01_11_01);
                        Vector256<float> pointsY = Vector256.Create(points1Y, points2Y);

                        // Multiply by the subsampling ratio, round, then multiply by the inverted subsampling ratio and assign.
                        // https://www.ocf.berkeley.edu/~horie/rounding.html
                        Vector256<float> rounded = Avx.RoundToPositiveInfinity(Avx.Subtract(Avx.Multiply(pointsY, ssRatio), half));
                        Unsafe.Add(ref destinationBase, i) = Avx.Multiply(rounded, inverseSsRatio);
                    }
                }
            }
            else if (Sse41.IsSupported)
            {
                // If the length of the input buffer as a float array is a multiple of 8, we can use Sse instructions:
                int verticesLengthInFloats = vertices.Length * 2;
                int vector128FloatCount_x2 = Vector128<float>.Count * 2;
                int remainder = verticesLengthInFloats % vector128FloatCount_x2;
                int verticesLength = verticesLengthInFloats - remainder;

                if (verticesLength > 0)
                {
                    ri = vertices.Length - (remainder / 2);
                    nint maxIterations = verticesLength / (Vector128<float>.Count * 2);
                    ref Vector128<float> sourceBase = ref Unsafe.As<PointF, Vector128<float>>(ref MemoryMarshal.GetReference(vertices));
                    ref Vector128<float> destinationBase = ref Unsafe.As<float, Vector128<float>>(ref MemoryMarshal.GetReference(destination));

                    Vector128<float> ssRatio = Vector128.Create(subsamplingRatio);
                    Vector128<float> inverseSsRatio = Vector128.Create(1F / subsamplingRatio);
                    Vector128<float> half = Vector128.Create(.5F);

                    // For every 1 vector we add to the destination we read 2 from the vertices.
                    for (nint i = 0, j = 0; i < maxIterations; i++, j += 2)
                    {
                        // Load 4 PointF
                        Vector128<float> points1 = Unsafe.Add(ref sourceBase, j);
                        Vector128<float> points2 = Unsafe.Add(ref sourceBase, j + 1);

                        // Shuffle the points to group the Y properties
                        Vector128<float> pointsY = Sse.Shuffle(points1, points2, 0b11_01_11_01);

                        // Multiply by the subsampling ratio, round, then multiply by the inverted subsampling ratio and assign.
                        // https://www.ocf.berkeley.edu/~horie/rounding.html
                        Vector128<float> rounded = Sse41.RoundToPositiveInfinity(Sse.Subtract(Sse.Multiply(pointsY, ssRatio), half));
                        Unsafe.Add(ref destinationBase, i) = Sse.Multiply(rounded, inverseSsRatio);
                    }
                }
            }
            else if (AdvSimd.IsSupported)
            {
                // If the length of the input buffer as a float array is a multiple of 8, we can use AdvSimd instructions:
                int verticesLengthInFloats = vertices.Length * 2;
                int vector128FloatCount_x2 = Vector128<float>.Count * 2;
                int remainder = verticesLengthInFloats % vector128FloatCount_x2;
                int verticesLength = verticesLengthInFloats - remainder;

                if (verticesLength > 0)
                {
                    ri = vertices.Length - (remainder / 2);
                    nint maxIterations = verticesLength / (Vector128<float>.Count * 2);
                    ref Vector128<float> sourceBase = ref Unsafe.As<PointF, Vector128<float>>(ref MemoryMarshal.GetReference(vertices));
                    ref Vector128<float> destinationBase = ref Unsafe.As<float, Vector128<float>>(ref MemoryMarshal.GetReference(destination));

                    Vector128<float> ssRatio = Vector128.Create(subsamplingRatio);
                    Vector128<float> inverseSsRatio = Vector128.Create(1F / subsamplingRatio);

                    // For every 1 vector we add to the destination we read 2 from the vertices.
                    for (nint i = 0, j = 0; i < maxIterations; i++, j += 2)
                    {
                        // Load 4 PointF
                        Vector128<float> points1 = Unsafe.Add(ref sourceBase, j);
                        Vector128<float> points2 = Unsafe.Add(ref sourceBase, j + 1);

                        // Shuffle the points to group the Y
                        Vector128<float> pointsY = AdvSimdShuffle(points1, points2, 0b11_01_11_01);

                        // Multiply by the subsampling ratio, round, then multiply by the inverted subsampling ratio and assign.
                        Vector128<float> rounded = AdvSimd.RoundAwayFromZero(AdvSimd.Multiply(pointsY, ssRatio));
                        Unsafe.Add(ref destinationBase, i) = AdvSimd.Multiply(rounded, inverseSsRatio);
                    }
                }
            }

            for (; ri < vertices.Length; ri++)
            {
                destination[ri] = MathF.Round(vertices[ri].Y * subsamplingRatio, MidpointRounding.AwayFromZero) / subsamplingRatio;
            }
        }

        return new ScanEdgeCollection(buffer, walker.EdgeCounter);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<float> AdvSimdShuffle(Vector128<float> a, Vector128<float> b, byte control)
    {
        Vector128<float> result = Vector128.Create(AdvSimd.Extract(a, (byte)(control & 0x3)));
        result = AdvSimd.Insert(result, 1, AdvSimd.Extract(a, (byte)((control >> 2) & 0x3)));
        result = AdvSimd.Insert(result, 2, AdvSimd.Extract(b, (byte)((control >> 4) & 0x3)));
        result = AdvSimd.Insert(result, 3, AdvSimd.Extract(b, (byte)((control >> 6) & 0x3)));

        return result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static VertexCategory CreateVertexCategory(EdgeCategory previousCategory, EdgeCategory currentCategory)
    {
        VertexCategory value = (VertexCategory)(((int)previousCategory << 2) | (int)currentCategory);
        VerifyVertexCategory(value);
        return value;
    }

    [Conditional("DEBUG")]
    private static void VerifyVertexCategory(VertexCategory vertexCategory)
    {
        int value = (int)vertexCategory;
        if (value is < 0 or >= 16)
        {
            throw new ArgumentOutOfRangeException(nameof(vertexCategory), "EdgeCategoryPair value shall be: 0 <= value < 16");
        }
    }

    private struct EdgeData
    {
        public EdgeCategory EdgeCategory;

        private PointF start;
        private PointF end;
        private int emitStart;
        private int emitEnd;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public EdgeData(ReadOnlySpan<PointF> vertices, ReadOnlySpan<float> roundedY, int idx)
            : this(
                vertices[idx].X,
                vertices[idx + 1].X,
                roundedY[idx],
                roundedY[idx + 1])
        {
        }

        public EdgeData(float startX, float endX, float startYRounded, float endYRounded)
        {
            this.start = new PointF(startX, startYRounded);
            this.end = new PointF(endX, endYRounded);

            if (this.start.Y == this.end.Y)
            {
                this.EdgeCategory = this.start.X < this.end.X ? EdgeCategory.Right : EdgeCategory.Left;
            }
            else
            {
                this.EdgeCategory = this.start.Y < this.end.Y ? EdgeCategory.Down : EdgeCategory.Up;
            }

            this.emitStart = 0;
            this.emitEnd = 0;
        }

        public void EmitScanEdge(Span<ScanEdge> edges, ref int edgeCounter)
        {
            if (this.EdgeCategory is EdgeCategory.Left or EdgeCategory.Right)
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
            // On PolygonScanner needs to handle intersections at edge connections (vertices) in a special way:
            // - We need to make sure we do not report ("emit") an intersection point more times than necessary because we detected the intersection at both edges.
            // - We need to make sure we we emit proper intersection points when scanning through a horizontal line
            // In practice this means that vertex intersections have to emitted: 0-2 times in total:
            // - Do not emit on vertex of collinear edges
            // - Emit 2 times if:
            //    - One of the edges is horizontal
            //    - The corner is concave
            //      (The reason for tis rule is that we do not scan horizontal edges)
            // - Emit once otherwise
            // Since PolygonScanner does not process vertices, only edges, we need to define arbitrary rules
            // about WHERE (on which edge) do we emit the vertex intersections.
            // For visualization of the rules see:
            //     PoygonScanning.MD
            // For an example, see:
            //     ImageSharp.Drawing.Tests/Shapes/Scan/SimplePolygon_AllEmitCases.png
            switch (vertexCategory)
            {
                case VertexCategory.UpUp:
                    // 0, 1
                    toEdge.emitStart = 1;
                    break;
                case VertexCategory.UpDown:
                    // 1, 1
                    toEdge.emitStart = 1;
                    fromEdge.emitEnd = 1;
                    break;
                case VertexCategory.UpLeft:
                    // 2, 0
                    fromEdge.emitEnd = 2;
                    break;
                case VertexCategory.UpRight:
                    // 1, 0
                    fromEdge.emitEnd = 1;
                    break;
                case VertexCategory.DownUp:
                    // 1, 1
                    toEdge.emitStart = 1;
                    fromEdge.emitEnd = 1;
                    break;
                case VertexCategory.DownDown:
                    // 0, 1
                    toEdge.emitStart = 1;
                    break;
                case VertexCategory.DownLeft:
                    // 1, 0
                    fromEdge.emitEnd = 1;
                    break;
                case VertexCategory.DownRight:
                    // 2, 0
                    fromEdge.emitEnd = 2;
                    break;
                case VertexCategory.LeftUp:
                    // 0, 1
                    toEdge.emitStart = 1;
                    break;
                case VertexCategory.LeftDown:
                    // 0, 2
                    toEdge.emitStart = 2;
                    break;
                case VertexCategory.LeftLeft:
                    // 0, 0 - collinear
                    break;
                case VertexCategory.LeftRight:
                    // 0, 0 - collinear
                    break;
                case VertexCategory.RightUp:
                    // 0, 2
                    toEdge.emitStart = 2;
                    break;
                case VertexCategory.RightDown:
                    // 0, 1
                    toEdge.emitStart = 1;
                    break;
                case VertexCategory.RightLeft:
                    // 0, 0 - collinear
                    break;
                case VertexCategory.RightRight:
                    // 0, 0 - collinear
                    break;
            }
        }

        private ScanEdge ToScanEdge()
        {
            int up = this.EdgeCategory == EdgeCategory.Up ? 1 : 0;
            if (up == 1)
            {
                Swap(ref this.start, ref this.end);
                Swap(ref this.emitStart, ref this.emitEnd);
            }

            int flags = up | (this.emitStart << 1) | (this.emitEnd << 3);
            return new ScanEdge(this.start, this.end, flags);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void Swap<T>(ref T left, ref T right)
        {
            T tmp = left;
            left = right;
            right = tmp;
        }
    }

    private ref struct RingWalker
    {
        private readonly Span<ScanEdge> output;
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
}
