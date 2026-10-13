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
    public void Pharmacode_WithText<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new PharmacodeSymbology(), "117480", CreateTextOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void TwoTrackPharmacode<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new TwoTrackPharmacodeSymbology(), "117480", CreateOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void TwoTrackPharmacode_WithText<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new TwoTrackPharmacodeSymbology(), "117480", CreateTextOptions());

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
        => RunTest(provider, new PostnetSymbology(), "01234", CreateNominalOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Postnet_WithText<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new PostnetSymbology(), "01234", CreateNominalTextOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Planet<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new PlanetSymbology(), "01234567890", CreateNominalOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Planet_WithText<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new PlanetSymbology(), "01234567890", CreateNominalTextOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void RoyalMail<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new RoyalMailSymbology(), "LE28HS9Z", CreateNominalOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void RoyalMail_WithText<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new RoyalMailSymbology(), "LE28HS9Z", CreateNominalTextOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Kix<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new KixSymbology(), "1231FZ13XHS", CreateNominalOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Kix_WithText<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new KixSymbology(), "1231FZ13XHS", CreateNominalTextOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Daft<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new DaftSymbology(), "FATDAFTDAD", CreateNominalOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void IntelligentMail<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new IntelligentMailSymbology(), "0123456709498765432101234567891", CreateNominalOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void IntelligentMail_WithText<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new IntelligentMailSymbology(), "0123456709498765432101234567891", CreateNominalTextOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void AustraliaPost<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new AustraliaPostSymbology(), "6254516251ABC123", CreateNominalOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void AustraliaPost_WithText<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new AustraliaPostSymbology(), "6254516251ABC123", CreateNominalTextOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void JapanPost<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new JapanPostSymbology(), "15400233-16-4-205", CreateNominalOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void JapanPost_WithText<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new JapanPostSymbology(), "15400233-16-4-205", CreateNominalTextOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void FourStateMailmark<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new FourStateMailmarkSymbology(), "21B2254800659JW5O9QA6Y", CreateNominalOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void FourStateMailmark_WithText<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new FourStateMailmarkSymbology(), "21B2254800659JW5O9QA6Y", CreateNominalTextOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Code16K<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new Code16KSymbology(), "Abcd-1234567890-wxyZ", CreateNominalOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Code16K_WithText<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new Code16KSymbology(), "Abcd-1234567890-wxyZ", CreateNominalTextOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Code49<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new Code49Symbology(), "MULTIPLE ROWS IN CODE 49", CreateNominalOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Code49_WithText<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new Code49Symbology(), "MULTIPLE ROWS IN CODE 49", CreateNominalTextOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void CodablockF<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new CodablockFSymbology(), "CODABLOCK F 34567890123456789010040digit", CreateNominalOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void CodablockF_WithText<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new CodablockFSymbology(), "CODABLOCK F 34567890123456789010040digit", CreateNominalTextOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void HibcCodablockF<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new HibcCodablockFSymbology(), "A123BJC5D6E71", CreateNominalOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void HibcCodablockF_WithText<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new HibcCodablockFSymbology(), "A123BJC5D6E71", CreateNominalTextOptions());

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Gs1DataBarOmnidirectional<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new Gs1DataBarOmnidirectionalSymbology(), "(01)09521234543213", CreateOptions(46F));

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Gs1DataBarOmnidirectional_WithText<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new Gs1DataBarOmnidirectionalSymbology(), "(01)09521234543213", CreateTextOptions(46F));

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Gs1DataBarTruncated<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new Gs1DataBarTruncatedSymbology(), "(01)09521234543213", CreateOptions(13F));

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Gs1DataBarTruncated_WithText<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new Gs1DataBarTruncatedSymbology(), "(01)09521234543213", CreateTextOptions(13F));

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Gs1DataBarStacked<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new Gs1DataBarStackedSymbology(), "(01)09521234543213", CreateOptions(7F));

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Gs1DataBarStacked_WithText<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new Gs1DataBarStackedSymbology(), "(01)09521234543213", CreateTextOptions(7F));

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Gs1DataBarStackedOmnidirectional<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new Gs1DataBarStackedOmnidirectionalSymbology(), "(01)09521234543213", CreateOptions(46F));

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Gs1DataBarStackedOmnidirectional_WithText<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new Gs1DataBarStackedOmnidirectionalSymbology(), "(01)09521234543213", CreateTextOptions(46F));

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Gs1DataBarLimited<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new Gs1DataBarLimitedSymbology(), "(01)15012345678907", CreateOptions(10F));

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Gs1DataBarLimited_WithText<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new Gs1DataBarLimitedSymbology(), "(01)15012345678907", CreateTextOptions(10F));

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Gs1DataBarExpanded<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new Gs1DataBarExpandedSymbology(), "(01)90614141000015(3202)000150", CreateOptions(34F));

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Gs1DataBarExpanded_WithText<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new Gs1DataBarExpandedSymbology(), "(01)90614141000015(3202)000150", CreateTextOptions(34F));

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Gs1DataBarExpandedStacked<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new Gs1DataBarExpandedStackedSymbology(), "(01)90614141000015(3202)000150", CreateOptions(34F));

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Gs1DataBarExpandedStacked_WithText<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new Gs1DataBarExpandedStackedSymbology(), "(01)90614141000015(3202)000150", CreateTextOptions(34F));

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Gs1NorthAmericanCoupon<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new Gs1NorthAmericanCouponSymbology(), "(8110)106141416543213500110000310123196000", CreateOptions(34F));

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Gs1NorthAmericanCoupon_WithText<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new Gs1NorthAmericanCouponSymbology(), "(8110)106141416543213500110000310123196000", CreateTextOptions(34F));

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void Pdf417<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new Pdf417Symbology(), "PDF417 Symbology Standard", CreateOptions(3F));

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void CompactPdf417<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new CompactPdf417Symbology(), "PDF417 Symbology Standard", CreateOptions(3F));

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void MicroPdf417<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new MicroPdf417Symbology(), "MicroPDF417", CreateOptions(2F));

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void HibcPdf417<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new HibcPdf417Symbology(), "A123BJC5D6E71", CreateOptions(3F));

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void HibcMicroPdf417<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new HibcMicroPdf417Symbology(), "A123BJC5D6E71", CreateOptions(2F));

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

    /// <summary>
    /// The linear goldens draw at 144 dots per inch with a one point X dimension and a 50 point bar
    /// height: 2 px modules and 100 px bars.
    /// </summary>
    private static BarcodeOptions CreateOptions()
        => CreateOptions(50F);

    /// <summary>
    /// Draws at 144 dots per inch with a one point X dimension, so a module is 2 px.
    /// </summary>
    /// <param name="barHeight">The bar height in modules.</param>
    /// <returns>The options.</returns>
    private static BarcodeOptions CreateOptions(float barHeight)
        => new()
        {
            Dpi = 144F,
            XDimension = BarcodeSymbology.PointXDimension,
            BarHeight = barHeight * BarcodeSymbology.PointXDimension,
            Background = Brushes.Solid(Color.White),
        };

    /// <summary>
    /// The postal and stacked goldens draw at 144 dots per inch at the X dimension and the bar height of
    /// their specifications.
    /// </summary>
    private static BarcodeOptions CreateNominalOptions()
        => new()
        {
            Dpi = 144F,
            Background = Brushes.Solid(Color.White),
        };

    private static BarcodeOptions CreateNominalTextOptions()
    {
        BarcodeOptions options = CreateNominalOptions();
        options.Font = BarcodeFonts.OcrB.CreateFont(10.75F);
        return options;
    }

    private static BarcodeOptions CreateTextOptions()
        => CreateTextOptions(50F);

    /// <summary>
    /// Draws at 144 dots per inch with a one point X dimension and the OCR-B font.
    /// </summary>
    /// <param name="barHeight">The bar height in modules.</param>
    /// <returns>The options.</returns>
    private static BarcodeOptions CreateTextOptions(float barHeight)
    {
        BarcodeOptions options = CreateOptions(barHeight);

        // GS1 prints the interpretation 2.75mm high at the 0.33mm X-dimension, so at 144 dots per inch it
        // is 16.7px of ink. The cap ink of this font stands 0.678 em, giving 24.6px, 12.3 points, but the
        // digit cell cap holds it to what fits a 7 module cell.
        options.Font = BarcodeFonts.OcrB.CreateFont(10.75F);
        return options;
    }
}
