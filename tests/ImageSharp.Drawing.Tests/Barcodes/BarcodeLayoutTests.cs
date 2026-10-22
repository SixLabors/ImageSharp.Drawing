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
/// renders must be inside that rectangle.
/// <para>
/// Bar edges and text baselines round onto the device pixel grid. A fractional origin or a fractional module width therefore moves ink away from the unrounded
/// layout position, by up to half a pixel in either direction. The golden tests all draw from a whole pixel origin, where rounding has no effect, so they do
/// not show this.
/// </para>
/// </summary>
[GroupOutput("Barcodes")]
public class BarcodeLayoutTests
{
    /// <summary>
    /// The clear space around the measured area, in pixels. Content outside the measured rectangle draws in this margin, where the assertion sees it. It does
    /// not draw off the image, where the image clips it.
    /// </summary>
    private const int Margin = 20;

    /// <summary>
    /// A Code 39 symbol with no human readable interpretation draws its bars inside the measured bounds at each origin and module width.
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
    /// An EAN-13 symbol prints its digits below the bars, which grows the measured area downward. The bars and the digits draw inside the measured bounds at
    /// each origin and module width.
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
    /// An EAN-5 add-on symbol prints its digits above the bars. Those digits set the top band, so the ink of the digits, not the bars, is on the measured top
    /// edge. The bars and the digits draw inside the measured bounds at each origin and module width.
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
    /// By default, <see cref="BarcodeOptions.FitCaptionToSymbolWidth"/> scales an ISBN caption to the symbol width. The scale comes from the measured caption
    /// width, so the fitted caption ends level with the bars. The caption draws inside the measured bounds at each origin and module width.
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
    /// With <see cref="BarcodeOptions.FitCaptionToSymbolWidth"/> off, an ISBN caption keeps the font size of the caller and extends past the bars. The caption,
    /// not the symbol, then sets the measured width. The caption draws inside the measured bounds at each origin and module width.
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
    /// An ITF-14 symbol has a bearer bar around it. The bearer bar widens the measured area by its thickness on both sides of the quiet zones and grows it
    /// above and below the bars. The printed line hangs from the lower bearer bar. The drawing stays inside the measured bounds. The bearer bar is the
    /// outermost ink on the left, the right and the top, so the drawing fills the measured area to those edges.
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
    /// A matrix symbol at a one point X dimension draws each module as a square of the module width in pixels, 1 pixel at 72 dots per inch, 2 at 144 and 3 at
    /// 216. Every square is dark for a dark module and light for a light one, wherever the symbol starts between two pixels, so the drawing is the module
    /// matrix of the symbol. The symbol starts on the nearest whole pixel.
    /// </summary>
    /// <param name="kind">The symbology.</param>
    /// <param name="dpi">The resolution.</param>
    /// <param name="fraction">The fractional part of the draw origin.</param>
    [Theory]
    [InlineData("AztecCode", 72F, 0F)]
    [InlineData("AztecCode", 72F, 0.3F)]
    [InlineData("AztecCode", 144F, 0.7F)]
    [InlineData("DataMatrix", 72F, 0.3F)]
    [InlineData("DataMatrix", 144F, 0F)]
    [InlineData("DataMatrix", 144F, 0.7F)]
    [InlineData("QrCode", 72F, 0.7F)]
    [InlineData("QrCode", 216F, 0.3F)]
    public void DrawsEveryModuleAsASquareOfWholePixels(string kind, float dpi, float fraction)
    {
        BarcodeSymbology symbology = kind switch
        {
            "AztecCode" => new AztecCodeSymbology(),
            "DataMatrix" => new DataMatrixSymbology(),
            _ => new QrCodeSymbology(),
        };

        const string text = "HELLO WORLD 12345";
        BarcodeOptions options = new() { Dpi = dpi, XDimension = SymbolEncoder.PointXDimension, IncludeQuietZones = false };
        (int columns, int rows, bool[] modules) = SymbolEncoder.Read(
            symbology,
            text,
            options,
            static (ref symbol) => (symbol.Columns, symbol.MatrixRows, symbol.Modules.ToArray()));

        int size = (int)(dpi / 72F);
        PointF origin = new(Margin + fraction, Margin + fraction);
        using Image<L8> image = new((columns * size) + (2 * Margin), (rows * size) + (2 * Margin), new L8(255));

        image.Mutate(x => x.Paint(canvas => canvas.DrawBarcode(symbology, options, origin, text)));

        int left = (int)MathF.Round(origin.X);
        int top = (int)MathF.Round(origin.Y);
        for (int y = 0; y < image.Height; y++)
        {
            for (int x = 0; x < image.Width; x++)
            {
                int column = (x - left) / size;
                int row = (y - top) / size;
                bool inside = x >= left && y >= top && column < columns && row < rows;
                bool expected = inside && modules[(row * columns) + column];
                Assert.True(expected == image[x, y].PackedValue < 128, $"The pixel {x}, {y} does not match its module.");
            }
        }
    }

