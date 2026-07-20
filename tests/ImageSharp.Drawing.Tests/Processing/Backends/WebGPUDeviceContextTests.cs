// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Processing.Backends;
using SixLabors.ImageSharp.Drawing.Tests;
using SixLabors.ImageSharp.Drawing.Tests.TestUtilities.Attributes;
using SixLabors.ImageSharp.Drawing.Tests.TestUtilities.ImageComparison;
using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using FeatureName = SixLabors.ImageSharp.Drawing.Processing.Backends.Native.WGPUFeatureName;

namespace SixLabors.ImageSharp.Drawing.Tests.Processing.Backends;

public class WebGPUDeviceContextTests
{
    [Fact]
    public void CompositeTargetDescriptors_MapSupportedPixelTypes()
    {
        AssertCompositeTargetDescriptor<NormalizedByte4>(WebGPUTextureFormat.Rgba8Snorm, PixelAlphaRepresentation.Unassociated, WebGPUTargetNumericEncoding.SignedUnit, FeatureName.TextureFormatsTier1);
        AssertCompositeTargetDescriptor<NormalizedByte4P>(WebGPUTextureFormat.Rgba8Snorm, PixelAlphaRepresentation.Associated, WebGPUTargetNumericEncoding.SignedUnit, FeatureName.TextureFormatsTier1);
        AssertCompositeTargetDescriptor<RgbaHalf>(WebGPUTextureFormat.Rgba16Float, PixelAlphaRepresentation.Unassociated, WebGPUTargetNumericEncoding.Unit, default);
        AssertCompositeTargetDescriptor<RgbaHalfP>(WebGPUTextureFormat.Rgba16Float, PixelAlphaRepresentation.Associated, WebGPUTargetNumericEncoding.Unit, default);
        AssertCompositeTargetDescriptor<Rgba32>(WebGPUTextureFormat.Rgba8Unorm, PixelAlphaRepresentation.Unassociated, WebGPUTargetNumericEncoding.Unit, default);
        AssertCompositeTargetDescriptor<Rgba32P>(WebGPUTextureFormat.Rgba8Unorm, PixelAlphaRepresentation.Associated, WebGPUTargetNumericEncoding.Unit, default);
        AssertCompositeTargetDescriptor<Bgra32>(WebGPUTextureFormat.Bgra8Unorm, PixelAlphaRepresentation.Unassociated, WebGPUTargetNumericEncoding.Unit, FeatureName.BGRA8UnormStorage);
        AssertCompositeTargetDescriptor<Bgra32P>(WebGPUTextureFormat.Bgra8Unorm, PixelAlphaRepresentation.Associated, WebGPUTargetNumericEncoding.Unit, FeatureName.BGRA8UnormStorage);
    }

    [Fact]
    public void OffscreenRgba16FloatTarget_UsesUnitNumericEncoding()
    {
        WebGPUTargetDescriptor descriptor = WebGPUDrawingBackend.CreateOffscreenTargetDescriptor(WebGPUTextureFormat.Rgba16Float, PixelAlphaRepresentation.Unassociated);

        Assert.Equal(WebGPUTargetNumericEncoding.Unit, descriptor.NumericEncoding);
    }

    [Fact]
    public void CompositeTargetDescriptors_RejectUnsupportedAssociatedLayouts()
    {
        Assert.False(WebGPUDrawingBackend.TryGetCompositeTargetDescriptor<Argb32P>(out _, out _));
        Assert.False(WebGPUDrawingBackend.TryGetCompositeTargetDescriptor<Abgr32P>(out _, out _));
    }

    [WebGPUFact]
    public void RenderTarget_ReadbackRejectsMismatchedFormat()
    {
        using WebGPURenderTarget target = new(WebGPUTextureFormat.Rgba8Unorm, 8, 8);
        using Image<Bgra32> destination = new(8, 8);

        Assert.Throws<NotSupportedException>(
            () => target.ReadbackInto(destination.Frames.RootFrame.PixelBuffer.GetRegion()));
    }

    [WebGPUFact]
    public void RenderTarget_CreateCanvas_RendersAndReadsBack()
    {
        using WebGPURenderTarget target = new(WebGPUTextureFormat.Rgba8Unorm, 18, 14);
        using (DrawingCanvas canvas = target.CreateCanvas())
        {
            canvas.Fill(Brushes.Solid(Color.Green), new RectanglePolygon(0, 0, 18, 14));
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
            canvas.Fill(Brushes.Solid(Color.Red), new RectanglePolygon(0, 0, 8, 6));
        }

        using Image readback = target.ReadbackImage();
        Image<Bgra32> typedReadback = Assert.IsType<Image<Bgra32>>(readback);

        Assert.Equal(target.Width, typedReadback.Width);
        Assert.Equal(target.Height, typedReadback.Height);
    }

