// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.Fonts;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Drawing.Tests.Issues;

public class Issue_397
{
    [Theory]
    [WithBlankImage(240, 160, PixelTypes.Rgba32, ClipOperation.Intersection)]
    [WithBlankImage(240, 160, PixelTypes.Rgba32, ClipOperation.Difference)]
    public void DrawTextWithIntersectingClip<TPixel>(
        TestImageProvider<TPixel> provider,
        ClipOperation operation)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        PointF textOrigin = new(54, 78);
        PointF clipCenter = new(104, 70);
        Font font = TestFontUtilities.GetFont("OpenSans-Regular.ttf", 18);

        // Expected output:
        // - Intersection shows only red text inside the moved star.
        // - Difference shows only red text outside the moved star.
        provider.RunValidatingProcessorTest(
            x => x.Paint(canvas => DrawIssue397Sample(canvas, operation, clipCenter, textOrigin, font)),
            testOutputDetails: $"{operation}_IntersectingClip",
            appendPixelTypeToFileName: false,
            appendSourceFileOrDescription: false);
    }

    [Theory]
    [WithBlankImage(240, 160, PixelTypes.Rgba32, ClipOperation.Intersection)]
    [WithBlankImage(240, 160, PixelTypes.Rgba32, ClipOperation.Difference)]
    public void DrawTextWithNonIntersectingClip<TPixel>(
        TestImageProvider<TPixel> provider,
        ClipOperation operation)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        PointF textOrigin = new(54, 78);
        PointF clipCenter = new(192, 116);
        Font font = TestFontUtilities.GetFont("OpenSans-Regular.ttf", 18);

        // Expected output:
        // - Intersection shows no red text because the moved star and text do not overlap.
        // - Difference shows the full red text because the moved star removes nothing from it.
        provider.RunValidatingProcessorTest(
            x => x.Paint(canvas => DrawIssue397Sample(canvas, operation, clipCenter, textOrigin, font)),
            testOutputDetails: $"{operation}_NonIntersectingClip",
            appendPixelTypeToFileName: false,
            appendSourceFileOrDescription: false);
    }

    private static void DrawIssue397Sample(
        DrawingCanvas canvas,
        ClipOperation operation,
        PointF clipCenter,
        PointF textOrigin,
        Font font)
    {
        canvas.Clear(Brushes.Solid(Color.White));
        StarPolygon clipPath = new(clipCenter, 7, 16, 38, 18);
        RichTextOptions textOptions = new(font)
        {
            Origin = textOrigin
        };

        // The gray outline is the unclipped text guide; the red draw below shows the boolean clip result.
        canvas.DrawText(textOptions, "This is a test", brush: null, Pens.Solid(Color.LightGray, 1F));

        // The blue outline marks the moved clipping path without adding a filled shape behind the text.
        canvas.Draw(Pens.Solid(Color.DarkBlue, 1F), clipPath);
        canvas.Save();
        canvas.Clip(operation, clipPath);

        canvas.DrawText(
            textOptions,
            "This is a test",
            Brushes.Solid(Color.Crimson),
            pen: null);

        canvas.Restore();
        canvas.Draw(Pens.Solid(Color.DarkBlue, 1F), clipPath);
    }
}