    /// <summary>
    /// A postal symbol draws every bar at one width and every space at one width in whole pixels, so the bars keep one pitch: Table 11 of the Mailmark barcode
    /// definition document requires that "they must be equally spaced", and page 12 of the Japan Post manual gives one bar width, "バー幅 0.6×a/10". A column of
    /// the drawing is dark when any of its pixels is dark, so a run of dark columns is a bar whatever its height. The symbol draws at its nominal size at each
    /// resolution, wherever it starts between two pixels.
    /// </summary>
    /// <param name="kind">The symbology.</param>
    [Theory]
    [InlineData("AustraliaPost")]
    [InlineData("RoyalMail")]
    [InlineData("Kix")]
    [InlineData("Daft")]
    [InlineData("FourStateMailmark")]
    [InlineData("JapanPost")]
    [InlineData("IntelligentMail")]
    [InlineData("Postnet")]
    [InlineData("Planet")]
    public void DrawsEveryPostalBarAtOneWidthAndOnePitch(string kind)
    {
        (BarcodeSymbology symbology, string text) = kind switch
        {
            "AustraliaPost" => ((BarcodeSymbology)new AustraliaPostSymbology(), "6254516251ABC123"),
            "RoyalMail" => (new RoyalMailSymbology(), "LE28HS9Z"),
            "Kix" => (new KixSymbology(), "1231FZ13XHS"),
            "Daft" => (new DaftSymbology(), "FATDAFTDAD"),
            "FourStateMailmark" => (new FourStateMailmarkSymbology(), "21B2254800659JW5O9QA6Y"),
            "JapanPost" => (new JapanPostSymbology(), "15400233-16-4-205"),
            "IntelligentMail" => (new IntelligentMailSymbology(), "0123456709498765432101234567891"),
            "Postnet" => (new PostnetSymbology(), "01234"),
            _ => (new PlanetSymbology(), "01234567890"),
        };

        // Every failing case is collected, so a failure shows every resolution and origin it happens at.
        List<string> failures = [];
        foreach (float dpi in (ReadOnlySpan<float>)[72F, 96F, 144F, 150F, 203.2F, 300F, 600F])
        {
            foreach (float fraction in (ReadOnlySpan<float>)[0F, 0.3F, 0.5F, 0.7F])
            {
                BarcodeOptions options = new() { Dpi = dpi, IncludeQuietZones = false };
                (HashSet<int> bars, HashSet<int> spaces) = MeasurePostalRuns(symbology, text, options, new PointF(Margin + fraction, Margin + fraction));
                if (bars.Count != 1 || spaces.Count != 1)
                {
                    failures.Add($"{dpi} dpi at {fraction}: bars {string.Join('/', bars)} px, spaces {string.Join('/', spaces)} px");
                }
            }
        }

        Assert.True(failures.Count == 0, string.Join("; ", failures));
    }

    /// <summary>
    /// The drawn bars keep the bar density of the specification at every resolution and at both ends of the bar width range. Mailmark, Table 11 of the Mailmark
    /// barcode definition document: "Bar pitch ... 20 - 24 bars per inch (25.4mm)", with a bar width of "0.38 - 0.63". Australia Post, page 13 of the
    /// customer barcoding technical specifications: "a bar density of 22 to 25 bars per 25.4mm", with a bar width of 0.4 to 0.6 mm. Intelligent Mail, section
    /// 2.3.1 of USPS-B-3200: "an overall barcode pitch of 22±2 bars per inch", with a bar width of "0.020±0.005 inch". The bars per inch are the resolution
    /// divided by the drawn pitch, a bar and a space in whole pixels.
    /// </summary>
    /// <param name="kind">The symbology.</param>
    /// <param name="xDimension">The bar width in millimetres, or <see langword="null"/> for the nominal bar width.</param>
    /// <param name="minimumBarsPerInch">The fewest bars per inch the specification permits.</param>
    /// <param name="maximumBarsPerInch">The most bars per inch the specification permits.</param>
    [Theory]
    [InlineData("FourStateMailmark", null, 20, 24)]
    [InlineData("FourStateMailmark", 0.38, 20, 24)]
    [InlineData("FourStateMailmark", 0.63, 20, 24)]
    [InlineData("AustraliaPost", null, 22, 25)]
    [InlineData("AustraliaPost", 0.4, 22, 25)]
    [InlineData("AustraliaPost", 0.6, 22, 25)]
    [InlineData("IntelligentMail", null, 20, 24)]
    [InlineData("IntelligentMail", 0.015 * 25.4, 20, 24)]
    [InlineData("IntelligentMail", 0.025 * 25.4, 20, 24)]
    public void DrawsThePostalBarDensityOfTheSpecification(string kind, double? xDimension, int minimumBarsPerInch, int maximumBarsPerInch)
    {
        (BarcodeSymbology symbology, string text) = kind switch
        {
            "FourStateMailmark" => ((BarcodeSymbology)new FourStateMailmarkSymbology(), "21B2254800659JW5O9QA6Y"),
            "AustraliaPost" => (new AustraliaPostSymbology(), "6254516251ABC123"),
            _ => (new IntelligentMailSymbology(), "0123456709498765432101234567891"),
        };

        // Every failing case is collected, so a failure shows every resolution it happens at.
        List<string> failures = [];
        foreach (float dpi in (ReadOnlySpan<float>)[72F, 96F, 144F, 150F, 203.2F, 300F, 600F])
        {
            BarcodeOptions options = new() { Dpi = dpi, XDimension = xDimension, IncludeQuietZones = false };
            (HashSet<int> bars, HashSet<int> spaces) = MeasurePostalRuns(symbology, text, options, new PointF(Margin, Margin));
            double barsPerInch = dpi / (double)(bars.Single() + spaces.Single());
            if (barsPerInch < minimumBarsPerInch || barsPerInch > maximumBarsPerInch)
            {
                failures.Add($"{dpi} dpi: {barsPerInch:0.##} bars per inch");
            }
        }

        Assert.True(failures.Count == 0, string.Join("; ", failures));
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

        AssertDrawnPixelsInside(image, metrics.Bounds);
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

        return AssertDrawnPixelsInside(image, bounds);
    }

