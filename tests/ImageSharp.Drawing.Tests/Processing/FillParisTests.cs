// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Processing.Backends;
using SixLabors.ImageSharp.Drawing.Tests.TestUtilities.Attributes;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace SixLabors.ImageSharp.Drawing.Tests.Processing;

public class FillParisTests
{
    private const float Scale = 1f;
    private const int Width = 1096;
    private const int Height = 1060;

    private static readonly string SvgFilePath =
        TestFile.GetInputFileFullPath(TestImages.Svg.Paris30k);

    private static readonly List<SvgBenchmarkHelper.SvgElement> elements = SvgBenchmarkHelper.ParseSvg(SvgFilePath);
    private static readonly List<(IPath Path, SolidBrush Fill, SolidPen Stroke)> isElements =
        SvgBenchmarkHelper.BuildImageSharpElements(elements, Scale);

    [Fact(Skip = "Benchmarking only")]
    public void FillParis_ImageSharp_CPU()
    {
        using Image<Rgba32> image = new(Width, Height);
        image.Mutate(c => c.ProcessWithCanvas(canvas =>
        {
            foreach ((IPath path, SolidBrush fill, SolidPen stroke) in isElements)
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

    [WebGPUFact(Skip = "Benchmarking Only")]
    public void FillParis_ImageSharp_WebGPU()
    {
        using FillParisWebGpuContext webGpu = new();
        using DrawingCanvas<Rgba32> canvas = new(webGpu.Configuration, webGpu.NativeFrame, new DrawingOptions());

        foreach ((IPath path, SolidBrush fill, SolidPen stroke) in isElements)
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

        canvas.Flush();

        Assert.True(
            webGpu.Backend.DiagnosticLastFlushUsedGPU,
            webGpu.Backend.DiagnosticLastSceneFailure ?? "The last flush did not use the staged path.");
    }

    private sealed class FillParisWebGpuContext : IDisposable
    {
        private readonly nint textureHandle;
        private readonly nint textureViewHandle;

        public FillParisWebGpuContext()
        {
            this.Backend = new WebGPUDrawingBackend();
            this.Configuration = Configuration.Default.Clone();
            this.Configuration.SetDrawingBackend(this.Backend);

            Assert.True(
                WebGPUTestNativeSurfaceAllocator.TryCreate<Rgba32>(
                    Width,
                    Height,
                    out NativeSurface nativeSurface,
                    out this.textureHandle,
                    out this.textureViewHandle,
                    out string createError),
                createError);

            this.NativeFrame = new NativeCanvasFrame<Rgba32>(
                new Rectangle(0, 0, Width, Height),
                nativeSurface);
        }

        public WebGPUDrawingBackend Backend { get; }

        public Configuration Configuration { get; }

        public NativeCanvasFrame<Rgba32> NativeFrame { get; }

        public void Dispose()
        {
            WebGPUTestNativeSurfaceAllocator.Release(this.textureHandle, this.textureViewHandle);
            this.Backend.Dispose();
        }
    }
}
