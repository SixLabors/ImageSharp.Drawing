// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Tests.TestUtilities.ImageComparison;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace SixLabors.ImageSharp.Drawing.Tests.Drawing;

[GroupOutput("Drawing")]
public class FillPathTests
{
    // https://developer.mozilla.org/en-US/docs/Web/SVG/Tutorial/Paths
    [Theory]
    [WithSolidFilledImages(325, 325, "White", PixelTypes.Rgba32)]
    public void FillPathSVGArcs<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        PathBuilder pb = new();

        pb.MoveTo(new Vector2(80, 80))
          .ArcTo(45, 45, 0, false, false, new Vector2(125, 125))
          .LineTo(new Vector2(125, 80))
          .CloseFigure();

        IPath path = pb.Build();

        pb = new PathBuilder();
        pb.MoveTo(new Vector2(230, 80))
          .ArcTo(45, 45, 0, true, false, new Vector2(275, 125))
          .LineTo(new Vector2(275, 80))
          .CloseFigure();

        IPath path2 = pb.Build();

        pb = new PathBuilder();
        pb.MoveTo(new Vector2(80, 230))
          .ArcTo(45, 45, 0, false, true, new Vector2(125, 275))
          .LineTo(new Vector2(125, 230))
          .CloseFigure();

        IPath path3 = pb.Build();

        pb = new PathBuilder();
        pb.MoveTo(new Vector2(230, 230))
          .ArcTo(45, 45, 0, true, true, new Vector2(275, 275))
          .LineTo(new Vector2(275, 230))
          .CloseFigure();

        IPath path4 = pb.Build();

        provider.VerifyOperation(
            image => image.Mutate(x => x.Fill(Color.Green, path).Fill(Color.Red, path2).Fill(Color.Purple, path3).Fill(Color.Blue, path4)),
            appendSourceFileOrDescription: false,
            appendPixelTypeToFileName: false);
    }

    // https://developer.mozilla.org/en-US/docs/Web/API/CanvasRenderingContext2D/arc
    [Theory]
    [WithSolidFilledImages(150, 200, "White", PixelTypes.Rgba32)]
    public void FillPathCanvasArcs<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => provider.VerifyOperation(
            ImageComparer.TolerantPercentage(5e-3f),
            image =>
            {
                for (int i = 0; i <= 3; i++)
                {
                    for (int j = 0; j <= 2; j++)
                    {
                        PathBuilder pb = new();

                        float x = 25 + (j * 50);                                      // x coordinate
                        float y = 25 + (i * 50);                                      // y coordinate
                        float radius = 20;                                            // Arc radius
                        float startAngle = 0;                                         // Starting point on circle
                        float endAngle = 180F + (180F * j / 2F);                      // End point on circle
                        bool counterclockwise = i % 2 == 1;                           // Draw counterclockwise

                        // To move counterclockwise we offset our sweepAngle parameter
                        // Canvas likely does something similar.
                        if (counterclockwise)
                        {
                            // 360 becomes zero and we don't accept that as a parameter (won't render).
                            if (endAngle < 360F)
                            {
                                endAngle = (360F - endAngle) % 360F;
                            }

                            endAngle *= -1;
                        }

                        pb.AddArc(x, y, radius, radius, 0, startAngle, endAngle);

                        if (i > 1)
                        {
                            image.Mutate(x => x.Fill(Color.Black, pb.Build()));
                        }
                        else
                        {
                            image.Mutate(x => x.Draw(new SolidPen(Color.Black, 1), pb.Build()));
                        }
                    }
                }
            },
            appendSourceFileOrDescription: false,
            appendPixelTypeToFileName: false);

    [Theory]
    [WithSolidFilledImages(400, 250, "White", PixelTypes.Rgba32)]
    public void FillPathArcToAlternates<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        // Test alternate syntax. Both should overlap creating an orange arc.
        PathBuilder pb = new();

        pb.MoveTo(new Vector2(50, 50));
        pb.ArcTo(20, 50, -72, false, true, new Vector2(200, 200));
        IPath path = pb.Build();

        pb = new PathBuilder();
        pb.MoveTo(new Vector2(50, 50));
        pb.AddSegment(new ArcLineSegment(new Vector2(50, 50), new Vector2(200, 200), new SizeF(20, 50), -72F, true, true));
        IPath path2 = pb.Build();

        provider.VerifyOperation(
            image => image.Mutate(x => x.Fill(Color.Yellow, path).Fill(Color.Red.WithAlpha(.5F), path2)),
            appendSourceFileOrDescription: false,
            appendPixelTypeToFileName: false);
    }
}
