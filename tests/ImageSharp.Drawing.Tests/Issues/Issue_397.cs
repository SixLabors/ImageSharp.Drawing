// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.Fonts;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Drawing.Tests.Issues;

public class Issue_397
{
    [Theory]
    [WithBlankImage(240, 160, PixelTypes.Rgba32, BooleanOperation.Intersection)]
    [WithBlankImage(240, 160, PixelTypes.Rgba32, BooleanOperation.Union)]
    [WithBlankImage(240, 160, PixelTypes.Rgba32, BooleanOperation.Difference)]
    [WithBlankImage(240, 160, PixelTypes.Rgba32, BooleanOperation.Xor)]
    public void DrawTextWithIntersectingClip<TPixel>(
        TestImageProvider<TPixel> provider,
        BooleanOperation operation)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        PointF textOrigin = new(54, 78);
        PointF clipCenter = new(104, 70);
        DrawingOptions clipOptions = CreateClipOptions(operation);
        Font font = TestFontUtilities.GetFont("OpenSans-Regular.ttf", 18);

        // Expected output:
        // - Intersection shows only red text inside the moved star.
        // - Difference shows only red text outside the moved star.
        // - Union and Xor can show a red star because the boolean-combined path includes the clip path,
        //   and DrawText fills that combined result with the text brush.
        provider.RunValidatingProcessorTest(
            x => x.Paint(canvas => DrawIssue397Sample(canvas, clipOptions, clipCenter, textOrigin, font)),
            testOutputDetails: $"{operation}_IntersectingClip",
            appendPixelTypeToFileName: false,
            appendSourceFileOrDescription: false);
    }

    [Theory]
    [WithBlankImage(240, 160, PixelTypes.Rgba32, BooleanOperation.Intersection)]
    [WithBlankImage(240, 160, PixelTypes.Rgba32, BooleanOperation.Union)]
    [WithBlankImage(240, 160, PixelTypes.Rgba32, BooleanOperation.Difference)]
    [WithBlankImage(240, 160, PixelTypes.Rgba32, BooleanOperation.Xor)]
    public void DrawTextWithNonIntersectingClip<TPixel>(
        TestImageProvider<TPixel> provider,
        BooleanOperation operation)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        PointF textOrigin = new(54, 78);
        PointF clipCenter = new(192, 116);
        DrawingOptions clipOptions = CreateClipOptions(operation);
        Font font = TestFontUtilities.GetFont("OpenSans-Regular.ttf", 18);

        // Expected output:
        // - Intersection shows no red text because the moved star and text do not overlap.
        // - Difference shows the full red text because the moved star removes nothing from it.
        // - Union and Xor show both the full red text and a red star because disjoint Xor matches Union,
        //   and DrawText fills the boolean-combined result with the text brush.
        provider.RunValidatingProcessorTest(
            x => x.Paint(canvas => DrawIssue397Sample(canvas, clipOptions, clipCenter, textOrigin, font)),
            testOutputDetails: $"{operation}_NonIntersectingClip",
            appendPixelTypeToFileName: false,
            appendSourceFileOrDescription: false);
    }

    private static void DrawIssue397Sample(
        DrawingCanvas canvas,
        DrawingOptions clipOptions,
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
        canvas.Save(clipOptions, clipPath);

        canvas.DrawText(
            textOptions,
            "This is a test",
            Brushes.Solid(Color.Crimson),
            pen: null);

        canvas.Restore();
        canvas.Draw(Pens.Solid(Color.DarkBlue, 1F), clipPath);
    }

    private static DrawingOptions CreateClipOptions(BooleanOperation operation)
        => new()
        {
            ShapeOptions = new()
            {
                BooleanOperation = operation
            }
        };
}
