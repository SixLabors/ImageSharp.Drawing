// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Processing.Backends;
using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.PixelFormats;
using Xunit.Abstractions;

namespace SixLabors.ImageSharp.Drawing.Tests.Processing;

[GroupOutput("Drawing")]
public partial class DrawingCanvasTests
{
    private readonly ITestOutputHelper output;

    public DrawingCanvasTests(ITestOutputHelper output)
    {
        this.output = output;
    }

    private static DrawingCanvas<TPixel> CreateCanvas<TPixel>(
        TestImageProvider<TPixel> provider,
        Image<TPixel> image,
        DrawingOptions options)
        where TPixel : unmanaged, IPixel<TPixel>
        => new(
            provider.Configuration,
            image.Frames.RootFrame.PixelBuffer.GetRegion(),
            options);

    private static PathBuilder CreateClosedPathBuilder()
    {
        PathBuilder pathBuilder = new();
        pathBuilder.AddLine(22, 24, 124, 30);
        pathBuilder.AddLine(124, 30, 168, 98);
        pathBuilder.AddLine(168, 98, 40, 108);
        pathBuilder.AddLine(40, 108, 22, 24);
        pathBuilder.CloseAllFigures();
        return pathBuilder;
    }

    private static PathBuilder CreateOpenPathBuilder()
    {
        PathBuilder pathBuilder = new();
        pathBuilder.AddLine(20, 98, 54, 22);
        pathBuilder.AddLine(54, 22, 114, 76);
        pathBuilder.AddLine(114, 76, 170, 26);
        return pathBuilder;
    }
}
