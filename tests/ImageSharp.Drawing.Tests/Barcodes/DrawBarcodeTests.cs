// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Barcodes;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace SixLabors.ImageSharp.Drawing.Tests.Barcodes;

/// <summary>
/// Golden image tests for <see cref="DrawingCanvas.DrawBarcode(BarcodeSymbology, BarcodeOptions, PointF, ReadOnlySpan{char})"/>. The encoder tests already
/// pin the module sequences, so these tests cover the rendering layer: module scaling, quiet zones, guard bar extension, background painting, brushes and the
/// human readable text. Each test draws one symbol at the default options, with or without the OCR-B font, and compares the drawing with its reference image.
/// A test has its own summary only where it proves more than that.
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
    /// The caption prints at the size the caller gave it, rather than being scaled to the symbol width, so it overhangs the bars and sets the width of the
    /// drawn area itself.
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
    public void IntelligentMailPackage_WithText<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new IntelligentMailPackageSymbology("USPS TRACKING #"), "(420)123456789(92)12391234567812345670", CreateTextOptions());

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
    public void Codabar<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new CodabarSymbology(), "A40156B", CreateOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Codabar_WithText<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new CodabarSymbology(), "A40156B", CreateTextOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Code11<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new Code11Symbology(), "123-45", CreateOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Code11_WithText<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new Code11Symbology(), "123-45", CreateTextOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Msi<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new MsiSymbology(), "1234567", CreateOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Msi_WithText<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new MsiSymbology(), "1234567", CreateTextOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Plessey<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new PlesseySymbology(), "01234ABCD", CreateOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Plessey_WithText<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new PlesseySymbology(), "01234ABCD", CreateTextOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Telepen<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new TelepenSymbology(), "ABC123", CreateOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Telepen_WithText<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new TelepenSymbology(), "ABC123", CreateTextOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void TelepenNumeric<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new TelepenNumericSymbology(), "1234", CreateOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void TelepenNumeric_WithText<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new TelepenNumericSymbology(), "1234", CreateTextOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void PosiCode<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new PosiCodeSymbology(), "ABC123", CreateOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void PosiCode_WithText<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new PosiCodeSymbology(), "ABC123", CreateTextOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Bc412<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new Bc412Symbology(), "BC412AB", CreateOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Bc412_WithText<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new Bc412Symbology(), "BC412AB", CreateTextOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void ChannelCode<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new ChannelCodeSymbology(), "3493", CreateOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void ChannelCode_WithText<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new ChannelCodeSymbology(), "3493", CreateTextOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Pharmacode<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new PharmacodeSymbology(), "117480", CreateOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void TwoTrackPharmacode<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new TwoTrackPharmacodeSymbology(), "117480", CreateOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Flattermarken<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new FlattermarkenSymbology(), "11099", CreateOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Flattermarken_WithText<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new FlattermarkenSymbology(), "11099", CreateTextOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void RawLinear<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new RawLinearSymbology(), "331132131313411122131313121312", CreateOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void FacingIdentificationMark<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new FacingIdentificationMarkSymbology(), "A", CreateOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Postnet<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new PostnetSymbology(), "01234", CreateOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Planet<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new PlanetSymbology(), "01234567890", CreateOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void RoyalMail<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new RoyalMailSymbology(), "LE28HS9Z", CreateOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void RoyalMail_WithText<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new RoyalMailSymbology(), "LE28HS9Z", CreateTextOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Kix<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new KixSymbology(), "1231FZ13XHS", CreateOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Kix_WithText<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new KixSymbology(), "1231FZ13XHS", CreateTextOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Daft<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new DaftSymbology(), "FATDAFTDAD", CreateOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void IntelligentMail<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new IntelligentMailSymbology(), "0123456709498765432101234567891", CreateOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void IntelligentMail_WithText<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new IntelligentMailSymbology(), "0123456709498765432101234567891", CreateTextOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void AustraliaPost<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new AustraliaPostSymbology(), "6254516251ABC123", CreateOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void AustraliaPost_WithText<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new AustraliaPostSymbology(), "6254516251ABC123", CreateTextOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void JapanPost<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new JapanPostSymbology(), "15400233-16-4-205", CreateOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void FourStateMailmark<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new FourStateMailmarkSymbology(), "21B2254800659JW5O9QA6Y", CreateOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Code16K<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new Code16KSymbology(), "Abcd-1234567890-wxyZ", CreateOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Code16K_WithText<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new Code16KSymbology(), "Abcd-1234567890-wxyZ", CreateTextOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Code49<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new Code49Symbology(), "MULTIPLE ROWS IN CODE 49", CreateOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Code49_WithText<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new Code49Symbology(), "MULTIPLE ROWS IN CODE 49", CreateTextOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void CodablockF<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new CodablockFSymbology(), "CODABLOCK F 34567890123456789010040digit", CreateOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void CodablockF_WithText<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new CodablockFSymbology(), "CODABLOCK F 34567890123456789010040digit", CreateTextOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void HibcCodablockF<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new HibcCodablockFSymbology(), "A123BJC5D6E71", CreateOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void HibcCodablockF_WithText<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new HibcCodablockFSymbology(), "A123BJC5D6E71", CreateTextOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Gs1DataBarOmnidirectional<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new Gs1DataBarOmnidirectionalSymbology(), "(01)09521234543213", CreateOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Gs1DataBarOmnidirectional_WithText<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new Gs1DataBarOmnidirectionalSymbology(), "(01)09521234543213", CreateTextOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Gs1DataBarTruncated<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new Gs1DataBarTruncatedSymbology(), "(01)09521234543213", CreateOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Gs1DataBarTruncated_WithText<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new Gs1DataBarTruncatedSymbology(), "(01)09521234543213", CreateTextOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Gs1DataBarStacked<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new Gs1DataBarStackedSymbology(), "(01)09521234543213", CreateOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Gs1DataBarStacked_WithText<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new Gs1DataBarStackedSymbology(), "(01)09521234543213", CreateTextOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Gs1DataBarStackedOmnidirectional<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new Gs1DataBarStackedOmnidirectionalSymbology(), "(01)09521234543213", CreateOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Gs1DataBarStackedOmnidirectional_WithText<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new Gs1DataBarStackedOmnidirectionalSymbology(), "(01)09521234543213", CreateTextOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Gs1DataBarLimited<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new Gs1DataBarLimitedSymbology(), "(01)15012345678907", CreateOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Gs1DataBarLimited_WithText<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new Gs1DataBarLimitedSymbology(), "(01)15012345678907", CreateTextOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Gs1DataBarExpanded<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new Gs1DataBarExpandedSymbology(), "(01)90614141000015(3202)000150", CreateOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Gs1DataBarExpanded_WithText<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new Gs1DataBarExpandedSymbology(), "(01)90614141000015(3202)000150", CreateTextOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Gs1DataBarExpandedStacked<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new Gs1DataBarExpandedStackedSymbology(), "(01)90614141000015(3202)000150", CreateOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Gs1DataBarExpandedStacked_WithText<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new Gs1DataBarExpandedStackedSymbology(), "(01)90614141000015(3202)000150", CreateTextOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Gs1NorthAmericanCoupon<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new Gs1NorthAmericanCouponSymbology(), "(8110)106141416543213500110000310123196000", CreateOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Gs1NorthAmericanCoupon_WithText<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new Gs1NorthAmericanCouponSymbology(), "(8110)106141416543213500110000310123196000", CreateTextOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Pdf417<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new Pdf417Symbology(), "PDF417 Symbology Standard", CreateOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void CompactPdf417<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new CompactPdf417Symbology(), "PDF417 Symbology Standard", CreateOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void MicroPdf417<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new MicroPdf417Symbology(), "MicroPDF417", CreateOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void HibcPdf417<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new HibcPdf417Symbology(), "A123BJC5D6E71", CreateOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void HibcMicroPdf417<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new HibcMicroPdf417Symbology(), "A123BJC5D6E71", CreateOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void DataMatrix<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new DataMatrixSymbology(), "This is Data Matrix!", CreateOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void DataMatrixRectangular<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new DataMatrixRectangularSymbology(), "This is Data Matrix!", CreateOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void DataMatrixRectangularExtension<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new DataMatrixRectangularExtensionSymbology(), "This is Data Matrix rectangular extension", CreateOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Gs1DataMatrix<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new Gs1DataMatrixSymbology(), "(01)09521234543213(17)120508(10)ABCD1234(410)9501101020917", CreateOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Gs1DataMatrix_WithText<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new Gs1DataMatrixSymbology(), "(01)09521234543213(17)120508(10)ABCD1234(410)9501101020917", CreateMatrixTextOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Gs1DataMatrixRectangular<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new Gs1DataMatrixRectangularSymbology(), "(01)09521234543213(17)120508(10)ABCD1234(410)9501101020917", CreateOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Gs1DataMatrixRectangular_WithText<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new Gs1DataMatrixRectangularSymbology(), "(01)09521234543213(17)120508(10)ABCD1234(410)9501101020917", CreateMatrixTextOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void HibcDataMatrix<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new HibcDataMatrixSymbology(), "A999BJC5D6E71", CreateOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void HibcDataMatrix_WithText<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new HibcDataMatrixSymbology(), "A999BJC5D6E71", CreateMatrixTextOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void HibcDataMatrixRectangular<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new HibcDataMatrixRectangularSymbology(), "A999BJC5D6E71", CreateOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void HibcDataMatrixRectangular_WithText<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new HibcDataMatrixRectangularSymbology(), "A999BJC5D6E71", CreateMatrixTextOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Mailmark2D<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new Mailmark2DSymbology(Mailmark2DType.Type29), "JGB 012100123412345678AB19XY1A 0             www.xyz.com", CreateOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Gs1DigitalLinkDataMatrix<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new Gs1DigitalLinkDataMatrixSymbology(), "https://id.gs1.org/01/09521234543213/22/ABC%2D123?99=XYZ-987", CreateOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void QrCode<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new QrCodeSymbology(), "https://sixlabors.com/", CreateOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void MicroQrCode<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new MicroQrCodeSymbology(), "Micro QR!", CreateOptions());

    /// <summary>
    /// Example 1 of Annex A of the QR-bill guidelines, Table 17, draws at the 46 mm width of section 6.4 with the Swiss cross of section 6.4.2 over its
    /// centre.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    /// <param name="provider">The image provider.</param>
    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void SwissQrCode<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        const string text = "SPC\n0200\n1\nCH6431961000004421557\nS\nMax Muster & Söhne\nMusterstrasse\n123\n8000\nSeldwyla\nCH\n\n\n\n\n\n\n\n50.00\nCHF\n"
            + "S\nSimon Muster\nMusterstrasse\n1\n8000\nSeldwyla\nCH\nQRR\n000008207791225857421286694\nPayment of travel\nEPD\n\n\n";

        RunTest(provider, new SwissQrCodeSymbology(), text, CreateOptions());
    }

    /// <summary>
    /// The 93 character message of Figure 2 of ISO/IEC 16023 draws at the nominal X dimension of 0.88 mm of Table 7.
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

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void CodeOne<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new CodeOneSymbology(), "Code One", CreateOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void CodeOne_VersionS<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new CodeOneSymbology(CodeOneVersion.S20), "123456789012", CreateOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void CodeOne_VersionT<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new CodeOneSymbology(CodeOneVersion.T16), "T16", CreateOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void HanXin<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new HanXinSymbology(), "Han Xin Code", CreateOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void DotCode<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new DotCodeSymbology(), "This is DotCode", CreateOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Gs1DotCode<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new Gs1DotCodeSymbology(), "(01)09521234543213(17)240101(10)ABC123", CreateOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Gs1DotCode_WithText<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new Gs1DotCodeSymbology(), "(01)09521234543213(17)240101(10)ABC123", CreateMatrixTextOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Ultracode<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new UltracodeSymbology(), "Awesome colours!", CreateOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void D3Aqr<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new D3AqrSymbology(2842), "HTTPS://ID.GS1.ORG/01/09521234543213/22/ABC%2D123?99=XYZ-987", CreateOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void RectangularMicroQrCode<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new RectangularMicroQrCodeSymbology(), "https://sixlabors.com/", CreateOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Gs1QrCode<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new Gs1QrCodeSymbology(), "(01)09521234543213(17)120508(10)ABCD1234(410)9501101020917", CreateOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Gs1QrCode_WithText<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new Gs1QrCodeSymbology(), "(01)09521234543213(17)120508(10)ABCD1234(410)9501101020917", CreateMatrixTextOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void HibcQrCode<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new HibcQrCodeSymbology(), "A999BJC5D6E71", CreateOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void HibcQrCode_WithText<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new HibcQrCodeSymbology(), "A999BJC5D6E71", CreateMatrixTextOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void AztecCode<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new AztecCodeSymbology(), "This is Aztec Code", CreateOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void AztecCode_FullRange<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new AztecCodeSymbology(23, 6), "This is Aztec Code", CreateOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void AztecCodeCompact<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new AztecCodeCompactSymbology(), "Compact", CreateOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void AztecRune<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new AztecRuneSymbology(), "25", CreateOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void HibcAztecCode<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new HibcAztecCodeSymbology(), "A999BJC5D6E71", CreateOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void HibcAztecCode_WithText<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new HibcAztecCodeSymbology(), "A999BJC5D6E71", CreateMatrixTextOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Gs1DigitalLinkQrCode<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new Gs1DigitalLinkQrCodeSymbology(), "https://id.gs1.org/01/09521234543213/22/ABC%2D123?99=XYZ-987", CreateOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Gs1DigitalLinkQrCode_WithText<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new Gs1DigitalLinkQrCodeSymbology(), "https://id.gs1.org/01/09521234543213/22/ABC%2D123?99=XYZ-987", CreateMatrixTextOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Gs1DigitalLinkDataMatrix_WithText<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(
            provider,
            new Gs1DigitalLinkDataMatrixSymbology(),
            "https://id.gs1.org/01/09521234543213/22/ABC%2D123?99=XYZ-987",
            CreateMatrixTextOptions());

    /// <summary>
    /// Without the quiet zones the symbol starts at the draw origin, the text still starts one quiet zone below the symbol, and no clear space surrounds the
    /// text.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    /// <param name="provider">The image provider.</param>
    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Gs1DataMatrix_WithText_NoQuietZones<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        BarcodeOptions options = CreateMatrixTextOptions();
        options.IncludeQuietZones = false;

        RunTest(provider, new Gs1DataMatrixSymbology(), "(01)09521234543213(17)120508(10)ABCD1234(410)9501101020917", options);
    }

    /// <summary>
    /// Without the quiet zones a centred line wider than the symbol shifts the rendering right, so the line starts at the draw origin.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    /// <param name="provider">The image provider.</param>
    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void HibcDataMatrix_WithText_NoQuietZones<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        BarcodeOptions options = CreateMatrixTextOptions();
        options.IncludeQuietZones = false;

        RunTest(provider, new HibcDataMatrixSymbology(), "A999BJC5D6E71", options);
    }

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
    /// Without the quiet zones the first bar starts at the draw origin. A gradient bar brush fills the bars unchanged.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    /// <param name="provider">The image provider.</param>
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

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Gs1CompositeComponentAlone<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new Gs1CompositeComponentSymbology(), "(01)09521234543213(3103)000123", CreateOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Gs1CompositeComponentAlone_WithText<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new Gs1CompositeComponentSymbology(), "(01)09521234543213(3103)000123", CreateTextOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Ean13Composite<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new Ean13CompositeSymbology(), "3312345678903", "(99)1234-abcd", CreateOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Ean13Composite_WithText<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new Ean13CompositeSymbology(), "3312345678903", "(99)1234-abcd", CreateTextOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Ean8Composite<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new Ean8CompositeSymbology(), "12345670", "(21)A12345678", CreateOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Ean8Composite_WithText<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new Ean8CompositeSymbology(), "12345670", "(21)A12345678", CreateTextOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void UpcAComposite<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new UpcACompositeSymbology(), "416000336108", "(99)1234-abcd", CreateOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void UpcAComposite_WithText<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new UpcACompositeSymbology(), "416000336108", "(99)1234-abcd", CreateTextOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void UpcEComposite<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new UpcECompositeSymbology(), "01234558", "(15)021231", CreateOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void UpcEComposite_WithText<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new UpcECompositeSymbology(), "01234558", "(15)021231", CreateTextOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Gs1128Composite<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new Gs1128CompositeSymbology(), "(01)03212345678906", "(21)A1B2C3D4E5F6G7H8", CreateOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Gs1128Composite_WithText<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new Gs1128CompositeSymbology(), "(01)03212345678906", "(21)A1B2C3D4E5F6G7H8", CreateTextOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Gs1128CompositeCcC<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(
            provider,
            new Gs1128CompositeSymbology(Gs1CompositeComponent.CcC),
            "(01)93812345678901",
            "(10)ABCD123456(410)3898765432108",
            CreateOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Gs1128CompositeCcC_WithText<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(
            provider,
            new Gs1128CompositeSymbology(Gs1CompositeComponent.CcC),
            "(01)93812345678901",
            "(10)ABCD123456(410)3898765432108",
            CreateTextOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Gs1DataBarOmnidirectionalComposite<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new Gs1DataBarOmnidirectionalCompositeSymbology(), "(01)03612345678904", "(11)990102", CreateOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Gs1DataBarOmnidirectionalComposite_WithText<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new Gs1DataBarOmnidirectionalCompositeSymbology(), "(01)03612345678904", "(11)990102", CreateTextOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Gs1DataBarTruncatedComposite<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new Gs1DataBarTruncatedCompositeSymbology(), "(01)03612345678904", "(11)990102", CreateOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Gs1DataBarTruncatedComposite_WithText<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new Gs1DataBarTruncatedCompositeSymbology(), "(01)03612345678904", "(11)990102", CreateTextOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Gs1DataBarStackedComposite<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new Gs1DataBarStackedCompositeSymbology(), "(01)03412345678900", "(17)010200", CreateOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Gs1DataBarStackedComposite_WithText<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new Gs1DataBarStackedCompositeSymbology(), "(01)03412345678900", "(17)010200", CreateTextOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Gs1DataBarStackedOmnidirectionalComposite<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new Gs1DataBarStackedOmnidirectionalCompositeSymbology(), "(01)03612345678904", "(11)990102", CreateOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Gs1DataBarStackedOmnidirectionalComposite_WithText<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new Gs1DataBarStackedOmnidirectionalCompositeSymbology(), "(01)03612345678904", "(11)990102", CreateTextOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Gs1DataBarLimitedComposite<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new Gs1DataBarLimitedCompositeSymbology(), "(01)13112345678906", "(17)010615(10)A123456", CreateOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Gs1DataBarLimitedComposite_WithText<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new Gs1DataBarLimitedCompositeSymbology(), "(01)13112345678906", "(17)010615(10)A123456", CreateTextOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Gs1DataBarExpandedComposite<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new Gs1DataBarExpandedCompositeSymbology(), "(01)93712345678904(3103)001234", "(91)1A2B3C4D5E", CreateOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Gs1DataBarExpandedComposite_WithText<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new Gs1DataBarExpandedCompositeSymbology(), "(01)93712345678904(3103)001234", "(91)1A2B3C4D5E", CreateTextOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Gs1DataBarExpandedStackedComposite<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new Gs1DataBarExpandedStackedCompositeSymbology(), "(01)00012345678905(10)ABCDEF", "(21)12345678", CreateOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Gs1DataBarExpandedStackedComposite_WithText<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new Gs1DataBarExpandedStackedCompositeSymbology(), "(01)00012345678905(10)ABCDEF", "(21)12345678", CreateTextOptions());

    /// <summary>
    /// Draws a composite symbol on an image the size of its measured area and compares the image with the reference image.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    /// <param name="provider">The image provider.</param>
    /// <param name="symbology">The composite symbology to draw.</param>
    /// <param name="first">The text of the linear component.</param>
    /// <param name="second">The text of the 2D component.</param>
    /// <param name="options">The sizing and painting options.</param>
    private static void RunTest<TPixel>(
        TestImageProvider<TPixel> provider,
        CompositeBarcodeSymbology symbology,
        string first,
        string second,
        BarcodeOptions options)
        where TPixel : unmanaged, IPixel<TPixel>
        => provider.RunValidatingProcessorTest(
            x =>
            {
                // The image is the composite, with no slack around it, as for one symbol.
                RectangleF bounds = BarcodeMeasurer.MeasureRenderableBounds(symbology, options, PointF.Empty, first, second);
                x.Resize(new Size((int)MathF.Ceiling(bounds.Width), (int)MathF.Ceiling(bounds.Height)));
                x.Paint(canvas => canvas.DrawBarcode(symbology, options, new PointF(-bounds.X, -bounds.Y), first, second));
            },
            appendPixelTypeToFileName: false,
            appendSourceFileOrDescription: false);

    /// <summary>
    /// Draws a symbol on an image the size of its measured area and compares the image with the reference image.
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
                // The image is the symbol, with no slack around it. The provider hands over a one pixel canvas, the measurer reports the area the symbol needs,
                // and the canvas grows to it. Slack would hide a symbol that grew, and a shortfall would clip one. Text can extend above the symbol or to the
                // left of it, so the symbol draws at the origin that puts the measured area at the corner of the image.
                RectangleF bounds = BarcodeMeasurer.MeasureRenderableBounds(symbology, options, PointF.Empty, text);
                x.Resize(new Size((int)MathF.Ceiling(bounds.Width), (int)MathF.Ceiling(bounds.Height)));
                x.Paint(canvas => canvas.DrawBarcode(symbology, options, new PointF(-bounds.X, -bounds.Y), text));
            },
            appendPixelTypeToFileName: false,
            appendSourceFileOrDescription: false);

    /// <summary>
    /// Creates the default options with a white background. At the default options each symbology draws at the X dimension and the bar height of its
    /// specification.
    /// </summary>
    /// <returns>The options.</returns>
    private static BarcodeOptions CreateOptions()
        => new()
        {
            Background = Brushes.Solid(Color.White),
        };

    /// <summary>
    /// Creates the options of <see cref="CreateOptions"/> with the OCR-B font at 10.75 points, for the linear, postal and stacked symbols.
    /// </summary>
    /// <returns>The options.</returns>
    private static BarcodeOptions CreateTextOptions()
    {
        BarcodeOptions options = CreateOptions();
        options.Font = BarcodeFonts.OcrB.CreateFont(10.75F);

        return options;
    }

    /// <summary>
    /// Creates the options of <see cref="CreateOptions"/> with the OCR-B font at 9 points, for the matrix symbols. Its cap ink stands 0.678 em, 2.15 mm, which
    /// meets the 2 mm character height of section 4.14 rule 2a.iii of the GS1 General Specifications.
    /// </summary>
    /// <returns>The options.</returns>
    private static BarcodeOptions CreateMatrixTextOptions()
    {
        BarcodeOptions options = CreateOptions();
        options.Font = BarcodeFonts.OcrB.CreateFont(9F);

        return options;
    }
}
