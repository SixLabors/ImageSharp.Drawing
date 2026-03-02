// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace SixLabors.ImageSharp.Drawing.Tests.Processing;

[GroupOutput("Drawing")]
public class DrawingCanvasDrawImageTests
{
    [Theory]
    [WithBasicTestPatternImages(384, 256, PixelTypes.Rgba32)]
    public void DrawImage_WithRotationTransform_MatchesReference<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using Image<TPixel> foreground = provider.GetImage();
        using Image<TPixel> target = new(384, 256);

        DrawingOptions options = new()
        {
            Transform = Matrix3x2.CreateRotation(MathF.PI / 4F, new Vector2(192F, 128F))
        };

        using DrawingCanvas<TPixel> canvas = new(
            provider.Configuration,
            new Buffer2DRegion<TPixel>(target.Frames.RootFrame.PixelBuffer, target.Bounds),
            options);

        canvas.Clear(Brushes.Solid(Color.White));
        canvas.DrawImage(
            foreground,
            foreground.Bounds,
            new RectangleF(72, 48, 240, 160),
            KnownResamplers.NearestNeighbor);
        canvas.Flush();

        target.DebugSave(provider, appendSourceFileOrDescription: false);
        target.CompareToReferenceOutput(provider, appendSourceFileOrDescription: false);
    }
}
