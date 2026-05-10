// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Drawing.Tests.Processing;

public partial class DrawingCanvasTests
{
    [Fact]
    public void SaveCount_InitialValue_IsOne()
    {
        TestImageProvider<Rgba32> provider = TestImageProvider<Rgba32>.Blank(1, 1);
        using Image<Rgba32> target = new(64, 64);
        using DrawingCanvas<Rgba32> canvas = CreateCanvas(provider, target, new DrawingOptions());

        Assert.Equal(1, canvas.SaveCount);
    }

    [Fact]
    public void Save_IncrementsSaveCount()
    {
        TestImageProvider<Rgba32> provider = TestImageProvider<Rgba32>.Blank(1, 1);
        using Image<Rgba32> target = new(64, 64);
        using DrawingCanvas<Rgba32> canvas = CreateCanvas(provider, target, new DrawingOptions());

        Assert.Equal(1, canvas.SaveCount);

        int count1 = canvas.Save();
        Assert.Equal(2, count1);
        Assert.Equal(2, canvas.SaveCount);

        int count2 = canvas.Save();
        Assert.Equal(3, count2);
        Assert.Equal(3, canvas.SaveCount);
    }

    [Fact]
    public void SaveWithOptions_IncrementsSaveCount()
    {
        TestImageProvider<Rgba32> provider = TestImageProvider<Rgba32>.Blank(1, 1);
        using Image<Rgba32> target = new(64, 64);
        using DrawingCanvas<Rgba32> canvas = CreateCanvas(provider, target, new DrawingOptions());

        int count = canvas.Save(new DrawingOptions(), new RectanglePolygon(0, 0, 32, 32));
        Assert.Equal(2, count);
        Assert.Equal(2, canvas.SaveCount);
    }

    [Fact]
    public void Restore_DecrementsSaveCount()
    {
        TestImageProvider<Rgba32> provider = TestImageProvider<Rgba32>.Blank(1, 1);
        using Image<Rgba32> target = new(64, 64);
        using DrawingCanvas<Rgba32> canvas = CreateCanvas(provider, target, new DrawingOptions());

        _ = canvas.Save();
        _ = canvas.Save();
        Assert.Equal(3, canvas.SaveCount);

        canvas.Restore();
        Assert.Equal(2, canvas.SaveCount);

        canvas.Restore();
        Assert.Equal(1, canvas.SaveCount);
    }

    [Fact]
    public void Restore_AtRootState_DoesNotDecrementBelowOne()
    {
        TestImageProvider<Rgba32> provider = TestImageProvider<Rgba32>.Blank(1, 1);
        using Image<Rgba32> target = new(64, 64);
        using DrawingCanvas<Rgba32> canvas = CreateCanvas(provider, target, new DrawingOptions());

        Assert.Equal(1, canvas.SaveCount);

        canvas.Restore();
        Assert.Equal(1, canvas.SaveCount);

        canvas.Restore();
        Assert.Equal(1, canvas.SaveCount);
    }

    [Fact]
    public void RestoreTo_SetsSaveCountToSpecifiedLevel()
    {
        TestImageProvider<Rgba32> provider = TestImageProvider<Rgba32>.Blank(1, 1);
        using Image<Rgba32> target = new(64, 64);
        using DrawingCanvas<Rgba32> canvas = CreateCanvas(provider, target, new DrawingOptions());

        _ = canvas.Save();
        int mid = canvas.Save();
        _ = canvas.Save();
        _ = canvas.Save();
        Assert.Equal(5, canvas.SaveCount);

        canvas.RestoreTo(mid);
        Assert.Equal(mid, canvas.SaveCount);
    }

    [Fact]
    public void RestoreTo_One_RestoresToRoot()
    {
        TestImageProvider<Rgba32> provider = TestImageProvider<Rgba32>.Blank(1, 1);
        using Image<Rgba32> target = new(64, 64);
        using DrawingCanvas<Rgba32> canvas = CreateCanvas(provider, target, new DrawingOptions());

        _ = canvas.Save();
        _ = canvas.Save();
        _ = canvas.Save();

        canvas.RestoreTo(1);
        Assert.Equal(1, canvas.SaveCount);
    }

    [Fact]
    public void Save_ReturnValue_MatchesSaveCount()
    {
        TestImageProvider<Rgba32> provider = TestImageProvider<Rgba32>.Blank(1, 1);
        using Image<Rgba32> target = new(64, 64);
        using DrawingCanvas<Rgba32> canvas = CreateCanvas(provider, target, new DrawingOptions());

        for (int i = 0; i < 5; i++)
        {
            int returned = canvas.Save();
            Assert.Equal(canvas.SaveCount, returned);
        }
    }
}
