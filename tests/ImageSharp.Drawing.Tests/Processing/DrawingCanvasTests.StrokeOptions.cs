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
    public void Draw_SelfIntersectingStroke_MatchesReference<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using Image<TPixel> target = provider.GetImage();
        IPath leftPath = CreateBowTiePath(new RectangleF(28, 34, 128, 152));
        IPath rightPath = CreateBowTiePath(new RectangleF(204, 34, 128, 152));

        SolidPen pen = new(Color.CornflowerBlue.WithAlpha(0.88F), 24F);
        pen.StrokeOptions.LineJoin = LineJoin.Round;
        pen.StrokeOptions.LineCap = LineCap.Round;

        DrawingOptions evenOddOptions = new()
        {
            ShapeOptions = new ShapeOptions { IntersectionRule = IntersectionRule.EvenOdd }
        };

        using (DrawingCanvas<TPixel> canvas = CreateCanvas(provider, target, new DrawingOptions()))
        {
            canvas.Clear(Brushes.Solid(Color.White));
            canvas.Fill(Brushes.Solid(Color.GhostWhite.WithAlpha(0.85F)), new Rectangle(12, 12, 336, 196));

            _ = canvas.Save(evenOddOptions);
            canvas.Draw(pen, leftPath);
            canvas.Draw(pen, rightPath);
            canvas.Restore();

            canvas.Draw(Pens.Solid(Color.DarkSlateGray, 2F), leftPath);
            canvas.Draw(Pens.Solid(Color.DarkSlateGray, 2F), rightPath);
            canvas.DrawLine(Pens.DashDot(Color.Gray, 2F), new PointF(180, 20), new PointF(180, 200));
            canvas.Draw(Pens.Solid(Color.Black, 2F), new Rectangle(8, 8, 344, 204));
        }

        target.DebugSave(provider, appendSourceFileOrDescription: false);
        target.CompareToReferenceOutput(ImageComparer.TolerantPercentage(0.0001F), provider, appendSourceFileOrDescription: false);
    }

    private static IPath CreateBowTiePath(RectangleF bounds)
    {
        float left = bounds.Left;
        float right = bounds.Right;
        float top = bounds.Top;
        float bottom = bounds.Bottom;

        PathBuilder builder = new();
        builder.AddLine(left, top, right, bottom);
        builder.AddLine(right, bottom, left, bottom);
        builder.AddLine(left, bottom, right, top);
        builder.AddLine(right, top, left, top);
        builder.CloseAllFigures();
        return builder.Build();
    }
}
