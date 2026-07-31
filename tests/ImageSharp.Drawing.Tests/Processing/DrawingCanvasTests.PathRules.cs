// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Tests.TestUtilities.ImageComparison;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Drawing.Tests.Processing;

public partial class DrawingCanvasTests
{
    [Theory]
    [WithBlankImage(360, 220, PixelTypes.Rgba32)]
    public void Fill_SelfIntersectingPath_EvenOddVsNonZero_MatchesReference<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using Image<TPixel> target = provider.GetImage();
        IPath leftPath = CreatePentagramPath(new PointF(96, 110), 78F);
        IPath rightPath = CreatePentagramPath(new PointF(264, 110), 78F);

        DrawingOptions evenOddOptions = new()
        {
            IntersectionRule = IntersectionRule.EvenOdd
        };

        DrawingOptions nonZeroOptions = new()
        {
            IntersectionRule = IntersectionRule.NonZero
        };

        using (DrawingCanvas<TPixel> canvas = CreateCanvas(provider, target, new DrawingOptions()))
        {
            canvas.Clear(Brushes.Solid(Color.White));
            canvas.Fill(Brushes.Solid(Color.AliceBlue.WithAlpha(0.7F)), new Rectangle(12, 12, 336, 196));

            _ = canvas.Save(evenOddOptions);
            canvas.Fill(Brushes.Solid(Color.DeepPink.WithAlpha(0.85F)), leftPath);
            canvas.Restore();

            _ = canvas.Save(nonZeroOptions);
            canvas.Fill(Brushes.Solid(Color.DeepPink.WithAlpha(0.85F)), rightPath);
            canvas.Restore();

            canvas.Draw(Pens.Solid(Color.Black, 3F), leftPath);
            canvas.Draw(Pens.Solid(Color.Black, 3F), rightPath);
            canvas.DrawLine(Pens.Dash(Color.Gray, 2F), new PointF(180, 20), new PointF(180, 200));
            canvas.Draw(Pens.Solid(Color.Black, 2F), new Rectangle(8, 8, 344, 204));
        }

