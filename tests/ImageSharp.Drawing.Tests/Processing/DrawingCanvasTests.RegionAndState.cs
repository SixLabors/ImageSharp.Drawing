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

        // Expected output from the source operations:
        // - The child canvas is clipped to absolute region (40,24)-(180,120), but commands use
        //   child-local coordinates.
        // - The sea-green fill is local (10,8)-(90,54), so it lands at absolute (50,32)-(130,78).
        // - The dark-blue 5px rectangle stroke is centered on local (0,0)-(140,96) and clipped by
        //   the region, leaving only the inner half of the stroke visible on the region boundary.
        // - The orange-red 4px line runs local (0,95)->(139,0), landing on the region diagonal from
        //   absolute near (40,119) to (179,24), clipped to the child region.
        using (DrawingCanvas<TPixel> canvas = CreateCanvas(provider, target, new DrawingOptions()))
        {
            canvas.Clear(Brushes.Solid(Color.White));

            using DrawingCanvas<TPixel> regionCanvas = canvas.CreateRegion(new Rectangle(40, 24, 140, 96));

            regionCanvas.Fill(Brushes.Solid(Color.LightSeaGreen.WithAlpha(0.8F)), new Rectangle(10, 8, 80, 46));
            regionCanvas.Draw(Pens.Solid(Color.DarkBlue, 5), new Rectangle(0, 0, 140, 96));
            regionCanvas.DrawLine(
                Pens.Solid(Color.OrangeRed, 4),
                new PointF(0, 95),
                new PointF(139, 0));
        }

        target.DebugSave(provider, appendSourceFileOrDescription: false);
        target.CompareToReferenceOutput(provider, appendSourceFileOrDescription: false);
    }

    [Theory]
    [WithBlankImage(192, 128, PixelTypes.Rgba32)]
    public void SaveRestore_ClipPath_MatchesReference<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using Image<TPixel> target = provider.GetImage();
        IPath clipPath = new EllipsePolygon(new PointF(96, 64), new SizeF(120, 76));

        ShapeOptions difference = new()
        {
            BooleanOperation = BooleanOperation.Difference
        };

        using (DrawingCanvas<TPixel> canvas = CreateCanvas(provider, target, new DrawingOptions() { ShapeOptions = difference }))
        {
            canvas.Clear(Brushes.Solid(Color.White));

            // Expected output from the source operations:
            // - Save replaces the active clip with ellipse (36,26)-(156,102), interpreted through
            //   Difference for subsequent commands in this saved state.
            // - The violet full-canvas fill becomes the canvas rectangle with that oval removed.
            // - The black 3px rectangle stroke is around (24,16)-(168,112); the ellipse sits inside
            //   that border, so the stroke remains a rectangular border rather than showing an oval cut.
            // - After Restore, the steel-blue bottom strip and dark-green border are drawn with no clip.
            _ = canvas.Save(new DrawingOptions() { ShapeOptions = difference }, clipPath);

            canvas.Fill(Brushes.Solid(Color.MediumVioletRed.WithAlpha(0.85F)), new Rectangle(0, 0, 192, 128));
            canvas.Draw(Pens.Solid(Color.Black, 3), new Rectangle(24, 16, 144, 96));

            canvas.Restore();

            canvas.Fill(Brushes.Solid(Color.SteelBlue.WithAlpha(0.75F)), new Rectangle(0, 96, 192, 32));
            canvas.Draw(Pens.Solid(Color.DarkGreen, 4), new Rectangle(8, 8, 176, 112));
        }

        target.DebugSave(provider, appendSourceFileOrDescription: false);

        ImageComparer tolerantComparer = ImageComparer.TolerantPercentage(0.0003F);
        target.CompareToReferenceOutput(tolerantComparer, provider, appendSourceFileOrDescription: false);
    }

    [Theory]
    [WithBlankImage(224, 160, PixelTypes.Rgba32)]
    public void RestoreTo_MultipleStates_MatchesReference<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        ShapeOptions difference = new()
        {
            BooleanOperation = BooleanOperation.Difference
        };

        using Image<TPixel> target = provider.GetImage();
        DrawingOptions firstOptions = new()
        {
            Transform = Matrix4x4.CreateTranslation(20F, 12F, 0),
            ShapeOptions = difference
        };

        DrawingOptions secondOptions = new()
        {
            Transform = new Matrix4x4(Matrix3x2.CreateRotation(0.24F, new Vector2(112, 80))),
            ShapeOptions = difference
        };

        using (DrawingCanvas<TPixel> canvas = CreateCanvas(provider, target, new DrawingOptions() { ShapeOptions = difference }))
        {
            canvas.Clear(Brushes.Solid(Color.White));

            // Expected output from the first saved state:
            // - Save replaces the active clip with rectangle (20,20)-(164,124), transformed by
            //   translation to absolute (40,32)-(184,136).
            // - The sky-blue fill is local (0,0)-(120,84), transformed to (20,12)-(140,96).
            // - Difference leaves the translated fill only in the top strip y=12..32 and left strip
            //   x=20..40 outside the translated clip rectangle.
            int firstSaveCount = canvas.Save(firstOptions, new RectanglePolygon(20, 20, 144, 104));
            canvas.Fill(Brushes.Solid(Color.SkyBlue.WithAlpha(0.8F)), new Rectangle(0, 0, 120, 84));

            // Expected output from the second saved state:
            // - Save replaces the first clip with ellipse (47,35)-(177,125), transformed by the same
            //   rotation used for the purple stroke.
            // - The purple 6px rectangle stroke is centered on (34,26)-(186,134). The ellipse is inside
            //   that border, so Difference leaves the rotated rectangular stroke visually uncut by the oval.
            _ = canvas.Save(secondOptions, new EllipsePolygon(new PointF(112, 80), new SizeF(130, 90)));
            canvas.Draw(Pens.Solid(Color.MediumPurple, 6), new Rectangle(34, 26, 152, 108));

            // RestoreTo(firstSaveCount) returns to the translated rectangle Difference clip. The
            // orange-red 5px polyline is transformed to (20,112)->(96,30)->(188,104).
            // The visible stroke pieces are centered on (20,112)->(40,90.4), the small top V
            // (94.1,32)->(96,30)->(98.5,32), and (184,100.8)->(188,104).
            canvas.RestoreTo(firstSaveCount);
            canvas.DrawLine(
                Pens.Solid(Color.OrangeRed, 5),
                new PointF(0, 100),
                new PointF(76, 18),
                new PointF(168, 92));

            // Restoring to root removes all saved transforms and clips before drawing the
            // un-clipped gold rectangle and final dark border.
            canvas.RestoreTo(1);
            canvas.Fill(Brushes.Solid(Color.Gold.WithAlpha(0.7F)), new Rectangle(156, 106, 48, 34));
            canvas.Draw(Pens.Solid(Color.DarkSlateGray, 4), new Rectangle(8, 8, 208, 144));
        }

        target.DebugSave(provider, appendSourceFileOrDescription: false);
        target.CompareToReferenceOutput(provider, appendSourceFileOrDescription: false);
    }

    [Theory]
    [WithBlankImage(320, 220, PixelTypes.Rgba32)]
    public void CreateRegion_NestedRegionsAndStateIsolation_MatchesReference<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        ShapeOptions difference = new()
        {
            BooleanOperation = BooleanOperation.Difference
        };

        using Image<TPixel> target = provider.GetImage();

        // This test checks the exact shape produced by each nested state:
        // 1. The root canvas starts as solid white, then gets an unrotated ghost-white rectangle
        //    at (12,12)-(308,208).
        // 2. The root saved state translates later drawing by (6,4) and uses Difference against
        //    the translated oval. The first outer-region fill and dark-blue rectangle stroke are
        //    therefore clipped to the outer region and have that oval removed.
        // 3. Saving the outer region with a new explicit clip replaces the root oval clip for that
        //    state. The active shape becomes the outer rotation around local (120,78), with
        //    Difference against the rotated rectangle (18,14)-(222,142).
        // 4. The purple fill is local rectangle (16,16)-(224,140). Because the active Difference
        //    rectangle fully covers its vertical span and starts/stops two pixels inside its
        //    horizontal edges, the only surviving purple geometry is local strip (16,16)-(18,140)
        //    and local strip (222,16)-(224,140), both rotated and clipped to the outer region.
        // 5. The inner region is created inside the outer rotated state. Its yellow clear uses the
        //    inner canvas bounds, local rectangle (0,0)-(132,82). Difference against the outer
        //    clip rectangle removes local (18,14)-(132,82), leaving the rotated yellow L made from
        //    local strip (0,0)-(18,82) plus local strip (18,0)-(132,14), clipped to the inner target.
        // 6. Saving the inner region with a new explicit clip replaces the outer rectangle clip for
        //    that state. The active shape becomes the inner skew transform with Difference against
        //    the skewed oval centered at (66,41), size (102,58).
        // 7. The green fill is local rectangle (0,0)-(132,82) with that skewed oval removed. The
        //    red polyline (0,80)->(66,0)->(132,74) is clipped by the same skewed oval.
        // 8. Restoring the inner state returns to the outer rotated Difference state. The black
        //    dash-dot rectangle is local stroke (4,4)-(128,78); the visible stroke is the rotated
        //    top edge and left edge that survive Difference against local rectangle (18,14)-(222,142),
        //    clipped to the inner target.
        // 9. Restoring the outer state returns to the root translated oval-Difference state. The
        //    orange rectangle (8,112)-(98,142) and black diagonal (8,8)->(232,148) are translated,
        //    clipped to the outer region, and have the root oval removed.
        // 10. Restoring the root state removes all nested clips and transforms. The final dark
        //    border (8,8)-(312,212) and grey dashed line (20,200)->(300,20) are root-local output.
        DrawingOptions rootOptions = new()
        {
            Transform = Matrix4x4.CreateTranslation(6F, 4F, 0),
            ShapeOptions = difference
        };

        IPath rootClip = new EllipsePolygon(new PointF(160, 110), new SizeF(252, 164));

        using (DrawingCanvas<TPixel> canvas = CreateCanvas(provider, target, new DrawingOptions() { ShapeOptions = difference }))
        {
            canvas.Clear(Brushes.Solid(Color.White));
            canvas.Fill(Brushes.Solid(Color.GhostWhite.WithAlpha(0.85F)), new Rectangle(12, 12, 296, 196));

            _ = canvas.Save(rootOptions, rootClip);

            using (DrawingCanvas<TPixel> outerRegion = canvas.CreateRegion(new Rectangle(30, 24, 240, 156)))
            {
                outerRegion.Fill(Brushes.Solid(Color.LightBlue.WithAlpha(0.35F)), new Rectangle(0, 0, 240, 156));
                outerRegion.Draw(Pens.Solid(Color.DarkBlue, 3F), new Rectangle(0, 0, 240, 156));

                DrawingOptions outerOptions = new()
                {
                    Transform = new Matrix4x4(Matrix3x2.CreateRotation(0.18F, new Vector2(120, 78))),
                    ShapeOptions = difference
                };

                _ = outerRegion.Save(outerOptions, new RectanglePolygon(18, 14, 204, 128));

                outerRegion.Fill(Brushes.Solid(Color.MediumPurple.WithAlpha(0.35F)), new Rectangle(16, 16, 208, 124));

                using (DrawingCanvas<TPixel> innerRegion = outerRegion.CreateRegion(new Rectangle(52, 34, 132, 82)))
                {
                    innerRegion.Clear(Brushes.Solid(Color.LightGoldenrodYellow.WithAlpha(0.8F)));

                    DrawingOptions innerOptions = new()
                    {
                        Transform = new Matrix4x4(Matrix3x2.CreateSkew(0.18F, 0F)),
                        ShapeOptions = difference
                    };

                    _ = innerRegion.Save(innerOptions, new EllipsePolygon(new PointF(66, 41), new SizeF(102, 58)));

                    innerRegion.Fill(Brushes.Solid(Color.SeaGreen.WithAlpha(0.55F)), new Rectangle(0, 0, 132, 82));
                    innerRegion.DrawLine(
                        Pens.Solid(Color.DarkRed, 4F),
                        new PointF(0, 80),
                        new PointF(66, 0),
                        new PointF(132, 74));

                    innerRegion.Restore();

                    innerRegion.Draw(Pens.DashDot(Color.Black.WithAlpha(0.75F), 2F), new Rectangle(4, 4, 124, 74));
                }

                outerRegion.Restore();

                outerRegion.Fill(Brushes.Solid(Color.OrangeRed.WithAlpha(0.6F)), new Rectangle(8, 112, 90, 30));
                outerRegion.DrawLine(Pens.Solid(Color.Black, 3F), new PointF(8, 8), new PointF(232, 148));
            }

            canvas.RestoreTo(1);

            canvas.Draw(Pens.Solid(Color.DarkSlateGray, 3F), new Rectangle(8, 8, 304, 204));
            canvas.DrawLine(Pens.Dash(Color.Gray, 2F), new PointF(20, 200), new PointF(300, 20));
        }

        target.DebugSave(provider, appendSourceFileOrDescription: false);
        target.CompareToReferenceOutput(provider, appendSourceFileOrDescription: false);
    }
}
