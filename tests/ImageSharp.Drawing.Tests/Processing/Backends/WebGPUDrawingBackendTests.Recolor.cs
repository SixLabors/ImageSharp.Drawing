// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Processing.Backends;
using SixLabors.ImageSharp.Drawing.Tests.TestUtilities.Attributes;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using FeatureName = SixLabors.ImageSharp.Drawing.Processing.Backends.Native.WGPUFeatureName;

namespace SixLabors.ImageSharp.Drawing.Tests.Processing.Backends;

public partial class WebGPUDrawingBackendTests
{
    [WebGPUFact]
    public void FillPath_WithRecolorBrush_UnmatchedKeyStillAppliesOuterClear()
    {
        Rgba32 background = Color.Green.ToPixel<Rgba32>();
        Brush brush = new RecolorBrush(Color.Red, Color.Blue, 0.01F);
        DrawingOptions drawingOptions = new()
        {
            GraphicsOptions = new GraphicsOptions
            {
                Antialias = false,
                AlphaCompositionMode = PixelAlphaCompositionMode.Clear
            }
        };

        using Image<Rgba32> defaultImage = new(8, 8, background);
        RenderWithDefaultBackend(defaultImage, drawingOptions, canvas => canvas.Fill(brush));

        using WebGPUDrawingBackend backend = new();
        using Image<Rgba32> initialImage = new(8, 8, background);
        using Image<Rgba32> actual = RenderWithNativeSurfaceWebGpuBackend(8, 8, backend, drawingOptions, canvas => canvas.Fill(brush), initialImage);

        // Recolor supplies the unchanged backdrop when the key does not match, but the brush
        // still runs that overlay through the configured outer composition operation.
        Assert.Equal(default, defaultImage[4, 4]);
        Assert.Equal(defaultImage[4, 4], actual[4, 4]);
    }

    [WebGPUFact]
    public void FillPath_WithRecolorBrush_TargetPreservesF32Precision()
    {
        Color targetColor = Color.FromScaledVector(new Vector4(0.4999F, 0F, 0F, 1F));
        Brush brush = new RecolorBrush(Color.Black, targetColor, 1F);
        DrawingOptions drawingOptions = CreateRecolorSourceOptions();

        void Draw(DrawingCanvas canvas)
        {
            canvas.Fill(Brushes.Solid(Color.Black));
            canvas.Fill(brush);
        }

        using Image<Rgba32> defaultImage = new(8, 8);
        RenderWithDefaultBackend(defaultImage, drawingOptions, Draw);

        using WebGPUDrawingBackend backend = new();
        using Image<Rgba32> actual = RenderWithNativeSurfaceWebGpuBackend<Rgba32>(8, 8, backend, drawingOptions, Draw);

        // 0.4999 * 255 rounds to 127. Transporting the color through binary16 first rounds it
        // to 0.5, which incorrectly crosses the byte midpoint and produces 128.
        Rgba32 expected = new(127, 0, 0, 255);
        Assert.Equal(expected, defaultImage[4, 4]);
        Assert.Equal(expected, actual[4, 4]);
    }

    [WebGPUFact]
    public void FillPath_WithRecolorBrush_Rgba32PTargetUsesStoredAlphaForAssociation()
    {
        Color targetColor = Color.FromScaledVector(new Vector4(0.49F, 0F, 0F, 0.5F));
        Brush brush = new RecolorBrush(Color.Black, targetColor, 1F);
        DrawingOptions drawingOptions = CreateRecolorSourceOptions();

        void Draw(DrawingCanvas canvas)
        {
            canvas.Fill(Brushes.Solid(Color.Black));
            canvas.Fill(brush);
        }

        using Image<Rgba32P> defaultImage = new(8, 8);
        RenderWithDefaultBackend(defaultImage, drawingOptions, Draw);

        using Image<Rgba32P> actual = RenderRecolorWithAssociatedWebGpuBackend<Rgba32P>(WebGPUTextureFormat.Rgba8Unorm, drawingOptions, Draw);

        // Rgba32P first stores alpha 0.5 as 128, then associates red with that stored alpha:
        // round(0.49 * 128) = 63. Multiplying by the unquantized alpha would incorrectly yield 62.
        Rgba32P expected = new(63, 0, 0, 128);
        Assert.Equal(expected, defaultImage[4, 4]);
        Assert.Equal(expected, actual[4, 4]);
    }

    [WebGPUFact]
    public void FillPath_WithRecolorBrush_NormalizedByte4PTargetMatchesExactStorage()
    {
        using WebGPUDeviceContext deviceContext = new();
        WebGPURuntime.DeviceSharedState deviceState = WebGPURuntime.GetOrCreateDeviceState(WebGPURuntime.GetApi(), deviceContext.DeviceHandle);

        if (!deviceState.HasFeature(FeatureName.TextureFormatsTier1))
        {
            Assert.Throws<NotSupportedException>(() => deviceContext.CreateRenderTarget(WebGPUTextureFormat.Rgba8Snorm, PixelAlphaRepresentation.Associated, 8, 8));
            return;
        }

        Color targetColor = Color.FromScaledVector(new Vector4(0.37F, 0.61F, 0.19F, 0.503F));
        Brush brush = new RecolorBrush(Color.Black, targetColor, 1F);
        DrawingOptions drawingOptions = CreateRecolorSourceOptions();

        void Draw(DrawingCanvas canvas)
        {
            canvas.Fill(Brushes.Solid(Color.Black));
            canvas.Fill(brush);
        }

        using Image<NormalizedByte4P> defaultImage = new(8, 8);
        RenderWithDefaultBackend(defaultImage, drawingOptions, Draw);

        using Image<NormalizedByte4P> actual = RenderRecolorWithAssociatedWebGpuBackend<NormalizedByte4P>(WebGPUTextureFormat.Rgba8Snorm, drawingOptions, Draw);

        NormalizedByte4P expected = targetColor.ToPixel<NormalizedByte4P>();
        Assert.Equal(expected, defaultImage[4, 4]);
        Assert.Equal(expected, actual[4, 4]);
    }