        target.DebugSave(provider, appendSourceFileOrDescription: false);
        target.CompareToReferenceOutput(provider, appendSourceFileOrDescription: false);
    }

    [Theory]
    [WithBlankImage(180, 180, PixelTypes.Rgba32)]
    public void Fill_SelfIntersectingPath_EvenOddWithRectangleClip_MatchesGeneralClip<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using Image<TPixel> expected = provider.GetImage();
        using Image<TPixel> actual = provider.GetImage();
        IPath path = CreatePentagramPath(new PointF(90, 90), 70F);
        IPath generalClipPath = CreateFivePointRectanglePath(new Rectangle(0, 0, 100, 180));
        IPath rectangleClipPath = new RectanglePolygon(0, 0, 100, 180);

        DrawingOptions evenOddOptions = new()
        {
            IntersectionRule = IntersectionRule.EvenOdd
        };

        using (DrawingCanvas<TPixel> canvas = CreateCanvas(provider, expected, new DrawingOptions()))
        {
            canvas.Clear(Brushes.Solid(Color.White));

            _ = canvas.Save(evenOddOptions);
            canvas.Clip(generalClipPath);
            canvas.Fill(Brushes.Solid(Color.DeepPink.WithAlpha(0.85F)), path);
            canvas.Restore();
        }

        using (DrawingCanvas<TPixel> canvas = CreateCanvas(provider, actual, new DrawingOptions()))
        {
            canvas.Clear(Brushes.Solid(Color.White));

            _ = canvas.Save(evenOddOptions);
            canvas.Clip(rectangleClipPath);
            canvas.Fill(Brushes.Solid(Color.DeepPink.WithAlpha(0.85F)), path);
            canvas.Restore();
        }

        expected.DebugSave(provider, "expected-general-clip", appendSourceFileOrDescription: false);
        actual.DebugSave(provider, "actual-rect-clip", appendSourceFileOrDescription: false);

        ImageComparer.TolerantPercentage(0.005F).VerifySimilarity(expected, actual);
        expected.CompareToReferenceOutput(provider, "expected-general-clip", appendSourceFileOrDescription: false);
        actual.CompareToReferenceOutput(provider, "actual-rect-clip", appendSourceFileOrDescription: false);
    }

    [Theory]
    [WithBlankImage(96, 64, PixelTypes.Rgba32)]
    public void Clip_DifferenceWithMultiplePaths_MatchesSequentialDifferenceClips<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using Image<TPixel> expected = provider.GetImage();
        using Image<TPixel> actual = provider.GetImage();
        IPath firstClip = new RectanglePolygon(20, 12, 34, 30);
        IPath secondClip = new RectanglePolygon(38, 28, 34, 24);

        using (DrawingCanvas<TPixel> canvas = CreateCanvas(provider, expected, new DrawingOptions()))
        {
            canvas.Clear(Brushes.Solid(Color.White));
            canvas.Clip(ClipOperation.Difference, firstClip);
            canvas.Clip(ClipOperation.Difference, secondClip);
            canvas.Fill(Brushes.Solid(Color.Red), new Rectangle(4, 4, 88, 56));
        }

        using (DrawingCanvas<TPixel> canvas = CreateCanvas(provider, actual, new DrawingOptions()))
        {
            canvas.Clear(Brushes.Solid(Color.White));
            canvas.Clip(ClipOperation.Difference, firstClip, secondClip);
            canvas.Fill(Brushes.Solid(Color.Red), new Rectangle(4, 4, 88, 56));
        }

        expected.DebugSave(provider, "expected-sequential-difference-clips", appendSourceFileOrDescription: false);
        actual.DebugSave(provider, "actual-multiple-difference-clips", appendSourceFileOrDescription: false);

        ImageComparer.Exact.VerifySimilarity(expected, actual);
        expected.CompareToReferenceOutput(provider, "expected-sequential-difference-clips", appendSourceFileOrDescription: false);
        actual.CompareToReferenceOutput(provider, "actual-multiple-difference-clips", appendSourceFileOrDescription: false);
    }

    /// <summary>
    /// Creates a rectangle path that is equivalent to <see cref="RectanglePolygon"/> but intentionally
    /// has five vertices so the rectangle fast path does not recognize it.
    /// </summary>
    /// <param name="rectangle">The rectangle to create.</param>
    /// <returns>The rectangle path.</returns>
    private static IPath CreateFivePointRectanglePath(Rectangle rectangle)
    {
        PathBuilder builder = new();
        builder.AddLine(rectangle.Left, rectangle.Top, rectangle.Right, rectangle.Top);
        builder.AddLine(rectangle.Right, rectangle.Top, rectangle.Right, rectangle.Top + (rectangle.Height / 2F));
        builder.AddLine(rectangle.Right, rectangle.Top + (rectangle.Height / 2F), rectangle.Right, rectangle.Bottom);
        builder.AddLine(rectangle.Right, rectangle.Bottom, rectangle.Left, rectangle.Bottom);
        builder.AddLine(rectangle.Left, rectangle.Bottom, rectangle.Left, rectangle.Top);
        builder.CloseAllFigures();

        return builder.Build();
    }

    private static IPath CreatePentagramPath(PointF center, float radius)
    {
        PointF[] points = new PointF[5];
        for (int i = 0; i < points.Length; i++)
        {
            float angle = (-MathF.PI / 2F) + (i * (MathF.PI * 2F / points.Length));
            points[i] = new PointF(
                center.X + (radius * MathF.Cos(angle)),
                center.Y + (radius * MathF.Sin(angle)));
        }

        int[] order = [0, 2, 4, 1, 3, 0];
        PathBuilder builder = new();
        for (int i = 0; i < order.Length - 1; i++)
        {
            PointF a = points[order[i]];
            PointF b = points[order[i + 1]];
            builder.AddLine(a.X, a.Y, b.X, b.Y);
        }

        builder.CloseAllFigures();
        return builder.Build();
    }
}
