// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Drawing.Tests.Processing;

public partial class DrawingCanvasTests
{
    [Fact]
    public void FromFrame_TargetsProvidedFrame()
    {
        using Image<Rgba32> image = new(48, 36);

        using (DrawingCanvas<Rgba32> canvas = DrawingCanvas<Rgba32>.FromFrame(
                   image.Frames.RootFrame,
                   new DrawingOptions()))
        {
            canvas.Clear(Brushes.Solid(Color.SeaGreen));
            canvas.Flush();
        }

        Assert.Equal(Color.SeaGreen.ToPixel<Rgba32>(), image[12, 10]);
    }

    [Fact]
    public void FromImage_TargetsRequestedFrame()
    {
        using Image<Rgba32> image = new(40, 30);
        image.Frames.AddFrame(image.Frames.RootFrame);

        using (DrawingCanvas<Rgba32> rootCanvas = DrawingCanvas<Rgba32>.FromRootFrame(image, new DrawingOptions()))
        {
            rootCanvas.Clear(Brushes.Solid(Color.White));
            rootCanvas.Flush();
        }

        using (DrawingCanvas<Rgba32> secondCanvas = DrawingCanvas<Rgba32>.FromImage(image, 1, new DrawingOptions()))
        {
            secondCanvas.Clear(Brushes.Solid(Color.MediumPurple));
            secondCanvas.Flush();
        }

        Assert.Equal(Color.White.ToPixel<Rgba32>(), image.Frames.RootFrame[8, 8]);
        Assert.Equal(Color.MediumPurple.ToPixel<Rgba32>(), image.Frames[1][8, 8]);
    }

    [Fact]
    public void FromImage_InvalidFrameIndex_Throws()
    {
        using Image<Rgba32> image = new(20, 20);
        image.Frames.AddFrame(image.Frames.RootFrame);

        ArgumentOutOfRangeException low = Assert.Throws<ArgumentOutOfRangeException>(
            () => DrawingCanvas<Rgba32>.FromImage(image, -1, new DrawingOptions()));
        ArgumentOutOfRangeException high = Assert.Throws<ArgumentOutOfRangeException>(
            () => DrawingCanvas<Rgba32>.FromImage(image, image.Frames.Count, new DrawingOptions()));

        Assert.Equal("frameIndex", low.ParamName);
        Assert.Equal("frameIndex", high.ParamName);
    }
}
