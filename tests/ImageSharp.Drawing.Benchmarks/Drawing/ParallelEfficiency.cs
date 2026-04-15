// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using BenchmarkDotNet.Attributes;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Tests;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace SixLabors.ImageSharp.Drawing.Benchmarks.Drawing;

public class ParallelEfficiency
{
    private static readonly string SvgFilePath =
        TestFile.GetInputFileFullPath(TestImages.Svg.GhostscriptTiger);

    private Image<Rgba32> image;
    private List<(IPath Path, Processing.SolidBrush Fill, SolidPen Stroke)> isElements;

    public static IEnumerable<int> MaxDegreeOfParallelismValues()
    {
        int processorCount = Environment.ProcessorCount;
        for (int p = 1; p <= processorCount; p *= 2)
        {
            yield return p;
        }

        if ((processorCount & (processorCount - 1)) != 0)
        {
            yield return processorCount;
        }
    }

    [ParamsSource(nameof(MaxDegreeOfParallelismValues))]
    public int MaxDegreeOfParallelism { get; set; }


    [Params(1000, 100)]
    public int Dimensions { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        int width = this.Dimensions;
        int height = this.Dimensions;
        float scale = this.Dimensions / 200f;

        ThreadPool.GetMinThreads(out int minWorkerThreads, out int minCompletionPortThreads);
        int desiredWorkerThreads = Math.Max(minWorkerThreads, Environment.ProcessorCount);
        ThreadPool.SetMinThreads(desiredWorkerThreads, minCompletionPortThreads);
        Parallel.For(0, desiredWorkerThreads, static _ => { });

        List<SvgBenchmarkHelper.SvgElement> elements = SvgBenchmarkHelper.ParseSvg(SvgFilePath);

        this.image = new Image<Rgba32>(width, height);
        this.isElements = SvgBenchmarkHelper.BuildImageSharpElements(elements, scale);
    }

    [GlobalCleanup]
    public void Cleanup() => this.image.Dispose();

    [Benchmark]
    public void FillTiger()
    {
        Configuration configuration = this.image.Configuration.Clone();
        configuration.MaxDegreeOfParallelism = this.MaxDegreeOfParallelism;
        this.image.Mutate(configuration, c => ProcessWithCanvasExtensions.ProcessWithCanvas(c, canvas =>
        {
            foreach ((IPath path, Processing.SolidBrush fill, SolidPen stroke) in this.isElements)
            {
                if (fill is not null)
                {
                    canvas.Fill(fill, path);
                }

                if (stroke is not null)
                {
                    canvas.Draw(stroke, path);
                }
            }
        }));
    }
}
