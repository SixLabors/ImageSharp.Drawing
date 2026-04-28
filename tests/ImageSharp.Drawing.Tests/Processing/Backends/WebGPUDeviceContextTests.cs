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
        using WebGPUHandle.HandleReference textureReference = target.TextureHandle.AcquireReference();
        using WebGPUHandle.HandleReference textureViewReference = target.TextureViewHandle.AcquireReference();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => drawing.CreateFrame(0, textureViewReference.Handle, target.Format, 8, 8));

        Assert.Throws<ArgumentOutOfRangeException>(
            () => drawing.CreateFrame(textureReference.Handle, 0, target.Format, 8, 8));

        Assert.Throws<ArgumentException>(
            () => mismatched.CreateFrame(target.TextureHandle, target.TextureViewHandle, target.Format, 8, 8));
    }

    [WebGPUFact]
    public void CreateCanvas_WithExternalTexture_UsesGpuPath()
    {
        using WebGPUDeviceContext<Rgba32> drawing = new();
        using WebGPURenderTarget<Rgba32> target = drawing.CreateRenderTarget(32, 24);
        using (DrawingCanvas canvas = drawing.CreateCanvas(
                   new DrawingOptions(),
                   target.TextureHandle,
                   target.TextureViewHandle,
                   target.Format,
                   32,
                   24))
        {
            canvas.Fill(Brushes.Solid(Color.Red), new RectangularPolygon(0, 0, 32, 24));
            canvas.Flush();

            using Image<Rgba32> readback = target.Readback();
            Assert.NotEqual(default, readback[16, 12]);
        }
    }

    [WebGPUFact]
    public void RenderTarget_CreateCanvas_RendersAndReadsBack()
    {
        using WebGPUDeviceContext<Rgba32> drawing = new();
        using WebGPURenderTarget<Rgba32> target = drawing.CreateRenderTarget(18, 14);
        using (DrawingCanvas canvas = target.CreateCanvas())
        {
            canvas.Fill(Brushes.Solid(Color.Green), new RectangularPolygon(0, 0, 18, 14));
            canvas.Flush();

            using Image<Rgba32> readback = target.Readback();
            Assert.NotEqual(default, readback[9, 7]);
        }
    }

    [WebGPUFact]
    public void Dispose_Context_DoesNotReleaseOwnedTargetHandles()
    {
        using WebGPUDeviceContext<Rgba32> drawing = new();
        using WebGPURenderTarget<Rgba32> target = drawing.CreateRenderTarget(12, 10);

        drawing.Dispose();

        using WebGPUDrawingBackend backend = new();
        using Image<Rgba32> image = new(12, 10);

        backend.ReadRegion(
            Configuration.Default,
            target.NativeFrame,
            target.Bounds,
            new Buffer2DRegion<Rgba32>(image.Frames.RootFrame.PixelBuffer));
    }
}