    [WebGPUFact]
    public void RenderTarget_AssociatedRgbaTarget_UsesAssociatedCanvasAndReadback()
        => AssertAssociatedRenderTarget<Rgba32P>(WebGPUTextureFormat.Rgba8Unorm);

    [WebGPUFact]
    public void RenderTarget_AssociatedBgraTarget_UsesAssociatedCanvasAndReadback()
        => AssertAssociatedRenderTarget<Bgra32P>(WebGPUTextureFormat.Bgra8Unorm);

    [WebGPUFact]
    public void RenderTarget_AssociatedSnormTarget_UsesAssociatedCanvasAndReadback()
    {
        using WebGPURenderTarget probe = new(WebGPUTextureFormat.Rgba8Unorm, 1, 1);
        WebGPURuntime.DeviceSharedState deviceState = WebGPURuntime.GetOrCreateDeviceState(WebGPURuntime.GetApi(), probe.DeviceContext.DeviceHandle);

        if (!deviceState.HasFeature(FeatureName.TextureFormatsTier1))
        {
            Assert.Throws<NotSupportedException>(() => new WebGPURenderTarget(WebGPUTextureFormat.Rgba8Snorm, PixelAlphaRepresentation.Associated, 8, 6));
            return;
        }

        AssertAssociatedRenderTarget<NormalizedByte4P>(WebGPUTextureFormat.Rgba8Snorm);
    }

    [WebGPUFact]
    public void RenderTarget_AssociatedHalfTarget_UsesAssociatedCanvasAndReadback()
        => AssertAssociatedRenderTarget<RgbaHalfP>(WebGPUTextureFormat.Rgba16Float);

    [WebGPUFact]
    public void RenderTarget_HalfTargets_InitializeToNativeDefault()
    {
        AssertRenderTargetInitializesToNativeDefault<RgbaHalf>(WebGPUTextureFormat.Rgba16Float, PixelAlphaRepresentation.Unassociated);
        AssertRenderTargetInitializesToNativeDefault<RgbaHalfP>(WebGPUTextureFormat.Rgba16Float, PixelAlphaRepresentation.Associated);
    }

    [WebGPUFact]
    public void RenderTarget_SnormTargets_InitializeToNativeDefault()
    {
        using WebGPURenderTarget probe = new(WebGPUTextureFormat.Rgba8Unorm, 1, 1);
        WebGPURuntime.DeviceSharedState deviceState = WebGPURuntime.GetOrCreateDeviceState(WebGPURuntime.GetApi(), probe.DeviceContext.DeviceHandle);

        if (!deviceState.HasFeature(FeatureName.TextureFormatsTier1))
        {
            Assert.Throws<NotSupportedException>(() => new WebGPURenderTarget(WebGPUTextureFormat.Rgba8Snorm, 8, 6));
            return;
        }

        AssertRenderTargetInitializesToNativeDefault<NormalizedByte4>(WebGPUTextureFormat.Rgba8Snorm, PixelAlphaRepresentation.Unassociated);
        AssertRenderTargetInitializesToNativeDefault<NormalizedByte4P>(WebGPUTextureFormat.Rgba8Snorm, PixelAlphaRepresentation.Associated);
    }

