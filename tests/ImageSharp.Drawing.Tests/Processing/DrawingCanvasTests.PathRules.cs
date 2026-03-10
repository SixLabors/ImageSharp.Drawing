// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Processing;
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
        using DrawingCanvas<TPixel> canvas = CreateCanvas(provider, target, new DrawingOptions());

        IPath leftPath = CreatePentagramPath(new PointF(96, 110), 78F);
        IPath rightPath = CreatePentagramPath(new PointF(264, 110), 78F);

        DrawingOptions evenOddOptions = new()
        {
            ShapeOptions = new ShapeOptions { IntersectionRule = IntersectionRule.EvenOdd }
        };

        DrawingOptions nonZeroOptions = new()
        {
            ShapeOptions = new ShapeOptions { IntersectionRule = IntersectionRule.NonZero }
        };

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
        canvas.Flush();

        target.DebugSave(provider, appendSourceFileOrDescription: false);
        target.CompareToReferenceOutput(provider, appendSourceFileOrDescription: false);
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
