// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Barcodes;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace SixLabors.ImageSharp.Drawing.Tests.Barcodes;

/// <summary>
/// Layout contract tests for the barcode rendering layer. A caller sizes an image from the rectangle
/// <see cref="DrawingCanvas.MeasureBarcode(BarcodeSymbology, BarcodeOptions, PointF, ReadOnlySpan{char})"/> returns, so everything the matching draw call
/// renders must land inside that rectangle.
/// <para>
/// Bar edges and text baselines round onto the device pixel grid. A fractional origin or a fractional module width therefore moves ink away from the unrounded
/// layout position, by up to half a pixel in either direction. The golden tests all draw from a whole pixel origin, where rounding is the identity, so they
/// cannot see this.
/// </para>
/// </summary>
[GroupOutput("Barcodes")]
public class BarcodeLayoutTests
{
    /// <summary>
    /// The clear space around the measured area, in pixels. Content that escapes the measured rectangle lands in this margin, where the assertion sees it,
    /// rather than off the image where it is clipped away.
    /// </summary>
    private const int Margin = 20;

    /// <summary>
    /// The bars alone, with no human readable interpretation.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    /// <param name="provider">The image provider.</param>
    /// <param name="fraction">The fractional part of the draw origin.</param>
    /// <param name="moduleWidth">The width of one module, in pixels.</param>
    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32, 0F, 2F)]
    [WithBlankImage(1, 1, PixelTypes.Rgba32, 0.3F, 2F)]
    [WithBlankImage(1, 1, PixelTypes.Rgba32, 0.5F, 2F)]
    [WithBlankImage(1, 1, PixelTypes.Rgba32, 0.7F, 2F)]
    [WithBlankImage(1, 1, PixelTypes.Rgba32, 0.3F, 2.3F)]
    [WithBlankImage(1, 1, PixelTypes.Rgba32, 0.7F, 2.3F)]
    public void BarsStayInsideTheMeasuredBounds<TPixel>(TestImageProvider<TPixel> provider, float fraction, float moduleWidth)
        where TPixel : unmanaged, IPixel<TPixel>
        => AssertEverythingDrawnStaysInsideBounds(provider, new Code39Symbology(), "CODE39", CreateOptions(moduleWidth), fraction, moduleWidth);

    /// <summary>
    /// A symbol whose digits print below the bars, which grows the measured area downward.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    /// <param name="provider">The image provider.</param>
    /// <param name="fraction">The fractional part of the draw origin.</param>
    /// <param name="moduleWidth">The width of one module, in pixels.</param>
    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32, 0F, 2F)]
    [WithBlankImage(1, 1, PixelTypes.Rgba32, 0.3F, 2F)]
    [WithBlankImage(1, 1, PixelTypes.Rgba32, 0.5F, 2F)]
    [WithBlankImage(1, 1, PixelTypes.Rgba32, 0.7F, 2F)]
    [WithBlankImage(1, 1, PixelTypes.Rgba32, 0.3F, 2.3F)]
    [WithBlankImage(1, 1, PixelTypes.Rgba32, 0.7F, 2.3F)]
    public void TextBelowTheBarsStaysInsideTheMeasuredBounds<TPixel>(TestImageProvider<TPixel> provider, float fraction, float moduleWidth)
        where TPixel : unmanaged, IPixel<TPixel>
        => AssertEverythingDrawnStaysInsideBounds(provider, new Ean13Symbology(), "5901234123457", CreateTextOptions(moduleWidth), fraction, moduleWidth);

    /// <summary>
    /// An add-on symbol, whose digits print above the bars. Those digits set the top band, so the ink of the topmost line sits on the measured top edge rather
    /// than the bars.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    /// <param name="provider">The image provider.</param>
    /// <param name="fraction">The fractional part of the draw origin.</param>
    /// <param name="moduleWidth">The width of one module, in pixels.</param>
    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32, 0F, 2F)]
    [WithBlankImage(1, 1, PixelTypes.Rgba32, 0.3F, 2F)]
    [WithBlankImage(1, 1, PixelTypes.Rgba32, 0.5F, 2F)]
    [WithBlankImage(1, 1, PixelTypes.Rgba32, 0.7F, 2F)]
    [WithBlankImage(1, 1, PixelTypes.Rgba32, 0.3F, 2.3F)]
    [WithBlankImage(1, 1, PixelTypes.Rgba32, 0.7F, 2.3F)]
    public void TextAboveTheBarsStaysInsideTheMeasuredBounds<TPixel>(TestImageProvider<TPixel> provider, float fraction, float moduleWidth)
        where TPixel : unmanaged, IPixel<TPixel>
        => AssertEverythingDrawnStaysInsideBounds(provider, new Ean5Symbology(), "52495", CreateTextOptions(moduleWidth), fraction, moduleWidth);

    /// <summary>
    /// A caption scaled to the symbol width, which <see cref="BarcodeOptions.FitCaptionToSymbolWidth"/> does by default. The scale comes from the measured
    /// caption width, so the fitted caption ends level with the bars.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    /// <param name="provider">The image provider.</param>
    /// <param name="fraction">The fractional part of the draw origin.</param>
    /// <param name="moduleWidth">The width of one module, in pixels.</param>
    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32, 0F, 2F)]
    [WithBlankImage(1, 1, PixelTypes.Rgba32, 0.3F, 2F)]
    [WithBlankImage(1, 1, PixelTypes.Rgba32, 0.5F, 2F)]
    [WithBlankImage(1, 1, PixelTypes.Rgba32, 0.7F, 2F)]
    [WithBlankImage(1, 1, PixelTypes.Rgba32, 0.3F, 2.3F)]
    [WithBlankImage(1, 1, PixelTypes.Rgba32, 0.7F, 2.3F)]
    public void AFittedCaptionStaysInsideTheMeasuredBounds<TPixel>(TestImageProvider<TPixel> provider, float fraction, float moduleWidth)
        where TPixel : unmanaged, IPixel<TPixel>
        => AssertEverythingDrawnStaysInsideBounds(provider, new IsbnSymbology(), "978-0-306-40615-7", CreateTextOptions(moduleWidth), fraction, moduleWidth);

    /// <summary>
    /// A caption with the fit to the symbol width suppressed, so it keeps the size the caller gave it and overhangs the bars. The overhanging caption sets the
    /// measured width in place of the symbol.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    /// <param name="provider">The image provider.</param>
    /// <param name="fraction">The fractional part of the draw origin.</param>
    /// <param name="moduleWidth">The width of one module, in pixels.</param>
    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32, 0F, 2F)]
    [WithBlankImage(1, 1, PixelTypes.Rgba32, 0.3F, 2F)]
    [WithBlankImage(1, 1, PixelTypes.Rgba32, 0.5F, 2F)]
    [WithBlankImage(1, 1, PixelTypes.Rgba32, 0.7F, 2F)]
    [WithBlankImage(1, 1, PixelTypes.Rgba32, 0.3F, 2.3F)]
    [WithBlankImage(1, 1, PixelTypes.Rgba32, 0.7F, 2.3F)]
    public void AnUnfittedCaptionStaysInsideTheMeasuredBounds<TPixel>(TestImageProvider<TPixel> provider, float fraction, float moduleWidth)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        BarcodeOptions options = CreateTextOptions(moduleWidth);
        options.FitCaptionToSymbolWidth = false;
        AssertEverythingDrawnStaysInsideBounds(provider, new IsbnSymbology(), "978-0-306-40615-7", options, fraction, moduleWidth);
    }

    /// <summary>
    /// A symbol inside a bearer bar. The frame widens the measured area by its thickness on both sides of the quiet zones and grows it above the bars and below
    /// them, and the printed line hangs from the lower bearer bar. The frame is the outermost ink on the left, the right and the top, so it fills the measured
    /// area to those edges and leaves no background showing past it.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    /// <param name="provider">The image provider.</param>
    /// <param name="fraction">The fractional part of the draw origin.</param>
    /// <param name="moduleWidth">The width of one module, in pixels.</param>
    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32, 0F, 2F)]
    [WithBlankImage(1, 1, PixelTypes.Rgba32, 0.3F, 2F)]
    [WithBlankImage(1, 1, PixelTypes.Rgba32, 0.5F, 2F)]
    [WithBlankImage(1, 1, PixelTypes.Rgba32, 0.7F, 2F)]
    [WithBlankImage(1, 1, PixelTypes.Rgba32, 0.3F, 2.3F)]
    [WithBlankImage(1, 1, PixelTypes.Rgba32, 0.7F, 2.3F)]
    public void ABearerBarStaysInsideTheMeasuredBounds<TPixel>(TestImageProvider<TPixel> provider, float fraction, float moduleWidth)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        BarcodeOptions options = CreateTextOptions(moduleWidth);
        PointF origin = new(Margin + fraction, Margin + fraction);
        RectangleF bounds = BarcodeMeasurer.MeasureRenderableBounds(new Itf14Symbology(), options, origin, "15400141288763");
        Rectangle drawn = AssertEverythingDrawnStaysInsideBounds(provider, new Itf14Symbology(), "15400141288763", options, fraction, moduleWidth);

        Assert.Equal((int)bounds.Left, drawn.Left);
        Assert.Equal((int)bounds.Right, drawn.Right);
        Assert.Equal((int)bounds.Top, drawn.Top);
    }

    /// <summary>
    /// The ink bounds hold every pixel the bars, the frame, the identification bars and the text draw. The symbol draws with no background, so every pixel
    /// that is not transparent is ink.
    /// </summary>
    /// <param name="name">The name of the case.</param>
    [Theory]
    [InlineData("IntelligentMailPackage")]
    [InlineData("AustraliaPost")]
    [InlineData("Ean5")]
    [InlineData("Isbn")]
    [InlineData("Itf14")]
    [InlineData("Gs1DataMatrix")]
    [InlineData("MaxiCode")]
    [InlineData("DotCode")]
    public void InkStaysInsideTheMeasuredInkBounds(string name)
    {
        (BarcodeSymbology symbology, string text) = name switch
        {
            "IntelligentMailPackage" => ((BarcodeSymbology)new IntelligentMailPackageSymbology("USPS TRACKING #"), "(420)123456789(92)12391234567812345670"),
            "AustraliaPost" => (new AustraliaPostSymbology(), "6254516251ABC123"),
            "Ean5" => (new Ean5Symbology(), "52495"),
            "Isbn" => (new IsbnSymbology(), "978-0-306-40615-7"),
            "Itf14" => (new Itf14Symbology(), "15400141288763"),
            "Gs1DataMatrix" => (new Gs1DataMatrixSymbology(), "(01)09521234543213(17)120508(10)ABCD1234"),
            "MaxiCode" => (new MaxiCodeSymbology(), "THIS IS A MAXICODE SYMBOL"),
            _ => (new DotCodeSymbology(), "This is DotCode"),
        };

        BarcodeOptions options = new() { Dpi = 144F, Font = BarcodeFonts.OcrB.CreateFont(10.75F) };
        PointF origin = new(Margin + 0.3F, Margin + 0.3F);
        BarcodeMetrics metrics = BarcodeMeasurer.Measure(symbology, options, origin, text);
        RectangleF renderable = metrics.RenderableBounds;
        using Image<Rgba32> image = new((int)MathF.Ceiling(renderable.Right) + Margin, (int)MathF.Ceiling(renderable.Bottom) + Margin);
        image.Mutate(x => x.Paint(canvas => canvas.DrawBarcode(symbology, options, origin, text)));

        int drawnLeft = int.MaxValue;
        int drawnTop = int.MaxValue;
        int drawnRight = int.MinValue;
        int drawnBottom = int.MinValue;
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                Span<Rgba32> row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    if (row[x].A != 0)
                    {
                        drawnLeft = Math.Min(drawnLeft, x);
                        drawnTop = Math.Min(drawnTop, y);
                        drawnRight = Math.Max(drawnRight, x);
                        drawnBottom = Math.Max(drawnBottom, y);
                    }
                }
            }
        });

        // The pixels the ink bounds touch. A pixel at index i covers the half open span from i to i + 1.
        RectangleF bounds = metrics.Bounds;
        Assert.InRange(drawnLeft, (int)MathF.Floor(bounds.Left), (int)MathF.Ceiling(bounds.Right) - 1);
        Assert.InRange(drawnRight, (int)MathF.Floor(bounds.Left), (int)MathF.Ceiling(bounds.Right) - 1);
        Assert.InRange(drawnTop, (int)MathF.Floor(bounds.Top), (int)MathF.Ceiling(bounds.Bottom) - 1);
        Assert.InRange(drawnBottom, (int)MathF.Floor(bounds.Top), (int)MathF.Ceiling(bounds.Bottom) - 1);
    }

    /// <summary>
    /// Measures the symbol, draws it at that same origin onto a larger image, and asserts that every pixel the draw call touched lies inside the measured
    /// rectangle.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    /// <param name="provider">The image provider.</param>
    /// <param name="symbology">The symbology to draw.</param>
    /// <param name="text">The text to encode.</param>
    /// <param name="options">The sizing and painting options.</param>
    /// <param name="fraction">The fractional part of the draw origin.</param>
    /// <param name="moduleWidth">The width of one module, in pixels.</param>
    /// <returns>The pixels the draw call touched, with exclusive right and bottom edges.</returns>
    private static Rectangle AssertEverythingDrawnStaysInsideBounds<TPixel>(
        TestImageProvider<TPixel> provider,
        BarcodeSymbology symbology,
        string text,
        BarcodeOptions options,
        float fraction,
        float moduleWidth)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        PointF origin = new(Margin + fraction, Margin + fraction);
        RectangleF bounds = BarcodeMeasurer.MeasureRenderableBounds(symbology, options, origin, text);
        using Image<TPixel> image = provider.GetImage();

        // The image stays transparent so that every pixel the draw call touches is rendered content: the background fill, the bars and the human readable
        // interpretation alike.
        image.Mutate(x =>
        {
            x.Resize(new Size((int)MathF.Ceiling(bounds.Right) + Margin, (int)MathF.Ceiling(bounds.Bottom) + Margin));
            x.Paint(canvas => canvas.DrawBarcode(symbology, options, origin, text));
        });

        image.DebugSave(
            provider,
            $"{fraction}_{moduleWidth}",
            "png",
            appendPixelTypeToFileName: false,
            appendSourceFileOrDescription: false);

        int drawnLeft = int.MaxValue;
        int drawnTop = int.MaxValue;
        int drawnRight = int.MinValue;
        int drawnBottom = int.MinValue;
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                Span<TPixel> row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    if (row[x].ToRgba32().A == 0)
                    {
                        continue;
                    }

                    drawnLeft = Math.Min(drawnLeft, x);
                    drawnTop = Math.Min(drawnTop, y);
                    drawnRight = Math.Max(drawnRight, x);
                    drawnBottom = Math.Max(drawnBottom, y);
                }
            }
        });

        // The pixels the measured rectangle touches. The rectangle is exact, so an edge between two pixels covers part of the pixel it falls in. A pixel at
        // index i covers the half open span from i to i + 1, so the first pixel touched holds the near edge and the last one holds the far edge.
        int left = (int)MathF.Floor(bounds.Left);
        int top = (int)MathF.Floor(bounds.Top);
        int right = (int)MathF.Ceiling(bounds.Right) - 1;
        int bottom = (int)MathF.Ceiling(bounds.Bottom) - 1;

        Assert.InRange(drawnLeft, left, right);
        Assert.InRange(drawnRight, left, right);
        Assert.InRange(drawnTop, top, bottom);
        Assert.InRange(drawnBottom, top, bottom);

        return Rectangle.FromLTRB(drawnLeft, drawnTop, drawnRight + 1, drawnBottom + 1);
    }

    /// <summary>
    /// Draws at a one point X dimension and 72 dots per inch for each pixel of module width, so one module is <paramref name="moduleWidth"/> pixels, with a 50
    /// point bar height.
    /// </summary>
    /// <param name="moduleWidth">The width of one module, in pixels.</param>
    /// <returns>The options.</returns>
    private static BarcodeOptions CreateOptions(float moduleWidth)
        => new()
        {
            Dpi = 72F * moduleWidth,
            XDimension = BarcodeSymbology.PointXDimension,
            BarHeight = 50F * BarcodeSymbology.PointXDimension,
            Background = Brushes.Solid(Color.White),
        };

    private static BarcodeOptions CreateTextOptions(float moduleWidth)
    {
        BarcodeOptions options = CreateOptions(moduleWidth);
        options.Font = BarcodeFonts.OcrB.CreateFont(21.5F / moduleWidth);
        return options;
    }
}