    [WebGPUFact]
    public void RenderTarget_AssociatedTarget_PreservesBackdropAcrossFlushes()
    {
        Color backdrop = Color.FromPixel(new Rgba32P(80, 40, 20, 128));
        Color foreground = Color.FromPixel(new Rgba32(20, 120, 200, 96));

        using WebGPURenderTarget target = new(WebGPUTextureFormat.Rgba8Unorm, PixelAlphaRepresentation.Associated, 8, 6);
        using (DrawingCanvas canvas = target.CreateCanvas())
        {
            canvas.Fill(Brushes.Solid(backdrop), new RectanglePolygon(0, 0, target.Width, target.Height));
        }

        using (DrawingCanvas canvas = target.CreateCanvas())
        {
            canvas.Fill(Brushes.Solid(foreground), new RectanglePolygon(0, 0, target.Width, target.Height));
        }

        using Image<Rgba32P> expected = new(target.Width, target.Height);
        expected.Mutate(context => context.Paint(canvas =>
        {
            canvas.Fill(Brushes.Solid(backdrop));
            canvas.Fill(Brushes.Solid(foreground));
        }));

        using Image<Rgba32P> actual = target.ReadbackImage<Rgba32P>();
        Rgba32P expectedPixel = expected[target.Width / 2, target.Height / 2];
        Rgba32P actualPixel = actual[target.Width / 2, target.Height / 2];

        // CPU byte blending and WGSL floating-point blending can round an associated color channel
        // to adjacent bytes. Alpha is not association-scaled and must remain exact.
        Assert.InRange(Math.Abs(expectedPixel.R - actualPixel.R), 0, 1);
        Assert.InRange(Math.Abs(expectedPixel.G - actualPixel.G), 0, 1);
        Assert.InRange(Math.Abs(expectedPixel.B - actualPixel.B), 0, 1);
        Assert.Equal(expectedPixel.A, actualPixel.A);
    }

    [WebGPUFact]
    public void RenderTarget_AssociatedImageBrush_IsNotAssociatedTwice()
    {
        Rgba32P sourcePixel = new(80, 40, 20, 128);

        using Image<Rgba32P> source = new(2, 2, sourcePixel);
        using WebGPURenderTarget target = new(WebGPUTextureFormat.Rgba8Unorm, PixelAlphaRepresentation.Associated, 8, 6);
        using (DrawingCanvas canvas = target.CreateCanvas())
        {
            canvas.Fill(new ImageBrush<Rgba32P>(source), new RectanglePolygon(0, 0, target.Width, target.Height));
        }

        using Image<Rgba32P> actual = target.ReadbackImage<Rgba32P>();
        Assert.Equal(sourcePixel, actual[target.Width / 2, target.Height / 2]);
    }

    [WebGPUFact]
    public void RenderTarget_ReadbackInto_BufferRegion_WritesSubregion()
    {
        using WebGPURenderTarget target = new(6, 4);
        using (DrawingCanvas canvas = target.CreateCanvas())
        {
            canvas.Fill(Brushes.Solid(Color.Red), new RectanglePolygon(-1, -1, target.Width + 2, target.Height + 2));
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
    public void PresentationRenderer_TransfersEverySupportedTargetFormat()
    {
        using WebGPURenderTarget probe = new(WebGPUTextureFormat.Rgba8Unorm, 1, 1);
        WebGPURuntime.DeviceSharedState deviceState = WebGPURuntime.GetOrCreateDeviceState(WebGPURuntime.GetApi(), probe.DeviceContext.DeviceHandle);

        AssertPresentationTransfer<Rgba32>(WebGPUTextureFormat.Rgba8Unorm, PixelAlphaRepresentation.Unassociated, copyToSurface: true);
        AssertPresentationTransfer<Rgba32>(WebGPUTextureFormat.Rgba8Unorm, PixelAlphaRepresentation.Unassociated, copyToSurface: false);
        AssertPresentationTransfer<Rgba32P>(WebGPUTextureFormat.Rgba8Unorm, PixelAlphaRepresentation.Associated, copyToSurface: true);
        AssertPresentationTransfer<Rgba32P>(WebGPUTextureFormat.Rgba8Unorm, PixelAlphaRepresentation.Associated, copyToSurface: false);
        AssertPresentationTransfer<RgbaHalfP>(WebGPUTextureFormat.Rgba16Float, PixelAlphaRepresentation.Associated, copyToSurface: true);
        AssertPresentationTransfer<RgbaHalfP>(WebGPUTextureFormat.Rgba16Float, PixelAlphaRepresentation.Associated, copyToSurface: false);

        if (deviceState.HasFeature(FeatureName.BGRA8UnormStorage))
        {
            AssertPresentationTransfer<Bgra32P>(WebGPUTextureFormat.Bgra8Unorm, PixelAlphaRepresentation.Associated, copyToSurface: true);
            AssertPresentationTransfer<Bgra32P>(WebGPUTextureFormat.Bgra8Unorm, PixelAlphaRepresentation.Associated, copyToSurface: false);
        }

        if (deviceState.HasFeature(FeatureName.TextureFormatsTier1))
        {
            AssertPresentationTransfer<NormalizedByte4P>(WebGPUTextureFormat.Rgba8Snorm, PixelAlphaRepresentation.Associated, copyToSurface: true);
            AssertPresentationTransfer<NormalizedByte4P>(WebGPUTextureFormat.Rgba8Snorm, PixelAlphaRepresentation.Associated, copyToSurface: false);
        }
    }

    private static void AssertCompositeTargetDescriptor<TPixel>(WebGPUTextureFormat format, PixelAlphaRepresentation alphaRepresentation, WebGPUTargetNumericEncoding numericEncoding, FeatureName requiredFeature)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        Assert.True(WebGPUDrawingBackend.TryGetCompositeTargetDescriptor<TPixel>(out WebGPUTargetDescriptor descriptor, out FeatureName actualRequiredFeature));
        Assert.Equal(format, descriptor.Format);
        Assert.Equal(alphaRepresentation, descriptor.AlphaRepresentation);
        Assert.Equal(numericEncoding, descriptor.NumericEncoding);
        Assert.Equal(requiredFeature, actualRequiredFeature);
    }

    private static void AssertRenderTargetInitializesToNativeDefault<TPixel>(WebGPUTextureFormat format, PixelAlphaRepresentation alphaRepresentation)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using WebGPURenderTarget target = new(format, alphaRepresentation, 8, 6);
        using Image<TPixel> readback = target.ReadbackImage<TPixel>();

        // WebGPU and ImageSharp clean allocations both initialize native pixel storage to zero.
        // Preserve those bits: unit formats interpret them as transparent black, while signed-unit
        // formats interpret them as the midpoint of their logical range.
        Assert.Equal(default(TPixel), readback[target.Width / 2, target.Height / 2]);
    }

