// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Barcodes;

namespace SixLabors.ImageSharp.Drawing.Tests.Barcodes;

/// <summary>
/// The input contract every symbology shares. The per-symbology test classes cover the format rules of their own standard; these cover what all of them owe a
/// caller, so no symbology reports bad input by crashing or by drawing a symbol that carries nothing.
/// </summary>
public class BarcodeInputValidationTests
{
    /// <summary>
    /// Empty text carries no data. Every symbology whose standard fixes a length or requires data rejects it. The list does not include Code 128 and Code 16K:
    /// their standards set no minimum, and an empty symbol is a start character, pad and check characters and a stop character.
    /// </summary>
    /// <param name="symbologyType">The symbology.</param>
    [Theory]
    [InlineData(typeof(Ean13Symbology))]
    [InlineData(typeof(Ean8Symbology))]
    [InlineData(typeof(Ean5Symbology))]
    [InlineData(typeof(Ean2Symbology))]
    [InlineData(typeof(UpcASymbology))]
    [InlineData(typeof(UpcESymbology))]
    [InlineData(typeof(IsbnSymbology))]
    [InlineData(typeof(IsmnSymbology))]
    [InlineData(typeof(IssnSymbology))]
    [InlineData(typeof(MandsSymbology))]
    [InlineData(typeof(Gs1128Symbology))]
    [InlineData(typeof(Ean14Symbology))]
    [InlineData(typeof(Sscc18Symbology))]
    [InlineData(typeof(HibcCode128Symbology))]
    [InlineData(typeof(Code39Symbology))]
    [InlineData(typeof(Code39ExtendedSymbology))]
    [InlineData(typeof(HibcCode39Symbology))]
    [InlineData(typeof(Pzn8Symbology))]
    [InlineData(typeof(Pzn7Symbology))]
    [InlineData(typeof(Code32Symbology))]
    [InlineData(typeof(Code93Symbology))]
    [InlineData(typeof(Code93ExtendedSymbology))]
    [InlineData(typeof(Interleaved2Of5Symbology))]
    [InlineData(typeof(LeitcodeSymbology))]
    [InlineData(typeof(IdentcodeSymbology))]
    [InlineData(typeof(Industrial2Of5Symbology))]
    [InlineData(typeof(Iata2Of5Symbology))]
    [InlineData(typeof(Matrix2Of5Symbology))]
    [InlineData(typeof(Coop2Of5Symbology))]
    [InlineData(typeof(Datalogic2Of5Symbology))]
    [InlineData(typeof(Itf14Symbology))]
    [InlineData(typeof(CodabarSymbology))]
    [InlineData(typeof(Code11Symbology))]
    [InlineData(typeof(MsiSymbology))]
    [InlineData(typeof(PlesseySymbology))]
    [InlineData(typeof(TelepenSymbology))]
    [InlineData(typeof(TelepenNumericSymbology))]
    [InlineData(typeof(PosiCodeSymbology))]
    [InlineData(typeof(Bc412Symbology))]
    [InlineData(typeof(ChannelCodeSymbology))]
    [InlineData(typeof(PharmacodeSymbology))]
    [InlineData(typeof(TwoTrackPharmacodeSymbology))]
    [InlineData(typeof(FlattermarkenSymbology))]
    [InlineData(typeof(RawLinearSymbology))]
    [InlineData(typeof(FacingIdentificationMarkSymbology))]
    [InlineData(typeof(PostnetSymbology))]
    [InlineData(typeof(PlanetSymbology))]
    [InlineData(typeof(RoyalMailSymbology))]
    [InlineData(typeof(KixSymbology))]
    [InlineData(typeof(DaftSymbology))]
    [InlineData(typeof(IntelligentMailSymbology))]
    [InlineData(typeof(AustraliaPostSymbology))]
    [InlineData(typeof(JapanPostSymbology))]
    [InlineData(typeof(FourStateMailmarkSymbology))]
    [InlineData(typeof(Code49Symbology))]
    [InlineData(typeof(HibcCodablockFSymbology))]
    [InlineData(typeof(AztecCodeSymbology))]
    [InlineData(typeof(AztecCodeCompactSymbology))]
    [InlineData(typeof(AztecRuneSymbology))]
    [InlineData(typeof(HibcAztecCodeSymbology))]
    [InlineData(typeof(MaxiCodeSymbology))]
    [InlineData(typeof(CodeOneSymbology))]
    [InlineData(typeof(HanXinSymbology))]
    [InlineData(typeof(DotCodeSymbology))]
    [InlineData(typeof(UltracodeSymbology))]
    public void RejectsEmptyText(Type symbologyType)
    {
        BarcodeSymbology symbology = (BarcodeSymbology)Activator.CreateInstance(symbologyType)!;

        Assert.ThrowsAny<ArgumentException>(() => SymbolEncoder.Encode(symbology, string.Empty, new BarcodeOptions()));
    }

    /// <summary>
    /// A Code 128 symbol carries at most 500 characters. The symbology rejects longer input. It does not encode a symbol that no scanner reads.
    /// </summary>
    [Fact]
    public void Code128_RejectsDataBeyondTheMaximumLength()
        => Assert.ThrowsAny<ArgumentException>(() => SymbolEncoder.Encode(new Code128Symbology(), new string('A', 501), new BarcodeOptions()));

