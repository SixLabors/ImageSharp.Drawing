// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace SixLabors.ImageSharp.Drawing.Tests.Processing;

public partial class ProcessWithDrawingCanvasTests
{
    /// <summary>
    /// Verifies that aliased fills produce only fully covered or uncovered pixels.
    /// </summary>
    /// <typeparam name="TPixel">The pixel type.</typeparam>
    /// <param name="provider">The test image provider.</param>
    [Theory]
    [WithSolidFilledImages(100, 100, nameof(Color.Black), PixelTypes.Rgba32)]
    public void Fill_Aliased_ProducesOnlyBinaryCoverage<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        EllipsePolygon circle = new(50, 50, 40);
        DrawingOptions options = new() { GraphicsOptions = new GraphicsOptions { Antialias = false } };

        using Image<TPixel> image = provider.GetImage();
        image.Mutate(ctx => ctx.Paint(options, canvas => canvas.Fill(Brushes.Solid(Color.White), circle)));

        int whitePixels = CountPixelsAbove(image, 250);
        int partialPixels = CountPixelsBetween(image, 1, 250);

        Assert.Equal(0, partialPixels);
        Assert.True(whitePixels > 0, "Expected some white pixels from the filled circle.");
    }

    /// <summary>
    /// Verifies that an aliased fill of two rectangles in one path leaves the gap between them
    /// empty when a rectangle edge lies on or left of the left bound of the band. That edge goes
    /// into the start cover of the band, and the other edges go into the crossings, so both must
    /// count with the same sign.
    /// </summary>
    /// <typeparam name="TPixel">The pixel type.</typeparam>
    /// <param name="provider">The test image provider.</param>
    /// <param name="secondX">The left edge of the second rectangle.</param>
    [Theory]
    [WithSolidFilledImages(64, 16, nameof(Color.Black), PixelTypes.Rgba32, 27)]
    [WithSolidFilledImages(64, 16, nameof(Color.Black), PixelTypes.Rgba32, 59)]
    [WithSolidFilledImages(64, 16, nameof(Color.Black), PixelTypes.Rgba32, 64)]
    [WithSolidFilledImages(64, 16, nameof(Color.Black), PixelTypes.Rgba32, -4)]
    public void Fill_AliasedContourPastImageEdge_LeavesGapsEmpty<TPixel>(TestImageProvider<TPixel> provider, int secondX)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        const int firstX = 11;
        const int rectangleWidth = 8;
        ComplexPolygon path = new(
            new RectanglePolygon(firstX, 0, rectangleWidth, 16),
            new RectanglePolygon(secondX, 0, rectangleWidth, 16));

        DrawingOptions options = new() { GraphicsOptions = new GraphicsOptions { Antialias = false } };

        using Image<TPixel> image = provider.GetImage();
        image.Mutate(ctx => ctx.Paint(options, canvas => canvas.Fill(Brushes.Solid(Color.White), path)));

        // An aliased fill sets a pixel when the shape holds its centre, so a rectangle with integer
        // edges sets the columns from its left edge up to but not including its right edge.
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                Span<TPixel> row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    bool inside = (x >= firstX && x < firstX + rectangleWidth) || (x >= secondX && x < secondX + rectangleWidth);
                    Assert.True(inside == (row[x].ToRgba32().R == 255), $"Pixel ({x}, {y}) must be {(inside ? "set" : "empty")}.");
                }
            }
        });
    }

    /// <summary>
    /// Verifies that an edge left of the image counts at a row centre exactly as a visible edge does.
    /// In both shapes, the row 7 centre at y = 7.5 crosses the shape from x &lt;= 0 to x = 10, so
    /// row 7 sets the columns 0 to 9. A rectangle from x = 20 to x = 28 follows the gap.
    /// </summary>
    /// <remarks>
    /// The first shape starts left of the image at y = 7 and reaches x = 0 at the row centre. The
    /// second shape has its leftmost vertex at x = -0.5 on the row centre. Each edge left of the
    /// image covers only part of row 7, so a rounded cover height gives the wrong winding.
    /// </remarks>
    /// <typeparam name="TPixel">The pixel type.</typeparam>
    /// <param name="provider">The test image provider.</param>
    /// <param name="shape">The index of the shape that crosses the left image edge.</param>
    [Theory]
    [WithSolidFilledImages(48, 16, nameof(Color.Black), PixelTypes.Rgba32, 0)]
    [WithSolidFilledImages(48, 16, nameof(Color.Black), PixelTypes.Rgba32, 1)]
    public void Fill_AliasedEdgeLeftOfImageAtRowCentre_CountsOneCrossing<TPixel>(TestImageProvider<TPixel> provider, int shape)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        PointF[] points = shape == 0
            ? [new PointF(-1, 7), new PointF(1, 8), new PointF(10, 8), new PointF(10, 7)]
            : [new PointF(0.5F, 7.3F), new PointF(-0.5F, 7.5F), new PointF(0.5F, 7.7F), new PointF(10, 7.7F), new PointF(10, 7.3F)];

        ComplexPolygon path = new(new Polygon(points), new RectanglePolygon(20, 0, 8, 16));
        DrawingOptions options = new() { GraphicsOptions = new GraphicsOptions { Antialias = false } };

        using Image<TPixel> image = provider.GetImage();
        image.Mutate(ctx => ctx.Paint(options, canvas => canvas.Fill(Brushes.Solid(Color.White), path)));

        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                Span<TPixel> row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    bool inside = (y == 7 && x < 10) || (x >= 20 && x < 28);
                    Assert.True(inside == (row[x].ToRgba32().R == 255), $"Pixel ({x}, {y}) must be {(inside ? "set" : "empty")}.");
                }
            }
        });
    }

    /// <summary>
    /// Verifies that a thin horizontal bar at the edge of a 16-row band sets the row that holds its
    /// midpoint.
    /// </summary>
    /// <remarks>
    /// A 0.3 pixel bar holds no row centre, so the column pass sets the row of its midpoint. The
    /// first bar crosses the band edge at y = 16, the second starts on it, and the third ends on it.
    /// In the last case a 0.7 pixel bar holds the centre of row 16, and a thin bar lies below it, so
    /// the column walk starts inside the shape. Only the bar interiors are tested, because the
    /// contour-tip rule decides the end columns.
    /// </remarks>
    /// <typeparam name="TPixel">The pixel type.</typeparam>
    /// <param name="provider">The test image provider.</param>
    /// <param name="top">The top of the first bar.</param>
    /// <param name="height">The height of the first bar.</param>
    /// <param name="firstRow">The row that the first bar sets.</param>
    /// <param name="secondRow">The row that a second, 0.3 pixel bar at y = 17.6 sets, or -1 for no second bar.</param>
    [Theory]
    [WithSolidFilledImages(48, 32, nameof(Color.Black), PixelTypes.Rgba32, 15.85F, 0.3F, 16, -1)]
    [WithSolidFilledImages(48, 32, nameof(Color.Black), PixelTypes.Rgba32, 16F, 0.3F, 16, -1)]
    [WithSolidFilledImages(48, 32, nameof(Color.Black), PixelTypes.Rgba32, 15.7F, 0.3F, 15, -1)]
    [WithSolidFilledImages(48, 32, nameof(Color.Black), PixelTypes.Rgba32, 16.2F, 0.7F, 16, 17)]
    public void Fill_AliasedThinBarAtBandEdge_SetsMidpointRow<TPixel>(TestImageProvider<TPixel> provider, float top, float height, int firstRow, int secondRow)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        IPath path = secondRow < 0
            ? new RectanglePolygon(4, top, 40, height)
            : new ComplexPolygon(new RectanglePolygon(4, top, 40, height), new RectanglePolygon(4, 17.6F, 40, 0.3F));

        DrawingOptions options = new() { GraphicsOptions = new GraphicsOptions { Antialias = false } };

        using Image<TPixel> image = provider.GetImage();
        image.Mutate(ctx => ctx.Paint(options, canvas => canvas.Fill(Brushes.Solid(Color.White), path)));

        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                Span<TPixel> row = accessor.GetRowSpan(y);
                bool barRow = y == firstRow || y == secondRow;
                for (int x = 0; x < row.Length; x++)
                {
                    bool set = row[x].ToRgba32().R == 255;
                    if (!barRow)
                    {
                        Assert.False(set, $"Pixel ({x}, {y}) must be empty.");
                    }
                    else if (x >= 8 && x < 40)
                    {
                        Assert.True(set, $"Pixel ({x}, {y}) must be set.");
                    }
                }
            }
        });
    }

    /// <summary>
    /// Verifies that a thin bar below an edge that passes exactly through a pixel centre sets the
    /// row of its midpoint.
    /// </summary>
    /// <remarks>
    /// The sloped edge from (11.5, 15.5) to (9.4, 17.6) passes exactly through (10.5, 16.5), the centre
    /// of column 10 in row 16, the first row of the band. The 0.3 pixel bar at y = 18.1 holds no row
    /// centre, so its midpoint row 18 is set in every column of the bar interior.
    /// </remarks>
    /// <typeparam name="TPixel">The pixel type.</typeparam>
    /// <param name="provider">The test image provider.</param>
    [Theory]
    [WithSolidFilledImages(48, 32, nameof(Color.Black), PixelTypes.Rgba32)]
    public void Fill_AliasedThinBarBelowEdgeThroughCentre_SetsMidpointRow<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        ComplexPolygon path = new(
            new Polygon([new PointF(11.5F, 15.5F), new PointF(9.4F, 17.6F), new PointF(20, 17.6F), new PointF(20, 15.5F)]),
            new RectanglePolygon(4, 18.1F, 40, 0.3F));

        DrawingOptions options = new()
        {
            GraphicsOptions = new GraphicsOptions { Antialias = false },
            IntersectionRule = IntersectionRule.EvenOdd
        };

        using Image<TPixel> image = provider.GetImage();
        image.Mutate(ctx => ctx.Paint(options, canvas => canvas.Fill(Brushes.Solid(Color.White), path)));

        for (int x = 8; x < 40; x++)
        {
            Assert.True(image[x, 18].ToRgba32().R == 255, $"Pixel ({x}, 18) must be set.");
        }
    }

    /// <summary>
    /// Verifies that antialiased fills produce partial edge coverage.
    /// </summary>
    /// <typeparam name="TPixel">The pixel type.</typeparam>
    /// <param name="provider">The test image provider.</param>
    [Theory]
    [WithSolidFilledImages(100, 100, nameof(Color.Black), PixelTypes.Rgba32)]
    public void Fill_Antialiased_ProducesPartialCoverage<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        EllipsePolygon circle = new(50, 50, 40);
        DrawingOptions options = new() { GraphicsOptions = new GraphicsOptions { Antialias = true } };

        using Image<TPixel> image = provider.GetImage();
        image.Mutate(ctx => ctx.Paint(options, canvas => canvas.Fill(Brushes.Solid(Color.White), circle)));

        Assert.True(CountPixelsBetween(image, 1, 250) > 0, "Expected partially covered edge pixels.");
    }

    /// <summary>
    /// Counts pixels whose red channel is above the specified value.
    /// </summary>
    /// <typeparam name="TPixel">The pixel type.</typeparam>
    /// <param name="image">The image to inspect.</param>
    /// <param name="cutoff">The exclusive lower limit.</param>
    /// <returns>The matching pixel count.</returns>
    private static int CountPixelsAbove<TPixel>(Image<TPixel> image, byte cutoff)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        int count = 0;
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                Span<TPixel> row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    Rgba32 rgba = row[x].ToRgba32();
                    if (rgba.R > cutoff)
                    {
                        count++;
                    }
                }
            }
        });

        return count;
    }

    /// <summary>
    /// Counts pixels whose red channel is within the specified half-open range.
    /// </summary>
    /// <typeparam name="TPixel">The pixel type.</typeparam>
    /// <param name="image">The image to inspect.</param>
    /// <param name="low">The inclusive lower limit.</param>
    /// <param name="high">The exclusive upper limit.</param>
    /// <returns>The matching pixel count.</returns>
    private static int CountPixelsBetween<TPixel>(Image<TPixel> image, byte low, byte high)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        int count = 0;
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                Span<TPixel> row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    Rgba32 rgba = row[x].ToRgba32();
                    if (rgba.R >= low && rgba.R < high)
                    {
                        count++;
                    }
                }
            }
        });

        return count;
    }
}
