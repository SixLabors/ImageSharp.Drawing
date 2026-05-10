// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace SixLabors.ImageSharp.Drawing.Tests.Processing;

public class ProcessWithCanvasExtensionsTests
{
    [Fact]
    public void ProcessWithCanvas_Mutate_AppliesToAllFrames()
    {
        using Image<Rgba32> image = new(24, 16);
        image.Frames.AddFrame(image.Frames.RootFrame);

        image.Mutate(ctx => ctx.Paint(canvas => canvas.Clear(Brushes.Solid(Color.OrangeRed))));

        Assert.Equal(Color.OrangeRed.ToPixel<Rgba32>(), image.Frames.RootFrame[8, 6]);
        Assert.Equal(Color.OrangeRed.ToPixel<Rgba32>(), image.Frames[1][8, 6]);
    }

    [Fact]
    public void ProcessWithCanvas_Clone_AppliesToAllFrames_WithoutMutatingSource()
    {
        using Image<Rgba32> source = new(24, 16);
        source.Frames.AddFrame(source.Frames.RootFrame);
        source.Mutate(ctx => ctx.Paint(canvas => canvas.Clear(Brushes.Solid(Color.White))));

        using Image<Rgba32> clone = source.Clone(
            ctx => ctx.Paint(canvas => canvas.Clear(Brushes.Solid(Color.MediumPurple))));

        Assert.Equal(Color.White.ToPixel<Rgba32>(), source.Frames.RootFrame[8, 6]);
        Assert.Equal(Color.White.ToPixel<Rgba32>(), source.Frames[1][8, 6]);
        Assert.Equal(Color.MediumPurple.ToPixel<Rgba32>(), clone.Frames.RootFrame[8, 6]);
        Assert.Equal(Color.MediumPurple.ToPixel<Rgba32>(), clone.Frames[1][8, 6]);
    }

    [Fact]
    public void ProcessWithCanvas_Mutate_DrawImage_AppliesToAllFrames()
    {
        using Image<Rgba32> image = new(24, 16);
        image.Frames.AddFrame(image.Frames.RootFrame);

        using Image<Bgra32> source = new(8, 8, Color.HotPink.ToPixel<Bgra32>());

        Rectangle sourceRect = new(2, 1, 4, 5);
        RectangleF destinationRect = new(6, 4, 10, 6);

        image.Mutate(ctx => ctx.Paint(canvas =>
        {
            canvas.Clear(Brushes.Solid(Color.White));
            canvas.DrawImage(source, sourceRect, destinationRect);
        }));

        Rgba32 expectedFill = Color.HotPink.ToPixel<Rgba32>();
        Rgba32 expectedBackground = Color.White.ToPixel<Rgba32>();

        Assert.Equal(expectedFill, image.Frames.RootFrame[10, 6]);
        Assert.Equal(expectedFill, image.Frames[1][10, 6]);
        Assert.Equal(expectedBackground, image.Frames.RootFrame[1, 1]);
        Assert.Equal(expectedBackground, image.Frames[1][1, 1]);
    }
}
