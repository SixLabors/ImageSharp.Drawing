// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Processing.Backends;
using SixLabors.ImageSharp.Drawing.Tests.TestUtilities.Attributes;
using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Drawing.Tests.Processing.Backends;

public class WebGPUDeviceContextTests
{
    [Fact]
    public void Create_RejectsZeroHandles()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new WebGPUDeviceContext<Rgba32>(0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new WebGPUDeviceContext<Rgba32>(1, 0));
    }

    [WebGPUFact]
    public void CreateFrame_RejectsInvalidHandlesAndMismatchedFormat()
    {
        using WebGPUDeviceContext<Rgba32> drawing = new();
        using WebGPURenderTarget<Rgba32> target = drawing.CreateRenderTarget(8, 8);
        using WebGPUDeviceContext<Bgra32> mismatched = new();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => drawing.CreateFrame(0, target.TextureViewHandle, target.Format, 8, 8));

        Assert.Throws<ArgumentOutOfRangeException>(
            () => drawing.CreateFrame(target.TextureHandle, 0, target.Format, 8, 8));

        Assert.Throws<ArgumentException>(
            () => mismatched.CreateFrame(target.TextureHandle, target.TextureViewHandle, target.Format, 8, 8));
    }

    [WebGPUFact]
    public void CreateCanvas_WithExternalTexture_UsesGpuPath()
    {
        using WebGPUDeviceContext<Rgba32> drawing = new();
        using WebGPURenderTarget<Rgba32> target = drawing.CreateRenderTarget(32, 24);
        using (DrawingCanvas<Rgba32> canvas = drawing.CreateCanvas(
                   target.TextureHandle,
                   target.TextureViewHandle,
                   target.Format,
                   32,
                   24,
                   new DrawingOptions()))
        {
            canvas.Fill(Brushes.Solid(Color.Red), new RectangularPolygon(0, 0, 32, 24));
            canvas.Flush();

            Assert.True(
                drawing.Backend.DiagnosticLastFlushUsedGPU,
                drawing.Backend.DiagnosticLastSceneFailure ?? "The last flush did not use the staged path.");

            using Image<Rgba32> readback = new(32, 24);
            Buffer2DRegion<Rgba32> destination = new(readback.Frames.RootFrame.PixelBuffer, readback.Bounds);
            Assert.True(
                drawing.Backend.TryReadRegion(drawing.Configuration, target.NativeFrame, new Rectangle(0, 0, 32, 24), destination));
            Assert.NotEqual(default, readback[16, 12]);
        }
    }

    [WebGPUFact]
    public void RenderTarget_CreateHybridCanvas_Image_Works()
    {
        using WebGPUDeviceContext<Rgba32> drawing = new();
        using WebGPURenderTarget<Rgba32> target = drawing.CreateRenderTarget(20, 16);
        using Image<Rgba32> cpuImage = new(20, 16);
        using (DrawingCanvas<Rgba32> canvas = target.CreateHybridCanvas(cpuImage))
        {
            canvas.Fill(Brushes.Solid(Color.Blue), new RectangularPolygon(0, 0, 20, 16));
            canvas.Flush();

            Assert.True(
                drawing.Backend.DiagnosticLastFlushUsedGPU,
                drawing.Backend.DiagnosticLastSceneFailure ?? "The last flush did not use the staged path.");

            HybridCanvasFrame<Rgba32> frame = target.CreateHybridFrame(cpuImage);
            Assert.True(frame.TryGetCpuRegion(out _));
            Assert.True(frame.TryGetNativeSurface(out _));
        }
    }

    [WebGPUFact]
    public void RenderTarget_CreateCanvas_RendersAndReadsBack()
    {
        using WebGPUDeviceContext<Rgba32> drawing = new();
        using WebGPURenderTarget<Rgba32> target = drawing.CreateRenderTarget(18, 14);
        using (DrawingCanvas<Rgba32> canvas = target.CreateCanvas())
        {
            canvas.Fill(Brushes.Solid(Color.Green), new RectangularPolygon(0, 0, 18, 14));
            canvas.Flush();

            using Image<Rgba32> readback = new(18, 14);
            Buffer2DRegion<Rgba32> destination = new(readback.Frames.RootFrame.PixelBuffer, readback.Bounds);
            Assert.True(
                drawing.Backend.TryReadRegion(drawing.Configuration, target.NativeFrame, new Rectangle(0, 0, 18, 14), destination));
            Assert.NotEqual(default, readback[9, 7]);
        }
    }

    [WebGPUFact]
    public void Dispose_Context_DoesNotReleaseOwnedTargetHandles()
    {
        using WebGPUDeviceContext<Rgba32> drawing = new();
        using WebGPURenderTarget<Rgba32> target = drawing.CreateRenderTarget(12, 10);

        nint textureHandle = target.TextureHandle;
        drawing.Dispose();

        Assert.True(
            WebGPUTextureTransfer.TryReadTexture(textureHandle, 12, 10, out Image<Rgba32>? image, out string readError),
            readError);

        image?.Dispose();
    }
}
