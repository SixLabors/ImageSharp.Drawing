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

        using (DrawingCanvas canvas = image.Frames.RootFrame.CreateCanvas(image.Configuration, new DrawingOptions()))
        {
            canvas.Clear(Brushes.Solid(Color.SeaGreen));
            canvas.Flush();
        }

        Assert.Equal(Color.SeaGreen.ToPixel<Rgba32>(), image[12, 10]);
    }
}
