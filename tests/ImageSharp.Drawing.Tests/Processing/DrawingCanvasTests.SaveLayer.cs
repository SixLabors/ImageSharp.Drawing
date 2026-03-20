// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Tests.TestUtilities.ImageComparison;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Drawing.Tests.Processing;

public partial class DrawingCanvasTests
{
    [Fact]
    public void SaveLayer_IncrementsSaveCount()
    {
        TestImageProvider<Rgba32> provider = TestImageProvider<Rgba32>.Blank(1, 1);
        using Image<Rgba32> target = new(64, 64);
        using DrawingCanvas<Rgba32> canvas = CreateCanvas(provider, target, new DrawingOptions());

        Assert.Equal(1, canvas.SaveCount);

        int count = canvas.SaveLayer();
        Assert.Equal(2, count);
        Assert.Equal(2, canvas.SaveCount);
    }

    [Fact]
    public void SaveLayer_WithOptions_IncrementsSaveCount()
    {
        TestImageProvider<Rgba32> provider = TestImageProvider<Rgba32>.Blank(1, 1);
        using Image<Rgba32> target = new(64, 64);
        using DrawingCanvas<Rgba32> canvas = CreateCanvas(provider, target, new DrawingOptions());

        int count = canvas.SaveLayer(new GraphicsOptions { BlendPercentage = 0.5f });
        Assert.Equal(2, count);
        Assert.Equal(2, canvas.SaveCount);
    }

    [Fact]
    public void SaveLayer_WithBounds_IncrementsSaveCount()
    {
        TestImageProvider<Rgba32> provider = TestImageProvider<Rgba32>.Blank(1, 1);
        using Image<Rgba32> target = new(64, 64);
        using DrawingCanvas<Rgba32> canvas = CreateCanvas(provider, target, new DrawingOptions());

        int count = canvas.SaveLayer(new GraphicsOptions(), new Rectangle(10, 10, 32, 32));
        Assert.Equal(2, count);
        Assert.Equal(2, canvas.SaveCount);
    }

    [Fact]
    public void SaveLayer_Restore_DecrementsSaveCount()
    {
        TestImageProvider<Rgba32> provider = TestImageProvider<Rgba32>.Blank(1, 1);
        using Image<Rgba32> target = new(64, 64);
        using DrawingCanvas<Rgba32> canvas = CreateCanvas(provider, target, new DrawingOptions());

        canvas.SaveLayer();
        Assert.Equal(2, canvas.SaveCount);

        canvas.Restore();
        Assert.Equal(1, canvas.SaveCount);
    }

    [Fact]
    public void SaveLayer_RestoreTo_DecrementsSaveCount()
    {
        TestImageProvider<Rgba32> provider = TestImageProvider<Rgba32>.Blank(1, 1);
        using Image<Rgba32> target = new(64, 64);
        using DrawingCanvas<Rgba32> canvas = CreateCanvas(provider, target, new DrawingOptions());

        int before = canvas.SaveCount;
        canvas.SaveLayer();
        canvas.Save();
        Assert.Equal(3, canvas.SaveCount);

        canvas.RestoreTo(before);
        Assert.Equal(before, canvas.SaveCount);
    }

    [Fact]
    public void SaveLayer_DrawAndRestore_CompositesLayerOntoTarget()
    {
        TestImageProvider<Rgba32> provider = TestImageProvider<Rgba32>.Blank(1, 1);
        using Image<Rgba32> target = new(64, 64);
        using (DrawingCanvas<Rgba32> canvas = CreateCanvas(provider, target, new DrawingOptions()))
        {
            // Fill background white.
            canvas.Fill(new SolidBrush(Color.White));

            // SaveLayer with full opacity, draw red rectangle, then restore.
            canvas.SaveLayer();
            canvas.Fill(new SolidBrush(Color.Red), new RectangularPolygon(10, 10, 20, 20));
            canvas.Restore();
        }

        // The red rectangle should be composited onto the white background.
        Rgba32 center = target[20, 20];
        Assert.Equal(new Rgba32(255, 0, 0, 255), center);

        // Outside the filled region should remain white.
        Rgba32 corner = target[0, 0];
        Assert.Equal(new Rgba32(255, 255, 255, 255), corner);
    }

    [Fact]
    public void SaveLayer_WithHalfOpacity_CompositesWithBlend()
    {
        TestImageProvider<Rgba32> provider = TestImageProvider<Rgba32>.Blank(1, 1);
        using Image<Rgba32> target = new(64, 64);
        using (DrawingCanvas<Rgba32> canvas = CreateCanvas(provider, target, new DrawingOptions()))
        {
            // Fill background white.
            canvas.Fill(new SolidBrush(Color.White));

            // SaveLayer with 50% opacity, draw red rectangle, then restore.
            canvas.SaveLayer(new GraphicsOptions { BlendPercentage = 0.5f });
            canvas.Fill(new SolidBrush(Color.Red), new RectangularPolygon(10, 10, 20, 20));
            canvas.Restore();
        }

        // The red should be blended at ~50% onto white, giving approximately (255, 128, 128).
        Rgba32 center = target[20, 20];
        Assert.InRange(center.R, 120, 255);
        Assert.InRange(center.G, 100, 140);
        Assert.InRange(center.B, 100, 140);
    }

