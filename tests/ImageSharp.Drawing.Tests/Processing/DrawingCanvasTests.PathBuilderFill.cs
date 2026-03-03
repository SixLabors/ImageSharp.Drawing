// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Drawing.Tests.Processing;

public partial class DrawingCanvasTests
{
    [Theory]
    [WithBlankImage(192, 128, PixelTypes.Rgba32)]
    public void Fill_PathBuilder_MatchesReference<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using Image<TPixel> target = provider.GetImage();

        DrawingOptions options = new()
        {
            Transform = Matrix3x2.CreateRotation(0.2F, new Vector2(96F, 64F))
        };

        using DrawingCanvas<TPixel> canvas = CreateCanvas(provider, target, options);
        PathBuilder pathBuilder = CreateClosedPathBuilder();

        canvas.Clear(Brushes.Solid(Color.White));
        canvas.Fill(pathBuilder, Brushes.Solid(Color.DeepPink.WithAlpha(0.85F)));
        canvas.Flush();

        target.DebugSave(provider, appendSourceFileOrDescription: false);
        target.CompareToReferenceOutput(provider, appendSourceFileOrDescription: false);
    }
}
