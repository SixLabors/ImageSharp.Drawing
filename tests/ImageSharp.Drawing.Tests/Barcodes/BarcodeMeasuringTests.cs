// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Barcodes;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace SixLabors.ImageSharp.Drawing.Tests.Barcodes;

/// <summary>
/// Golden image tests for <see cref="BarcodeMetrics"/>, the way the text measuring tests show <c>TextMetrics</c>. Each test draws a barcode and outlines the
/// three rectangles that the canvas measures for it: the advance in green, the bounds in blue and the renderable bounds in dashed red. The background fills
/// the renderable bounds in light yellow, and a grey cross marks the origin. A reviewer sees at once whether each rectangle holds what it states.
/// </summary>
[GroupOutput("Barcodes")]
public class BarcodeMeasuringTests
{
    /// <summary>
    /// The clear margin in pixels around the renderable bounds, so the outlines and the origin cross are not cut off at the image edge.
    /// </summary>
    private const int Margin = 24;

    /// <summary>
    /// An EAN-13 symbol prints its leading digit in the left quiet zone. The measured rectangles include that digit.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    /// <param name="provider">The image provider.</param>
    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Ean13_WithText<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new Ean13Symbology(), "5901234123457", CreateTextOptions());

    /// <summary>
    /// An EAN-5 add-on symbol prints its digits above the bars. The advance and the bounds extend above the origin.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    /// <param name="provider">The image provider.</param>
    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Ean5_WithText<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new Ean5Symbology(), "52495", CreateTextOptions());

    /// <summary>
    /// An ISBN caption at the font size of the caller is wider than the bars. The advance and the bounds extend to the left of the origin.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    /// <param name="provider">The image provider.</param>
    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Isbn_WithText_UnscaledCaption<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        BarcodeOptions options = CreateTextOptions();
        options.FitCaptionToSymbolWidth = false;

        RunTest(provider, new IsbnSymbology(), "978-0-306-40615-7", options);
    }

    /// <summary>
    /// An Intelligent Mail Package symbol has identification bars above the banner and below the interpretation, at its nominal bar height. The measured
    /// rectangles include the bars and both texts. The symbology draws the text bold, as sections 2.4.1 and 2.4.2 of USPS2000508 require.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    /// <param name="provider">The image provider.</param>
    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void IntelligentMailPackage_WithText<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new IntelligentMailPackageSymbology("USPS TRACKING #"), "(420)123456789(92)12391234567812345670", CreateTextOptions());

    /// <summary>
    /// An ITF-14 symbol has a bearer bar around its quiet zones. The measured rectangles include the bearer bar.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    /// <param name="provider">The image provider.</param>
    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Itf14_WithText<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new Itf14Symbology(), "15400141288763", CreateTextOptions());

    /// <summary>
    /// A PDF417 symbol has quiet zones on all four sides. The renderable bounds include all four quiet zones.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    /// <param name="provider">The image provider.</param>
    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Pdf417<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new Pdf417Symbology(), "PDF417 Symbology Standard", CreateOptions());

    /// <summary>
    /// A GS1 DataMatrix symbol prints its interpretation below the modules. The measured rectangles include the interpretation.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    /// <param name="provider">The image provider.</param>
    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Gs1DataMatrix_WithText<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new Gs1DataMatrixSymbology(), "(01)09521234543213(17)120508(10)ABCD1234(410)9501101020917", CreateMatrixTextOptions());

    /// <summary>
    /// A MaxiCode symbol has hexagons around a circular finder. The measured rectangles include every hexagon.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    /// <param name="provider">The image provider.</param>
    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void MaxiCode<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(
            provider,
            new MaxiCodeSymbology(),
            "THIS IS A 93 CHARACTER CODE SET A MESSAGE THAT FILLS A MODE 4, UNAPPENDED, MAXICODE SYMBOL...",
            CreateOptions());

    /// <summary>
    /// A DotCode symbol has dots. The bounds end on the edges of the outermost dots.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    /// <param name="provider">The image provider.</param>
    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void DotCode<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new DotCodeSymbology(), "This is DotCode", CreateOptions());

    /// <summary>
    /// Draws the barcode with its measured rectangles outlined, on an image that holds the renderable bounds and a margin.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    /// <param name="provider">The image provider.</param>
    /// <param name="symbology">The symbology to draw.</param>
    /// <param name="text">The text to encode.</param>
    /// <param name="options">The sizing and painting options.</param>
    private static void RunTest<TPixel>(TestImageProvider<TPixel> provider, BarcodeSymbology symbology, string text, BarcodeOptions options)
        where TPixel : unmanaged, IPixel<TPixel>
        => provider.RunValidatingProcessorTest(
            x =>
            {
                // The image holds the measured area and the margin on each side, and the symbol draws at the origin that puts the measured area one margin in
                // from the corner of the image.
                RectangleF measured = BarcodeMeasurer.MeasureRenderableBounds(symbology, options, PointF.Empty, text);
                PointF origin = new(Margin - measured.X, Margin - measured.Y);
                int width = (int)MathF.Ceiling(measured.Width) + (2 * Margin);
                int height = (int)MathF.Ceiling(measured.Height) + (2 * Margin);

                x.Resize(new Size(width, height));
                x.Paint(canvas =>
                {
                    canvas.Fill(Brushes.Solid(Color.White), new RectanglePolygon(0, 0, width, height));
                    canvas.DrawBarcode(symbology, options, origin, text);

                    // The advance is relative to the origin, as the advance of text is. The bounds and the renderable bounds are absolute. The red line is
                    // dashed with gaps as long as its dashes, so an outline under it still shows where two rectangles are equal.
                    BarcodeMetrics metrics = canvas.MeasureBarcode(symbology, options, origin, text);
                    RectangleF advance = metrics.Advance;
                    RectangleF bounds = metrics.Bounds;
                    RectangleF renderable = metrics.RenderableBounds;

                    canvas.Draw(Pens.Solid(Color.Green, 1), new RectanglePolygon(origin.X + advance.X, origin.Y + advance.Y, advance.Width, advance.Height));
                    canvas.Draw(Pens.Solid(Color.Blue, 1), new RectanglePolygon(bounds.X, bounds.Y, bounds.Width, bounds.Height));
                    canvas.Draw(
                        new PatternPen(Color.Red, 1, [8F, 8F]),
                        new RectanglePolygon(renderable.X, renderable.Y, renderable.Width, renderable.Height));

                    canvas.DrawLine(Pens.Solid(Color.Gray, 1), new PointF(origin.X - 12, origin.Y), new PointF(origin.X + 12, origin.Y));
                    canvas.DrawLine(Pens.Solid(Color.Gray, 1), new PointF(origin.X, origin.Y - 12), new PointF(origin.X, origin.Y + 12));
                });
            },
            appendPixelTypeToFileName: false,
            appendSourceFileOrDescription: false);

    /// <summary>
    /// Creates the default options with a light yellow background.
    /// </summary>
    /// <returns>The options.</returns>
    private static BarcodeOptions CreateOptions()
        => new()
        {
            Background = Brushes.Solid(Color.LightYellow),
        };

    /// <summary>
    /// Creates the options for linear symbols: the options of <see cref="CreateOptions"/> with the OCR-B font at 10.75 points, the size of the linear
    /// goldens.
    /// </summary>
    /// <returns>The options.</returns>
    private static BarcodeOptions CreateTextOptions()
    {
        BarcodeOptions options = CreateOptions();
        options.Font = BarcodeFonts.OcrB.CreateFont(10.75F);

        return options;
    }

    /// <summary>
    /// Creates the options for matrix symbols: the options of <see cref="CreateOptions"/> with the OCR-B font at 9 points, the size of the matrix goldens.
    /// </summary>
    /// <returns>The options.</returns>
    private static BarcodeOptions CreateMatrixTextOptions()
    {
        BarcodeOptions options = CreateOptions();
        options.Font = BarcodeFonts.OcrB.CreateFont(9F);

        return options;
    }
}