    /// <summary>
    /// A resolution, an X dimension or a bar height of zero or less has no physical size. The options reject each of these values.
    /// </summary>
    /// <param name="value">The rejected value.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Options_RejectSizesNotGreaterThanZero(float value)
    {
        BarcodeOptions options = new();

        Assert.Throws<ArgumentOutOfRangeException>(() => options.Dpi = value);
        Assert.Throws<ArgumentOutOfRangeException>(() => options.XDimension = value);
        Assert.Throws<ArgumentOutOfRangeException>(() => options.BarHeight = value);
    }

    /// <summary>
    /// At the default options a symbol draws at an X dimension inside the range its specification permits, at 96, 144, 300 and 600 dots per inch, where a
    /// whole number of pixels is inside that range. Where none is, as for BC412 at 96, 144 and 300 dots per inch, the module is the smallest whole number of
    /// pixels over the minimum. The ranges are the smallest minimum and the largest maximum of the symbol specification tables of section 5.12.3 of the GS1
    /// General Specifications that list ITF-14, GS1-128, EAN/UPC, GS1 DataBar, GS1 DataMatrix and GS1 QR Code, note (*) and the logistic unit rows of Tables
    /// 5-51 and 5-58 for the SSCC, Table 5-58 for GS1 DotCode, Tables 5-46 and 5-49 for GS1 Digital Link, section 4 of the IFA PZN document, Table 1 of
    /// SEMI T1-95 for BC412, DMM 708.4.2.5 c for POSTNET and DMM 708.9.3 b for the FIM.
    /// </summary>
    /// <param name="symbologyType">The symbology.</param>
    /// <param name="text">The text to encode.</param>
    /// <param name="minimum">The smallest X dimension the specification permits, in millimetres.</param>
    /// <param name="maximum">The largest X dimension the specification permits, in millimetres.</param>
    [Theory]
    [InlineData(typeof(Itf14Symbology), "15400141288763", 0.170, 1.016)]
    [InlineData(typeof(Gs1128Symbology), "(01)09521234543213", 0.170, 1.016)]
    [InlineData(typeof(Ean14Symbology), "(01)09521234543213", 0.170, 1.016)]
    [InlineData(typeof(Sscc18Symbology), "(00)106141411234567897", 0.250, 0.940)]
    [InlineData(typeof(Ean13Symbology), "5901234123457", 0.170, 0.660)]
    [InlineData(typeof(Gs1DataBarOmnidirectionalSymbology), "(01)09521234543213", 0.170, 0.660)]
    [InlineData(typeof(Gs1DotCodeSymbology), "(01)09521234543213(17)240101(10)ABC123", 0.380, 0.990)]
    [InlineData(typeof(Gs1DataMatrixSymbology), "(01)09521234543213", 0.100, 3.50)]
    [InlineData(typeof(Gs1QrCodeSymbology), "(01)09521234543213", 0.254, 3.50)]
    [InlineData(typeof(Gs1DigitalLinkDataMatrixSymbology), "https://id.gs1.org/01/09521234543213", 0.396, 0.990)]
    [InlineData(typeof(Gs1DigitalLinkQrCodeSymbology), "https://id.gs1.org/01/09521234543213", 0.396, 0.990)]
    [InlineData(typeof(Pzn8Symbology), "03752864", 0.187, 0.450)]
    [InlineData(typeof(Bc412Symbology), "BC412AB", 0.120 - 0.025, 0.120 + 0.025)]
    [InlineData(typeof(PostnetSymbology), "01234", 0.015 * 25.4, 0.025 * 25.4)]
    [InlineData(typeof(FacingIdentificationMarkSymbology), "A", ((1.0 / 32) - 0.008) * 25.4, ((1.0 / 32) + 0.008) * 25.4)]
    public void DrawsTheDefaultXDimensionInsideTheRangeOfTheSpecification(Type symbologyType, string text, double minimum, double maximum)
    {
        BarcodeSymbology symbology = (BarcodeSymbology)Activator.CreateInstance(symbologyType)!;
        foreach (float dpi in (ReadOnlySpan<float>)[96F, 144F, 300F, 600F])
        {
            // The whole numbers of pixels at or over the minimum and at or under the maximum. The tolerance keeps a limit that falls on a whole pixel on it.
            double pixelsPerMillimetre = dpi / 25.4;
            double lowest = Math.Max(1, Math.Ceiling((minimum * pixelsPerMillimetre) - 1e-9));
            double highest = Math.Floor((maximum * pixelsPerMillimetre) + 1e-9);
            double xDimension = SymbolEncoder.Read(symbology, text, new BarcodeOptions { Dpi = dpi }, static (ref symbol) => symbol.XDimension);

            if (lowest <= highest)
            {
                Assert.InRange(xDimension, minimum - 1e-9, maximum + 1e-9);
            }
            else
            {
                Assert.Equal(lowest, xDimension * pixelsPerMillimetre, 1e-9);
            }
        }
    }
}