    [Fact]
    public void SaveLayer_Dispose_CompositesActiveLayer()
    {
        TestImageProvider<Rgba32> provider = TestImageProvider<Rgba32>.Blank(1, 1);
        using Image<Rgba32> target = new(64, 64);

        // Create canvas, push a layer, draw, and dispose without explicit Restore.
        using (DrawingCanvas<Rgba32> canvas = CreateCanvas(provider, target, new DrawingOptions()))
        {
            canvas.Fill(new SolidBrush(Color.White));
            canvas.SaveLayer();
            canvas.Fill(new SolidBrush(Color.Blue), new RectangularPolygon(0, 0, 32, 32));

            // Dispose should composite the layer.
        }

        // After dispose, the blue fill should be visible.
        Rgba32 pixel = target[16, 16];
        Assert.Equal(new Rgba32(0, 0, 255, 255), pixel);
    }

    [Fact]
    public void SaveLayer_Dispose_UsesStoredLayerOptions()
    {
        TestImageProvider<Rgba32> provider = TestImageProvider<Rgba32>.Blank(1, 1);
        using Image<Rgba32> restoredTarget = new(64, 64);
        using Image<Rgba32> disposedTarget = new(64, 64);

        using (DrawingCanvas<Rgba32> canvas = CreateCanvas(provider, restoredTarget, new DrawingOptions()))
        {
            canvas.Fill(new SolidBrush(Color.White));
            canvas.SaveLayer(new GraphicsOptions { BlendPercentage = 0.5f });
            canvas.Fill(new SolidBrush(Color.Blue), new RectangularPolygon(0, 0, 32, 32));
            canvas.Restore();
        }

        using (DrawingCanvas<Rgba32> canvas = CreateCanvas(provider, disposedTarget, new DrawingOptions()))
        {
            canvas.Fill(new SolidBrush(Color.White));
            canvas.SaveLayer(new GraphicsOptions { BlendPercentage = 0.5f });
            canvas.Fill(new SolidBrush(Color.Blue), new RectangularPolygon(0, 0, 32, 32));
        }

        ImageComparer.Exact.VerifySimilarity(restoredTarget, disposedTarget);
    }

    [Fact]
    public void SaveLayer_NestedLayers_CompositeCorrectly()
    {
        TestImageProvider<Rgba32> provider = TestImageProvider<Rgba32>.Blank(1, 1);
        using Image<Rgba32> target = new(64, 64);
        using (DrawingCanvas<Rgba32> canvas = CreateCanvas(provider, target, new DrawingOptions()))
        {
            canvas.Fill(new SolidBrush(Color.White));

            // Outer layer.
            canvas.SaveLayer();
            canvas.Fill(new SolidBrush(Color.Red), new RectangularPolygon(0, 0, 64, 64));

            // Inner layer.
            canvas.SaveLayer();
            canvas.Fill(new SolidBrush(Color.Blue), new RectangularPolygon(16, 16, 32, 32));
            canvas.Restore(); // Closes blue onto red.

            canvas.Restore(); // Closes red+blue onto white.
        }

        // Center should be blue (inner layer overwrites outer).
        Rgba32 center = target[32, 32];
        Assert.Equal(new Rgba32(0, 0, 255, 255), center);

        // Corner should be red (outer layer only).
        Rgba32 corner = target[5, 5];
        Assert.Equal(new Rgba32(255, 0, 0, 255), corner);
    }

    [Fact]
    public void SaveLayer_MixedSaveAndSaveLayer_WorksCorrectly()
    {
        TestImageProvider<Rgba32> provider = TestImageProvider<Rgba32>.Blank(1, 1);
        using Image<Rgba32> target = new(64, 64);
        using (DrawingCanvas<Rgba32> canvas = CreateCanvas(provider, target, new DrawingOptions()))
        {
            canvas.Fill(new SolidBrush(Color.White));

            canvas.Save();              // SaveCount = 2 (plain save)
            canvas.SaveLayer();         // SaveCount = 3 (layer)
            canvas.Save();              // SaveCount = 4 (plain save)
            Assert.Equal(4, canvas.SaveCount);

            canvas.Fill(new SolidBrush(Color.Green), new RectangularPolygon(0, 0, 64, 64));

            // RestoreTo(1) should pop all states including the layer.
            canvas.RestoreTo(1);
            Assert.Equal(1, canvas.SaveCount);
        }

        Rgba32 pixel = target[32, 32];
        Assert.Equal(new Rgba32(0, 128, 0, 255), pixel);
    }
}