    private static void AssertAssociatedRenderTarget<TPixel>(WebGPUTextureFormat format)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        Color color = Color.FromPixel(new Rgba32P(80, 40, 20, 128));

        using Image<TPixel> expected = new(8, 6);
        using (DrawingCanvas<TPixel> canvas = new(expected.Configuration, new DrawingOptions(), expected.Frames.RootFrame.PixelBuffer.GetRegion()))
        {
            canvas.Fill(Brushes.Solid(color), new RectanglePolygon(0, 0, expected.Width, expected.Height));
        }

        using WebGPURenderTarget target = new(format, PixelAlphaRepresentation.Associated, 8, 6);
        Assert.Equal(PixelAlphaRepresentation.Associated, target.AlphaRepresentation);

        using (DrawingCanvas canvas = target.CreateCanvas())
        {
            Assert.IsType<DrawingCanvas<TPixel>>(canvas);
            canvas.Fill(Brushes.Solid(color), new RectanglePolygon(0, 0, target.Width, target.Height));
        }

        using Image readback = target.ReadbackImage();
        Image<TPixel> typedReadback = Assert.IsType<Image<TPixel>>(readback);
        TPixel expectedPixel = expected[expected.Width / 2, expected.Height / 2];
        TPixel actualPixel = typedReadback[target.Width / 2, target.Height / 2];
        Assert.Equal(expectedPixel.ToRgba32(), actualPixel.ToRgba32());

        Assert.Throws<NotSupportedException>(() => target.ReadbackImage<Rgba32>());

        using WebGPURenderTarget child = target.CreateRenderTarget(4, 3);
        Assert.Equal(target.Format, child.Format);
        Assert.Equal(target.AlphaRepresentation, child.AlphaRepresentation);
    }

    private static void AssertPresentationTransfer<TPixel>(WebGPUTextureFormat format, PixelAlphaRepresentation alphaRepresentation, bool copyToSurface)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using WebGPURenderTarget source = new(format, alphaRepresentation, 13, 9);
        using WebGPURenderTarget destination = source.CreateRenderTarget(source.Width, source.Height);
        using (DrawingCanvas canvas = source.CreateCanvas())
        {
            canvas.Fill(Brushes.Solid(Color.FromPixel(new Rgba32(173, 41, 229, 137))), new RectanglePolygon(0, 0, source.Width, source.Height));
            canvas.Fill(Brushes.Solid(Color.FromPixel(new Rgba32(19, 211, 67, 83))), new RectanglePolygon(3, 2, 7, 5));
        }

        using (WebGPUPresentationRenderer renderer = new(WebGPURuntime.GetApi(), source.DeviceContext, source, copyToSurface))
        {
            renderer.Present(destination.TextureHandle, destination.TextureViewHandle);
        }

        using Image<TPixel> expected = source.ReadbackImage<TPixel>();
        using Image<TPixel> actual = destination.ReadbackImage<TPixel>();
        ImageComparer.Exact.VerifySimilarity(expected, actual);
    }
}
