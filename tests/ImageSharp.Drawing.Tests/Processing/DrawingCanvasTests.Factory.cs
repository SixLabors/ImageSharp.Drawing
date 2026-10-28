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

    [Fact]
    public void CreateScene_KeepsScenesRecordedBeforeItInOrder()
    {
        using Image<Rgba32> sourceImage = new(32, 16);
        using Image<Rgba32> target = new(32, 16);

        using (DrawingCanvas sourceCanvas = sourceImage.Frames.RootFrame.CreateCanvas(sourceImage.Configuration, new DrawingOptions()))
        {
            sourceCanvas.Fill(Brushes.Solid(Color.Red), new Rectangle(4, 4, 8, 8));
            using DrawingBackendScene inserted = sourceCanvas.CreateScene();

            // Green is recorded before the inserted red scene and blue after it. Both overlap the red
            // square, so the colors in the overlaps show the order of the three parts.
            sourceCanvas.Fill(Brushes.Solid(Color.Green), new Rectangle(0, 4, 6, 8));
            sourceCanvas.RenderScene(inserted);
            sourceCanvas.Fill(Brushes.Solid(Color.Blue), new Rectangle(10, 4, 8, 8));
            using DrawingBackendScene combined = sourceCanvas.CreateScene();

            using (DrawingCanvas targetCanvas = target.Frames.RootFrame.CreateCanvas(target.Configuration, new DrawingOptions()))
            {
                targetCanvas.RenderScene(combined);
            }
        }

        Assert.Equal(Color.Green.ToPixel<Rgba32>(), target[2, 8]);
        Assert.Equal(Color.Red.ToPixel<Rgba32>(), target[5, 8]);
        Assert.Equal(Color.Red.ToPixel<Rgba32>(), target[8, 8]);
        Assert.Equal(Color.Blue.ToPixel<Rgba32>(), target[11, 8]);
        Assert.Equal(Color.Blue.ToPixel<Rgba32>(), target[16, 8]);

        // The recorded work went into the scenes, so the source canvas drew nothing on disposal.
        Assert.Equal(default, sourceImage[8, 8]);
    }
}
