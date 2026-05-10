// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Tests.TestUtilities.ImageComparison;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace SixLabors.ImageSharp.Drawing.Tests.Processing;

public partial class DrawingCanvasTests
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
            Transform = new Matrix4x4(Matrix3x2.CreateRotation(MathF.PI / 4F, new Vector2(192F, 128F)))
        };

        using (DrawingCanvas<TPixel> canvas = CreateCanvas(provider, target, options))
        {
            canvas.Clear(Brushes.Solid(Color.White));
            canvas.DrawImage(
                foreground,
                foreground.Bounds,
                new RectangleF(72, 48, 240, 160),
                KnownResamplers.NearestNeighbor);
        }

        target.DebugSave(provider, appendSourceFileOrDescription: false);

        // Ubunut with .NET10 has some minor difference due to nearest neightbor resampling, so we need to use a tolerant comparer here.
        target.CompareToReferenceOutput(ImageComparer.TolerantPercentage(0.0080F), provider, appendSourceFileOrDescription: false);
    }

    [Theory]
    [WithBasicTestPatternImages(320, 220, PixelTypes.Rgba32)]
    public void DrawImage_WithSourceClippingAndScaling_MatchesReference<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using Image<TPixel> foreground = provider.GetImage();
        using Image<TPixel> target = new(320, 220);
        using (DrawingCanvas<TPixel> canvas = CreateCanvas(provider, target, new DrawingOptions()))
        {
            canvas.Clear(Brushes.Solid(Color.White));
            canvas.DrawImage(
                foreground,
                new Rectangle(-48, 18, 196, 148),
                new RectangleF(18, 20, 170, 120),
                KnownResamplers.Bicubic);
            canvas.DrawImage(
                foreground,
                new Rectangle(220, 100, 160, 140),
                new RectangleF(170, 72, 130, 110),
                KnownResamplers.NearestNeighbor);
            canvas.Draw(Pens.Solid(Color.Black, 3), new Rectangle(8, 8, 304, 204));
        }

        target.DebugSave(provider, appendSourceFileOrDescription: false);
        target.CompareToReferenceOutput(provider, appendSourceFileOrDescription: false);
    }

    [Theory]
    [WithBasicTestPatternImages(360, 240, PixelTypes.Rgba32)]
    public void DrawImage_WithClipPathAndTransform_MatchesReference<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using Image<TPixel> foreground = provider.GetImage();
        using Image<TPixel> target = new(360, 240);

        DrawingOptions transformedOptions = new()
        {
            Transform = new Matrix4x4(Matrix3x2.CreateRotation(0.32F, new Vector2(180, 120)))
        };

        IPath clipPath = new EllipsePolygon(new PointF(180, 120), new SizeF(208, 126));

        using (DrawingCanvas<TPixel> canvas = CreateCanvas(provider, target, new DrawingOptions()))
        {
            canvas.Clear(Brushes.Solid(Color.White));
            canvas.Fill(Brushes.Solid(Color.LightGray.WithAlpha(0.45F)), new Rectangle(18, 16, 324, 208));

            _ = canvas.Save(transformedOptions, clipPath);
            canvas.DrawImage(
                foreground,
                new Rectangle(10, 8, 234, 180),
                new RectangleF(64, 36, 232, 164),
                KnownResamplers.Bicubic);
            canvas.DrawImage(
                foreground,
                new Rectangle(102, 32, 196, 166),
                new RectangleF(92, 58, 210, 148),
                KnownResamplers.NearestNeighbor);
            canvas.Restore();

            canvas.Draw(Pens.DashDot(Color.DarkSlateGray, 3F), clipPath);
            canvas.Draw(Pens.Solid(Color.Black, 2F), new Rectangle(8, 8, 344, 224));
        }

        target.DebugSave(provider, appendSourceFileOrDescription: false);
        target.CompareToReferenceOutput(provider, appendSourceFileOrDescription: false);
    }
}
