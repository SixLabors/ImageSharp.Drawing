// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Barcodes;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace SixLabors.ImageSharp.Drawing.Tests.Barcodes;

/// <summary>
/// Golden image tests for <see cref="DrawingCanvas.DrawBarcode(BarcodeSymbology, string, BarcodeOptions, PointF)"/>.
/// The encoder tests already pin the module sequences, so these tests cover the rendering layer: module
/// scaling, quiet zones, guard bar extension, background painting, brushes and the human readable text.
/// </summary>
[GroupOutput("Barcodes")]
public class DrawBarcodeTests
{
    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Ean13<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new Ean13Symbology(), "5901234123457", CreateOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Ean13_WithText<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new Ean13Symbology(), "5901234123457", CreateTextOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Ean8<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new Ean8Symbology(), "96385074", CreateOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void UpcA_WithText<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new UpcASymbology(), "036000291452", CreateTextOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void UpcE_WithText<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new UpcESymbology(), "01234565", CreateTextOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Ean5_WithText<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new Ean5Symbology(), "52495", CreateTextOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Ean2<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new Ean2Symbology(), "05", CreateOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Isbn_WithText<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new IsbnSymbology(), "978-0-306-40615-7", CreateTextOptions());

    /// <summary>
    /// The caption prints at the size the caller gave it, rather than being scaled to the symbol width, so
    /// it overhangs the bars and sets the width of the drawn area itself.
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

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Ismn_WithText<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new IsmnSymbology(), "M-2306-7118", CreateTextOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Issn_WithText<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new IssnSymbology(), "0317-8471", CreateTextOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Mands_WithText<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new MandsSymbology(), "0642118", CreateTextOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Code128<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new Code128Symbology(), "CODE128", CreateOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Code128_WithText<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new Code128Symbology(), "CODE128", CreateTextOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Gs1_128<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new Gs1128Symbology(), "(01)09521234543213(3103)000123", CreateOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Gs1_128_WithText<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new Gs1128Symbology(), "(01)09521234543213(3103)000123", CreateTextOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Ean14<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new Ean14Symbology(), "(01)09521234543213", CreateOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Ean14_WithText<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new Ean14Symbology(), "(01)09521234543213", CreateTextOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Sscc18<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new Sscc18Symbology(), "(00)106141411234567897", CreateOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Sscc18_WithText<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new Sscc18Symbology(), "(00)106141411234567897", CreateTextOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void HibcCode128<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new HibcCode128Symbology(), "A123BJC5D6E71", CreateOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void HibcCode128_WithText<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new HibcCode128Symbology(), "A123BJC5D6E71", CreateTextOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Code39<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new Code39Symbology(), "CODE39", CreateOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Code39_WithText<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new Code39Symbology(), "CODE39", CreateTextOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Code39Extended<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new Code39ExtendedSymbology(), "Code39", CreateOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Code39Extended_WithText<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new Code39ExtendedSymbology(), "Code39", CreateTextOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void HibcCode39<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new HibcCode39Symbology(), "A123BJC5D6E71", CreateOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void HibcCode39_WithText<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new HibcCode39Symbology(), "A123BJC5D6E71", CreateTextOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Pzn8<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new Pzn8Symbology(), "03752864", CreateOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Pzn8_WithText<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new Pzn8Symbology(), "03752864", CreateTextOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Pzn7<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new Pzn7Symbology(), "1234562", CreateOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Pzn7_WithText<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new Pzn7Symbology(), "1234562", CreateTextOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Code93<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new Code93Symbology(), "CODE93", CreateOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Code93_WithText<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new Code93Symbology(), "CODE93", CreateTextOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Code93Extended<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new Code93ExtendedSymbology(), "Code93", CreateOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Code93Extended_WithText<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new Code93ExtendedSymbology(), "Code93", CreateTextOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Interleaved2Of5<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new Interleaved2Of5Symbology(), "1234", CreateOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Interleaved2Of5_WithText<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new Interleaved2Of5Symbology(), "1234", CreateTextOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Leitcode<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new LeitcodeSymbology(), "2134807501640", CreateOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Leitcode_WithText<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new LeitcodeSymbology(), "2134807501640", CreateTextOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Identcode<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new IdentcodeSymbology(), "56310243031", CreateOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Identcode_WithText<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new IdentcodeSymbology(), "56310243031", CreateTextOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Industrial2Of5<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new Industrial2Of5Symbology(), "1234", CreateOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Industrial2Of5_WithText<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new Industrial2Of5Symbology(), "1234", CreateTextOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Iata2Of5<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new Iata2Of5Symbology(), "1234", CreateOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Iata2Of5_WithText<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new Iata2Of5Symbology(), "1234", CreateTextOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Matrix2Of5<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new Matrix2Of5Symbology(), "1234", CreateOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Matrix2Of5_WithText<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new Matrix2Of5Symbology(), "1234", CreateTextOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Coop2Of5<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new Coop2Of5Symbology(), "1234", CreateOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Coop2Of5_WithText<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new Coop2Of5Symbology(), "1234", CreateTextOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Datalogic2Of5<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new Datalogic2Of5Symbology(), "1234", CreateOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Datalogic2Of5_WithText<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new Datalogic2Of5Symbology(), "1234", CreateTextOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Itf14<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new Itf14Symbology(), "15400141288763", CreateOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Itf14_WithText<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new Itf14Symbology(), "15400141288763", CreateTextOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Code32<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new Code32Symbology(), "012345676", CreateOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Code32_WithText<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new Code32Symbology(), "012345676", CreateTextOptions());

    /// <summary>
    /// Disabling the quiet zones must shift the first bar to the draw origin, and a non-solid brush must
    /// flow through to the bar fill unchanged.
    /// </summary>
    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Ean13_NoQuietZones_GradientBars<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        BarcodeOptions options = CreateOptions();
        options.IncludeQuietZones = false;
        options.BarBrush = new LinearGradientBrush(
            new PointF(0, 0),
            new PointF(220, 0),
            GradientRepetitionMode.None,
            new ColorStop(0, Color.DarkBlue),
            new ColorStop(1, Color.DarkRed));

        RunTest(provider, new Ean13Symbology(), "5901234123457", options);
    }

    private static void RunTest<TPixel>(TestImageProvider<TPixel> provider, BarcodeSymbology symbology, string text, BarcodeOptions options)
        where TPixel : unmanaged, IPixel<TPixel>
        => provider.RunValidatingProcessorTest(
            x =>
            {
                // The image is the symbol, with no slack around it. The provider hands over a one pixel
                // canvas, the measurer reports the area the symbol needs, and the canvas grows to it.
                // Slack would hide a symbol that grew, and a shortfall would clip one.
                RectangleF bounds = BarcodeMeasurer.MeasureRenderableBounds(symbology, text, options, PointF.Empty);
                x.Resize(new Size((int)MathF.Ceiling(bounds.Width), (int)MathF.Ceiling(bounds.Height)));
                x.Paint(canvas => canvas.DrawBarcode(symbology, text, options, PointF.Empty));
            },
            appendPixelTypeToFileName: false,
            appendSourceFileOrDescription: false);

    private static BarcodeOptions CreateOptions()
        => new()
        {
            ModuleWidth = 2F,
            BarHeight = 100F,
            Background = Brushes.Solid(Color.White),
        };

    private static BarcodeOptions CreateTextOptions()
    {
        BarcodeOptions options = CreateOptions();

        // GS1 prints the interpretation 2.75mm high at the 0.33mm X-dimension, so at ModuleWidth 2 it
        // is 16.7px of ink. The cap ink of this font stands 0.678 em, giving 24.6 points, but the digit cell
        // cap holds it to what fits a 7 module cell.
        options.Font = BarcodeFonts.OcrB.CreateFont(21.5F);
        return options;
    }
}
