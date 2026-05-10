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
        Assert.Throws<ArgumentOutOfRangeException>(() => new WebGPUDeviceContext(0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new WebGPUDeviceContext(1, 0));
    }

    [WebGPUFact]
    public void CreateCanvas_RejectsInvalidHandles_AndReadbackRejectsMismatchedFormat()
    {
        using WebGPUDeviceContext drawing = new();
        using WebGPURenderTarget target = drawing.CreateRenderTarget(8, 8);
        using WebGPUHandle.HandleReference textureReference = target.TextureHandle.AcquireReference();
        using WebGPUHandle.HandleReference textureViewReference = target.TextureViewHandle.AcquireReference();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => drawing.CreateCanvas(0, textureViewReference.Handle, target.Format, 8, 8));

        Assert.Throws<ArgumentOutOfRangeException>(
            () => drawing.CreateCanvas(textureReference.Handle, 0, target.Format, 8, 8));

        using Image<Bgra32> destination = new(8, 8);

        Assert.Throws<NotSupportedException>(
            () => target.ReadbackInto(destination.Frames.RootFrame.PixelBuffer.GetRegion()));
    }

    [WebGPUFact]
    public void CreateCanvas_WithExternalTexture_UsesGpuPath()
    {
        using WebGPUDeviceContext drawing = new();
        using WebGPURenderTarget target = drawing.CreateRenderTarget(32, 24);
        using (DrawingCanvas canvas = drawing.CreateCanvas(
                   new DrawingOptions(),
                   target.TextureHandle,
                   target.TextureViewHandle,
                   target.Format,
                   32,
                   24))
        {
            canvas.Fill(Brushes.Solid(Color.Red), new RectangularPolygon(0, 0, 32, 24));
        }

        using Image<Rgba32> readback = target.ReadbackImage<Rgba32>();
        Assert.NotEqual(default, readback[16, 12]);
    }

    [WebGPUFact]
    public void RenderTarget_CreateCanvas_RendersAndReadsBack()
    {
        using WebGPUDeviceContext drawing = new();
        using WebGPURenderTarget target = drawing.CreateRenderTarget(18, 14);
        using (DrawingCanvas canvas = target.CreateCanvas())
        {
            canvas.Fill(Brushes.Solid(Color.Green), new RectangularPolygon(0, 0, 18, 14));
        }

        using Image<Rgba32> readback = target.ReadbackImage<Rgba32>();
        Assert.NotEqual(default, readback[9, 7]);
    }

    [WebGPUFact]
    public void RenderTarget_ReadbackImage_UsesTargetFormat()
    {
        using WebGPURenderTarget target = new(WebGPUTextureFormat.Bgra8Unorm, 8, 6);
        using (DrawingCanvas canvas = target.CreateCanvas())
        {
            canvas.Fill(Brushes.Solid(Color.Red), new RectangularPolygon(0, 0, 8, 6));
        }

        using Image readback = target.ReadbackImage();
        Image<Bgra32> typedReadback = Assert.IsType<Image<Bgra32>>(readback);

        Assert.Equal(target.Width, typedReadback.Width);
        Assert.Equal(target.Height, typedReadback.Height);
    }

    [WebGPUFact]
    public void RenderTarget_ReadbackInto_BufferRegion_WritesSubregion()
    {
        using WebGPURenderTarget target = new(6, 4);
        using (DrawingCanvas canvas = target.CreateCanvas())
        {
            canvas.Fill(Brushes.Solid(Color.Red), new RectangularPolygon(-1, -1, target.Width + 2, target.Height + 2));
        }

        using Image<Rgba32> destination = new(10, 8, Color.Blue.ToPixel<Rgba32>());

        // The public readback sink is a buffer region so callers can target an ImageFrame,
        // an arbitrary region of a larger image, or any other Buffer2D-backed destination.
        Buffer2DRegion<Rgba32> destinationRegion =
            destination.Frames.RootFrame.PixelBuffer.GetRegion().GetSubRegion(2, 3, target.Width, target.Height);

        target.ReadbackInto(destinationRegion);

        Assert.Equal(Color.Blue.ToPixel<Rgba32>(), destination[1, 1]);
        Assert.Equal(Color.Red.ToPixel<Rgba32>(), destination[2, 3]);
        Assert.Equal(Color.Red.ToPixel<Rgba32>(), destination[7, 6]);
        Assert.Equal(Color.Blue.ToPixel<Rgba32>(), destination[8, 7]);
    }

    [WebGPUFact]
    public void Dispose_Context_DoesNotReleaseOwnedTargetHandles()
    {
        using WebGPUDeviceContext drawing = new();
        using WebGPURenderTarget target = drawing.CreateRenderTarget(12, 10);

        drawing.Dispose();

        using WebGPUDrawingBackend backend = new();
        using Image<Rgba32> image = new(12, 10);

        backend.ReadRegion(
            Configuration.Default,
            WebGPUCanvasFactory.CreateFrame<Rgba32>(target.Bounds, target.Surface),
            target.Bounds,
            image.Frames.RootFrame.PixelBuffer.GetRegion());
    }
}
