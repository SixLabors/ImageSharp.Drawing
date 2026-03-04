// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Tests.TestUtilities.ImageComparison;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Drawing.Tests.Processing;

public partial class DrawingCanvasTests
{
    [Theory]
    [WithBlankImage(256, 160, PixelTypes.Rgba32)]
    public void CreateRegion_LocalCoordinates_MatchesReference<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using Image<TPixel> target = provider.GetImage();
        using DrawingCanvas<TPixel> canvas = CreateCanvas(provider, target, new DrawingOptions());

        canvas.Clear(Brushes.Solid(Color.White));

        using (DrawingCanvas<TPixel> regionCanvas = canvas.CreateRegion(new Rectangle(40, 24, 140, 96)))
        {
            regionCanvas.Fill(new Rectangle(10, 8, 80, 46), Brushes.Solid(Color.LightSeaGreen.WithAlpha(0.8F)));
            regionCanvas.Draw(Pens.Solid(Color.DarkBlue, 5), new Rectangle(0, 0, 140, 96));
            regionCanvas.DrawLine(
                Pens.Solid(Color.OrangeRed, 4),
                new PointF(0, 95),
                new PointF(139, 0));
        }

        canvas.Flush();

        target.DebugSave(provider, appendSourceFileOrDescription: false);
        target.CompareToReferenceOutput(provider, appendSourceFileOrDescription: false);
    }

    [Theory]
    [WithBlankImage(192, 128, PixelTypes.Rgba32)]
    public void SaveRestore_ClipPath_MatchesReference<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using Image<TPixel> target = provider.GetImage();
        using DrawingCanvas<TPixel> canvas = CreateCanvas(provider, target, new DrawingOptions());

        canvas.Clear(Brushes.Solid(Color.White));

        IPath clipPath = new EllipsePolygon(new PointF(96, 64), new SizeF(120, 76));
        _ = canvas.Save(new DrawingOptions(), clipPath);

        canvas.Fill(new Rectangle(0, 0, 192, 128), Brushes.Solid(Color.MediumVioletRed.WithAlpha(0.85F)));
        canvas.Draw(Pens.Solid(Color.Black, 3), new Rectangle(24, 16, 144, 96));

        canvas.Restore();

        canvas.Fill(new Rectangle(0, 96, 192, 32), Brushes.Solid(Color.SteelBlue.WithAlpha(0.75F)));
        canvas.Draw(Pens.Solid(Color.DarkGreen, 4), new Rectangle(8, 8, 176, 112));

        canvas.Flush();

        target.DebugSave(provider, appendSourceFileOrDescription: false);

        ImageComparer tolerantComparer = ImageComparer.TolerantPercentage(0.0003F);
        target.CompareToReferenceOutput(provider, tolerantComparer, appendSourceFileOrDescription: false);
    }

    [Theory]
    [WithBlankImage(224, 160, PixelTypes.Rgba32)]
    public void RestoreTo_MultipleStates_MatchesReference<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using Image<TPixel> target = provider.GetImage();
        using DrawingCanvas<TPixel> canvas = CreateCanvas(provider, target, new DrawingOptions());

        canvas.Clear(Brushes.Solid(Color.White));

        DrawingOptions firstOptions = new()
        {
            Transform = Matrix3x2.CreateTranslation(20F, 12F)
        };

        int firstSaveCount = canvas.Save(firstOptions, new RectangularPolygon(20, 20, 144, 104));
        canvas.Fill(new Rectangle(0, 0, 120, 84), Brushes.Solid(Color.SkyBlue.WithAlpha(0.8F)));

        DrawingOptions secondOptions = new()
        {
            Transform = Matrix3x2.CreateRotation(0.24F, new Vector2(112, 80))
        };

        _ = canvas.Save(secondOptions, new EllipsePolygon(new PointF(112, 80), new SizeF(130, 90)));
        canvas.Draw(Pens.Solid(Color.MediumPurple, 6), new Rectangle(34, 26, 152, 108));

        canvas.RestoreTo(firstSaveCount);
        canvas.DrawLine(
            Pens.Solid(Color.OrangeRed, 5),
            new PointF(0, 100),
            new PointF(76, 18),
            new PointF(168, 92));

        canvas.RestoreTo(1);
        canvas.Fill(new Rectangle(156, 106, 48, 34), Brushes.Solid(Color.Gold.WithAlpha(0.7F)));
        canvas.Draw(Pens.Solid(Color.DarkSlateGray, 4), new Rectangle(8, 8, 208, 144));

        canvas.Flush();

        target.DebugSave(provider, appendSourceFileOrDescription: false);
        target.CompareToReferenceOutput(provider, appendSourceFileOrDescription: false);
    }

    [Theory]
    [WithBlankImage(320, 220, PixelTypes.Rgba32)]
    public void CreateRegion_NestedRegionsAndStateIsolation_MatchesReference<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using Image<TPixel> target = provider.GetImage();
        using DrawingCanvas<TPixel> canvas = CreateCanvas(provider, target, new DrawingOptions());

        canvas.Clear(Brushes.Solid(Color.White));
        canvas.Fill(new Rectangle(12, 12, 296, 196), Brushes.Solid(Color.GhostWhite.WithAlpha(0.85F)));

        DrawingOptions rootOptions = new()
        {
            Transform = Matrix3x2.CreateTranslation(6F, 4F)
        };

        IPath rootClip = new EllipsePolygon(new PointF(160, 110), new SizeF(252, 164));
        _ = canvas.Save(rootOptions, rootClip);

        using (DrawingCanvas<TPixel> outerRegion = canvas.CreateRegion(new Rectangle(30, 24, 240, 156)))
        {
            outerRegion.Fill(new Rectangle(0, 0, 240, 156), Brushes.Solid(Color.LightBlue.WithAlpha(0.35F)));
            outerRegion.Draw(Pens.Solid(Color.DarkBlue, 3F), new Rectangle(0, 0, 240, 156));

            DrawingOptions outerOptions = new()
            {
                Transform = Matrix3x2.CreateRotation(0.18F, new Vector2(120, 78))
            };

            _ = outerRegion.Save(outerOptions, new RectangularPolygon(18, 14, 204, 128));
            outerRegion.Fill(new Rectangle(16, 16, 208, 124), Brushes.Solid(Color.MediumPurple.WithAlpha(0.35F)));

            using (DrawingCanvas<TPixel> innerRegion = outerRegion.CreateRegion(new Rectangle(52, 34, 132, 82)))
            {
                innerRegion.Clear(Brushes.Solid(Color.LightGoldenrodYellow.WithAlpha(0.8F)));

                DrawingOptions innerOptions = new()
                {
                    Transform = Matrix3x2.CreateSkew(0.18F, 0F)
                };

                _ = innerRegion.Save(innerOptions, new EllipsePolygon(new PointF(66, 41), new SizeF(102, 58)));
                innerRegion.Fill(new Rectangle(0, 0, 132, 82), Brushes.Solid(Color.SeaGreen.WithAlpha(0.55F)));
                innerRegion.DrawLine(
                    Pens.Solid(Color.DarkRed, 4F),
                    new PointF(0, 80),
                    new PointF(66, 0),
                    new PointF(132, 74));
                innerRegion.Restore();

                innerRegion.Draw(Pens.DashDot(Color.Black.WithAlpha(0.75F), 2F), new Rectangle(4, 4, 124, 74));
            }

            outerRegion.Restore();

            outerRegion.Fill(new Rectangle(8, 112, 90, 30), Brushes.Solid(Color.OrangeRed.WithAlpha(0.6F)));
            outerRegion.DrawLine(Pens.Solid(Color.Black, 3F), new PointF(8, 8), new PointF(232, 148));
        }

        canvas.RestoreTo(1);
        canvas.Draw(Pens.Solid(Color.DarkSlateGray, 3F), new Rectangle(8, 8, 304, 204));
        canvas.DrawLine(Pens.Dash(Color.Gray, 2F), new PointF(20, 200), new PointF(300, 20));

        canvas.Flush();

        target.DebugSave(provider, appendSourceFileOrDescription: false);
        target.CompareToReferenceOutput(provider, appendSourceFileOrDescription: false);
    }
}
