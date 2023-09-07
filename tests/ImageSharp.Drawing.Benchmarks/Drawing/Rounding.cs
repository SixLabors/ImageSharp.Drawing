// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using BenchmarkDotNet.Attributes;

namespace SixLabors.ImageSharp.Drawing.Benchmarks.Drawing;
public class Rounding
{
    private PointF[] vertices;
    private float[] destination;
    private float[] destinationAvx;

    [GlobalSetup]
    public void Setup()
    {
        this.vertices = new PointF[1000];
        this.destination = new float[this.vertices.Length];
        this.destinationAvx = new float[this.vertices.Length];
        Random r = new(42);
        for (int i = 0; i < this.vertices.Length; i++)
        {
            this.vertices[i] = new PointF((float)r.NextDouble(), (float)r.NextDouble());
        }
    }

    [Benchmark]
    public void RoundYAvx() => RoundYAvx(this.vertices, this.destinationAvx, 16);

    [Benchmark(Baseline = true)]
    public void RoundY() => RoundY(this.vertices, this.destination, 16);

    private static void RoundYAvx(ReadOnlySpan<PointF> vertices, Span<float> destination, float subsamplingRatio)
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
                float maxIterations = verticesLength / (Vector256<float>.Count * 2);
                ref Vector256<float> sourceBase = ref Unsafe.As<PointF, Vector256<float>>(ref MemoryMarshal.GetReference(vertices));
                ref Vector256<float> destinationBase = ref Unsafe.As<float, Vector256<float>>(ref MemoryMarshal.GetReference(destination));

                Vector256<float> ssRatio = Vector256.Create(subsamplingRatio);
                Vector256<float> inverseSsRatio = Vector256.Create(1F / subsamplingRatio);

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
                    Vector256<float> rounded = Avx.RoundToPositiveInfinity(Avx.Multiply(pointsY, ssRatio));
                    Unsafe.Add(ref destinationBase, i) = Avx.Multiply(rounded, inverseSsRatio);
                }
            }
        }

        for (; ri < vertices.Length; ri++)
        {
            destination[ri] = MathF.Round(vertices[ri].Y * subsamplingRatio, MidpointRounding.AwayFromZero) / subsamplingRatio;
        }
    }

    private static void RoundY(ReadOnlySpan<PointF> vertices, Span<float> destination, float subsamplingRatio)
    {
        int ri = 0;
        for (; ri < vertices.Length; ri++)
        {
            destination[ri] = MathF.Round(vertices[ri].Y * subsamplingRatio, MidpointRounding.AwayFromZero) / subsamplingRatio;
        }
    }
}