    /// <summary>
    /// Finds the pixels that are not transparent and asserts that each of them is inside the rectangle. The image starts transparent, so these pixels are the
    /// pixels the draw call touched.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    /// <param name="image">The drawn image.</param>
    /// <param name="bounds">The measured rectangle.</param>
    /// <returns>The pixels the draw call touched, with exclusive right and bottom edges.</returns>
    private static Rectangle AssertDrawnPixelsInside<TPixel>(Image<TPixel> image, RectangleF bounds)
        where TPixel : unmanaged, IPixel<TPixel>
    {
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
    /// Creates options at the default X dimension, a bar height of 50 default X dimensions, a resolution of 72 dots per inch for each pixel of
    /// <paramref name="moduleWidth"/> and a white background.
    /// </summary>
    /// <param name="moduleWidth">The width of one module, in pixels.</param>
    /// <returns>The options.</returns>
    /// <summary>
    /// Draws a postal symbol and measures its bars and spaces in whole pixels. A column of the drawing is dark when any of its pixels is dark, so a run of dark
    /// columns is a bar whatever its height.
    /// </summary>
    /// <param name="symbology">The postal symbology.</param>
    /// <param name="text">The text to encode.</param>
    /// <param name="options">The options to draw with.</param>
    /// <param name="origin">The top left corner of the symbol.</param>
    /// <returns>The distinct bar widths and the distinct space widths in pixels.</returns>
    private static (HashSet<int> Bars, HashSet<int> Spaces) MeasurePostalRuns(BarcodeSymbology symbology, string text, BarcodeOptions options, PointF origin)
    {
        RectangleF bounds = BarcodeMeasurer.MeasureRenderableBounds(symbology, options, origin, text);
        using Image<L8> image = new((int)MathF.Ceiling(bounds.Right) + Margin, (int)MathF.Ceiling(bounds.Bottom) + Margin, new L8(255));
        image.Mutate(x => x.Paint(canvas => canvas.DrawBarcode(symbology, options, origin, text)));

        // One pass over the rows marks every column that holds a dark pixel.
        bool[] darkColumns = new bool[image.Width];
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                Span<L8> row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    darkColumns[x] |= row[x].PackedValue < 128;
                }
            }
        });

        HashSet<int> bars = [];
        HashSet<int> spaces = [];
        int run = 0;
        bool? dark = null;
        foreach (bool column in darkColumns)
        {
            if (column != dark)
            {
                if (dark == true)
                {
                    bars.Add(run);
                }
                else if (dark == false && bars.Count > 0 && column)
                {
                    spaces.Add(run);
                }

                dark = column;
                run = 0;
            }

            run++;
        }

        return (bars, spaces);
    }

    private static BarcodeOptions CreateOptions(float moduleWidth)
        => new()
        {
            Dpi = 72F * moduleWidth,
            XDimension = BarcodeSymbology.DefaultXDimension,
            BarHeight = 50F * BarcodeSymbology.DefaultXDimension,
            Background = Brushes.Solid(Color.White),
        };

    /// <summary>
    /// Creates the options of <see cref="CreateOptions"/> with the OCR-B font at 21.5 points divided by the module width, which keeps the text at one size
    /// in pixels at each module width.
    /// </summary>
    /// <param name="moduleWidth">The width of one module, in pixels.</param>
    /// <returns>The options.</returns>
    private static BarcodeOptions CreateTextOptions(float moduleWidth)
    {
        BarcodeOptions options = CreateOptions(moduleWidth);
        options.Font = BarcodeFonts.OcrB.CreateFont(21.5F / moduleWidth);

        return options;
    }
}