    [WebGPUFact]
    public void FillPath_WithRecolorBrush_RgbaHalfPTargetMatchesExactStorage()
    {
        Color targetColor = Color.FromScaledVector(new Vector4(0.37F, 0.61F, 0.19F, 0.503F));
        Brush brush = new RecolorBrush(Color.Black, targetColor, 1F);
        DrawingOptions drawingOptions = CreateRecolorSourceOptions();

        void Draw(DrawingCanvas canvas)
        {
            canvas.Fill(Brushes.Solid(Color.Black));
            canvas.Fill(brush);
        }

        using Image<RgbaHalfP> defaultImage = new(8, 8);
        RenderWithDefaultBackend(defaultImage, drawingOptions, Draw);

        using Image<RgbaHalfP> actual = RenderRecolorWithAssociatedWebGpuBackend<RgbaHalfP>(WebGPUTextureFormat.Rgba16Float, drawingOptions, Draw);

        RgbaHalfP expected = targetColor.ToPixel<RgbaHalfP>();
        Assert.Equal(expected, defaultImage[4, 4]);
        Assert.Equal(expected, actual[4, 4]);
    }

    [WebGPUFact]
    public void FillPath_WithRecolorBrush_RebasesPartitionAuxiliaryOffsets()
    {
        const int width = 32;
        const int height = 32;
        Color secondTarget = Color.FromScaledVector(new Vector4(0F, 0.4999F, 0F, 1F));
        Brush firstBrush = new RecolorBrush(Color.Black, Color.Red, 1F);
        Brush secondBrush = new RecolorBrush(Color.Black, secondTarget, 1F);
        DrawingOptions drawingOptions = CreateRecolorSourceOptions();
        RectanglePolygon left = new(0, 0, width / 2, height);
        RectanglePolygon right = new(width / 2, 0, width / 2, height);

        void Draw(DrawingCanvas canvas)
        {
            canvas.Fill(firstBrush, left);
            canvas.Fill(secondBrush, right);
        }

        using Image<Rgba32> defaultImage = new(width, height, Color.Black.ToPixel<Rgba32>());
        RenderWithDefaultBackend(defaultImage, drawingOptions, Draw);

        using WebGPUDrawingBackend backend = new();
        using WebGPURenderTarget renderTarget = new(WebGPUTextureFormat.Rgba8Unorm, PixelAlphaRepresentation.Unassociated, width, height);
        Configuration configuration = Configuration.Default.Clone();
        configuration.MaxDegreeOfParallelism = 2;
        configuration.SetDrawingBackend(backend);

        using Image<Rgba32> initialImage = new(width, height, Color.Black.ToPixel<Rgba32>());
        using (DrawingCanvas initialCanvas = WebGPUCanvasFactory.CreateCanvas(
                   configuration,
                   new DrawingOptions(),
                   backend,
                   renderTarget.Bounds,
                   renderTarget.Surface,
                   renderTarget.Surface.TargetDescriptor))
        {
            initialCanvas.DrawImage(initialImage, initialImage.Bounds, renderTarget.Bounds);
        }

        // Two commands, two target tile rows, and a parallelism limit of two force one Recolor
        // command into each encoder partition. The second command's local payload offset must be
        // rebased past the first partition's payload when the encoded streams are concatenated.
        using (DrawingCanvas canvas = WebGPUCanvasFactory.CreateCanvas(
                   configuration,
                   drawingOptions,
                   backend,
                   renderTarget.Bounds,
                   renderTarget.Surface,
                   renderTarget.Surface.TargetDescriptor))
        {
            Draw(canvas);
        }

        using Image<Rgba32> actual = renderTarget.ReadbackImage<Rgba32>();
        Rgba32 expectedSecond = new(0, 127, 0, 255);

        Assert.Equal(Color.Red.ToPixel<Rgba32>(), defaultImage[8, 16]);
        Assert.Equal(expectedSecond, defaultImage[24, 16]);
        Assert.Equal(defaultImage[8, 16], actual[8, 16]);
        Assert.Equal(defaultImage[24, 16], actual[24, 16]);
    }

    private static DrawingOptions CreateRecolorSourceOptions()
        => new()
        {
            GraphicsOptions = new GraphicsOptions
            {
                Antialias = false,
                AlphaCompositionMode = PixelAlphaCompositionMode.Src
            }
        };

    private static Image<TPixel> RenderRecolorWithAssociatedWebGpuBackend<TPixel>(WebGPUTextureFormat format, DrawingOptions drawingOptions, Action<DrawingCanvas> drawAction)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using WebGPUDrawingBackend backend = new();
        using WebGPURenderTarget renderTarget = new(format, PixelAlphaRepresentation.Associated, 8, 8);
        Configuration configuration = Configuration.Default.Clone();
        configuration.SetDrawingBackend(backend);

        using (DrawingCanvas canvas = WebGPUCanvasFactory.CreateCanvas(
                   configuration,
                   drawingOptions,
                   backend,
                   renderTarget.Bounds,
                   renderTarget.Surface,
                   renderTarget.Surface.TargetDescriptor))
        {
            drawAction(canvas);
        }

        return renderTarget.ReadbackImage<TPixel>();
    }
}
