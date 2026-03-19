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

    [Fact]
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

    [WebGPUFact]
    public void FillParis_ImageSharp_WebGPU()
    {
        using WebGPUDrawingBackend backend = new();
        Assert.True(
            WebGPUTestNativeSurfaceAllocator.TryCreate<Rgba32>(
                Width,
                Height,
                out NativeSurface nativeSurface,
                out nint textureHandle,
                out nint textureViewHandle,
                out string createError),
            createError);

        try
        {
            using Image<Rgba32> initialImage = new(Width, Height);
            Assert.True(
                WebGPUTestNativeSurfaceAllocator.TryWriteTexture(
                    textureHandle,
                    Width,
                    Height,
                    initialImage,
                    out string uploadError),
                uploadError);

            Configuration configuration = Configuration.Default.Clone();
            configuration.SetDrawingBackend(backend);

            using DrawingCanvas<Rgba32> canvas =
                new(configuration, new NativeCanvasFrame<Rgba32>(new Rectangle(0, 0, Width, Height), nativeSurface), new DrawingOptions());

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
                WebGPUTestNativeSurfaceAllocator.TryReadTexture(
                    textureHandle,
                    Width,
                    Height,
                    out Image<Rgba32> image,
                    out string readError),
                readError);
            image.Dispose();

            Assert.True(backend.TestingGPUInitializationAttempted);
            Assert.True(
                backend.DiagnosticGpuCompositeCount > 0,
                backend.DiagnosticLastSceneFailure ?? "No GPU composites were recorded.");
        }
        finally
        {
            WebGPUTestNativeSurfaceAllocator.Release(textureHandle, textureViewHandle);
        }
    }
}
