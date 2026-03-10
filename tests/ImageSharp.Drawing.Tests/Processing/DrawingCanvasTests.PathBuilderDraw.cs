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
    public void Draw_PathBuilder_MatchesReference<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using Image<TPixel> target = provider.GetImage();

        DrawingOptions options = new()
        {
            Transform = new Matrix4x4(Matrix3x2.CreateRotation(-0.15F, new Vector2(96F, 64F)))
        };

        using DrawingCanvas<TPixel> canvas = CreateCanvas(provider, target, options);
        PathBuilder pathBuilder = CreateOpenPathBuilder();

        canvas.Clear(Brushes.Solid(Color.White));
        canvas.Draw(Pens.Solid(Color.CornflowerBlue, 6F), pathBuilder);
        canvas.Flush();

        target.DebugSave(provider, appendSourceFileOrDescription: false);
        target.CompareToReferenceOutput(provider, appendSourceFileOrDescription: false);
    }
}
