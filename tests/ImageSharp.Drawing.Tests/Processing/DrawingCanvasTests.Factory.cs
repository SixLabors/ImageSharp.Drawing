// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Drawing.Tests.Processing;

public partial class DrawingCanvasTests
{
    [Fact]
    public void CreateCanvas_FromFrame_TargetsProvidedFrame()
    {
        using Image<Rgba32> image = new(48, 36);

        using (DrawingCanvas<Rgba32> canvas = image.Frames.RootFrame.CreateCanvas(
                   new DrawingOptions()))
        {
            canvas.Clear(Brushes.Solid(Color.SeaGreen));
            canvas.Flush();
        }

        Assert.Equal(Color.SeaGreen.ToPixel<Rgba32>(), image[12, 10]);
    }

    [Fact]
    public void CreateCanvas_FromImage_TargetsRequestedFrame()
    {
        using Image<Rgba32> image = new(40, 30);
        image.Frames.AddFrame(image.Frames.RootFrame);

        using (DrawingCanvas<Rgba32> rootCanvas = image.CreateCanvas(new DrawingOptions()))
        {
            rootCanvas.Clear(Brushes.Solid(Color.White));
            rootCanvas.Flush();
        }

        using (DrawingCanvas<Rgba32> secondCanvas = image.CreateCanvas(new DrawingOptions(), 1))
        {
            secondCanvas.Clear(Brushes.Solid(Color.MediumPurple));
            secondCanvas.Flush();
        }

        Assert.Equal(Color.White.ToPixel<Rgba32>(), image.Frames.RootFrame[8, 8]);
        Assert.Equal(Color.MediumPurple.ToPixel<Rgba32>(), image.Frames[1][8, 8]);
    }

    [Fact]
    public void CreateCanvas_FromImage_InvalidFrameIndex_Throws()
    {
        using Image<Rgba32> image = new(20, 20);
        image.Frames.AddFrame(image.Frames.RootFrame);

        ArgumentOutOfRangeException low = Assert.Throws<ArgumentOutOfRangeException>(
            () => image.CreateCanvas(new DrawingOptions(), -1));
        ArgumentOutOfRangeException high = Assert.Throws<ArgumentOutOfRangeException>(
            () => image.CreateCanvas(new DrawingOptions(), image.Frames.Count));

        Assert.Equal("frameIndex", low.ParamName);
        Assert.Equal("frameIndex", high.ParamName);
    }
}
