// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Processing.Backends;
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

    [Fact]
    public void CreateScene_CanRenderRetainedCommandsRepeatedly()
    {
        using Image<Rgba32> sceneSource = new(32, 24);
        using Image<Rgba32> firstTarget = new(32, 24);
        using Image<Rgba32> secondTarget = new(32, 24);

        using DrawingCanvas sceneCanvas = sceneSource.Frames.RootFrame.CreateCanvas(sceneSource.Configuration, new DrawingOptions());
        sceneCanvas.Fill(Brushes.Solid(Color.Red), new Rectangle(8, 6, 16, 12));

        using DrawingBackendScene scene = sceneCanvas.CreateScene();

        using (DrawingCanvas firstCanvas = firstTarget.Frames.RootFrame.CreateCanvas(firstTarget.Configuration, new DrawingOptions()))
        {
            firstCanvas.Fill(Brushes.Solid(Color.Blue));
            firstCanvas.RenderScene(scene);
        }

        using (DrawingCanvas secondCanvas = secondTarget.Frames.RootFrame.CreateCanvas(secondTarget.Configuration, new DrawingOptions()))
        {
            secondCanvas.RenderScene(scene);
        }

        Assert.Equal(default, sceneSource[12, 10]);
        Assert.Equal(Color.Red.ToPixel<Rgba32>(), firstTarget[12, 10]);
        Assert.Equal(Color.Blue.ToPixel<Rgba32>(), firstTarget[1, 1]);
        Assert.Equal(Color.Red.ToPixel<Rgba32>(), secondTarget[12, 10]);
    }
}
