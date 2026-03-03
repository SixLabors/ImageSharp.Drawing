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

        image.Mutate(ctx => ctx.ProcessWithCanvas<Rgba32>(canvas => canvas.Clear(Brushes.Solid(Color.OrangeRed))));

        Assert.Equal(Color.OrangeRed.ToPixel<Rgba32>(), image.Frames.RootFrame[8, 6]);
        Assert.Equal(Color.OrangeRed.ToPixel<Rgba32>(), image.Frames[1][8, 6]);
    }

    [Fact]
    public void ProcessWithCanvas_Clone_AppliesToAllFrames_WithoutMutatingSource()
    {
        using Image<Rgba32> source = new(24, 16);
        source.Frames.AddFrame(source.Frames.RootFrame);
        source.Mutate(ctx => ctx.ProcessWithCanvas<Rgba32>(canvas => canvas.Clear(Brushes.Solid(Color.White))));

        using Image<Rgba32> clone = source.Clone(
            ctx => ctx.ProcessWithCanvas<Rgba32>(canvas => canvas.Clear(Brushes.Solid(Color.MediumPurple))));

        Assert.Equal(Color.White.ToPixel<Rgba32>(), source.Frames.RootFrame[8, 6]);
        Assert.Equal(Color.White.ToPixel<Rgba32>(), source.Frames[1][8, 6]);
        Assert.Equal(Color.MediumPurple.ToPixel<Rgba32>(), clone.Frames.RootFrame[8, 6]);
        Assert.Equal(Color.MediumPurple.ToPixel<Rgba32>(), clone.Frames[1][8, 6]);
    }

    [Fact]
    public void ProcessWithCanvas_WhenPixelTypeMismatch_Throws()
    {
        using Image<Rgba32> image = new(12, 12);

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
            () => image.Mutate(ctx => ctx.ProcessWithCanvas<Bgra32>(canvas => canvas.Clear(Brushes.Solid(Color.Black)))));

        Assert.Contains("expects pixel type", ex.Message);
    }
}
