// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace SixLabors.ImageSharp.Drawing.Tests.Processing;

public partial class ProcessWithDrawingCanvasTests
{
    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    [WithBlankImage(7, 4, PixelTypes.Rgba32)]
    [WithBlankImage(16, 7, PixelTypes.Rgba32)]
    [WithBlankImage(33, 32, PixelTypes.Rgba32)]
    [WithBlankImage(400, 500, PixelTypes.Rgba32)]
    public void Clear_DoesNotDependOnSize<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using Image<TPixel> image = provider.GetImage();
        Color color = Color.HotPink;
        DrawingOptions options = new();

        image.Mutate(ctx => ctx.ProcessWithCanvas(options, canvas => canvas.Clear(Brushes.Solid(color))));

        image.DebugSave(provider, appendPixelTypeToFileName: false);
        image.ComparePixelBufferTo(color);
    }

    [Theory]
    [WithBlankImage(16, 16, PixelTypes.Rgba32 | PixelTypes.Argb32 | PixelTypes.RgbaVector)]
    public void Clear_DoesNotDependOnSinglePixelType<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using Image<TPixel> image = provider.GetImage();
        Color color = Color.HotPink;
        DrawingOptions options = new();

        image.Mutate(ctx => ctx.ProcessWithCanvas(options, canvas => canvas.Clear(Brushes.Solid(color))));

        image.DebugSave(provider, appendSourceFileOrDescription: false);
        image.ComparePixelBufferTo(color);
    }

    [Theory]
    [WithSolidFilledImages(16, 16, "Red", PixelTypes.Rgba32, "Blue")]
    [WithSolidFilledImages(16, 16, "Yellow", PixelTypes.Rgba32, "Khaki")]
    public void Clear_WhenColorIsOpaque_OverridePreviousColor<TPixel>(
        TestImageProvider<TPixel> provider,
        string newColorName)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using Image<TPixel> image = provider.GetImage();
        Color color = TestUtils.GetColorByName(newColorName);
        DrawingOptions options = new();

        image.Mutate(ctx => ctx.ProcessWithCanvas(options, canvas => canvas.Clear(Brushes.Solid(color))));

        image.DebugSave(
            provider,
            newColorName,
            appendPixelTypeToFileName: false,
            appendSourceFileOrDescription: false);
        image.ComparePixelBufferTo(color);
    }

    [Theory]
    [WithSolidFilledImages(16, 16, "Red", PixelTypes.Rgba32, "Blue")]
    [WithSolidFilledImages(16, 16, "Yellow", PixelTypes.Rgba32, "Khaki")]
    public void Clear_AlwaysOverridesPreviousColor<TPixel>(
        TestImageProvider<TPixel> provider,
        string newColorName)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using Image<TPixel> image = provider.GetImage();
        Color color = TestUtils.GetColorByName(newColorName).WithAlpha(0.5F);
        DrawingOptions options = new();

        image.Mutate(ctx => ctx.ProcessWithCanvas(options, canvas => canvas.Clear(Brushes.Solid(color))));

        image.DebugSave(
            provider,
            newColorName,
            appendPixelTypeToFileName: false,
            appendSourceFileOrDescription: false);
        image.ComparePixelBufferTo(color);
    }

    [Theory]
    [WithSolidFilledImages(16, 16, "Red", PixelTypes.Rgba32, 5, 7, 3, 8)]
    [WithSolidFilledImages(16, 16, "Red", PixelTypes.Rgba32, 8, 5, 6, 4)]
    public void Clear_Region<TPixel>(TestImageProvider<TPixel> provider, int x0, int y0, int w, int h)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using Image<TPixel> image = provider.GetImage();
        Color clearColor = Color.Blue;
        Color backgroundColor = Color.Red;
        Rectangle region = new(x0, y0, w, h);
        DrawingOptions options = new();

        image.Mutate(ctx => ctx.ProcessWithCanvas(options, canvas => canvas.Clear(Brushes.Solid(clearColor), region)));

        image.DebugSave(provider, $"(x{x0},y{y0},w{w},h{h})", appendPixelTypeToFileName: false);
        AssertRegionFill(image, region, clearColor, backgroundColor);
    }

    [Theory]
    [WithSolidFilledImages(16, 16, "Red", PixelTypes.Rgba32, 5, 7, 3, 8)]
    [WithSolidFilledImages(16, 16, "Red", PixelTypes.Rgba32, 8, 5, 6, 4)]
    public void Clear_Region_WorksOnWrappedMemoryImage<TPixel>(
        TestImageProvider<TPixel> provider,
        int x0,
        int y0,
        int w,
        int h)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using Image<TPixel> source = provider.GetImage();
        Assert.True(source.DangerousTryGetSinglePixelMemory(out Memory<TPixel> sourcePixels));
        TestMemoryManager<TPixel> memoryManager = TestMemoryManager<TPixel>.CreateAsCopyOf(sourcePixels.Span);
        using Image<TPixel> wrapped = Image.WrapMemory(memoryManager.Memory, source.Width, source.Height);

        Color clearColor = Color.Blue;
        Color backgroundColor = Color.Red;
        Rectangle region = new(x0, y0, w, h);
        DrawingOptions options = new();

        wrapped.Mutate(ctx => ctx.ProcessWithCanvas(options, canvas => canvas.Clear(Brushes.Solid(clearColor), region)));

        wrapped.DebugSave(provider, $"(x{x0},y{y0},w{w},h{h})", appendPixelTypeToFileName: false);
        AssertRegionFill(wrapped, region, clearColor, backgroundColor);
    }

    private static void AssertRegionFill<TPixel>(
        Image<TPixel> image,
        Rectangle region,
        Color inside,
        Color outside)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        TPixel insidePixel = inside.ToPixel<TPixel>();
        TPixel outsidePixel = outside.ToPixel<TPixel>();
        Buffer2D<TPixel> buffer = image.Frames.RootFrame.PixelBuffer;

        for (int y = 0; y < image.Height; y++)
        {
            Span<TPixel> row = buffer.DangerousGetRowSpan(y);
            for (int x = 0; x < image.Width; x++)
            {
                TPixel expected = region.Contains(x, y) ? insidePixel : outsidePixel;
                Assert.Equal(expected, row[x]);
            }
        }
    }
}
