// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Barcodes;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using ZXing;

namespace SixLabors.ImageSharp.Drawing.Tests.Barcodes;

/// <summary>
/// Round-trip tests: every rendered barcode must decode back to its input through ZXing.Net, an independent
/// decoder. This proves the images are valid scannable symbols without a human point of reference, which the
/// module-sequence and golden image tests cannot do on their own. The standalone add-on symbologies have no
/// decode tests because ZXing reads add-ons only as extensions of a main EAN/UPC symbol.
/// </summary>
public class BarcodeDecodeTests
{
    [Theory]
    [InlineData("5901234123457")]
    [InlineData("4006381333931")]
    [InlineData("9780306406157")]
    public void Ean13_RoundTrips(string text)
        => AssertDecodes(new Ean13Symbology(), text, BarcodeFormat.EAN_13, text);

    [Fact]
    public void Ean8_RoundTrips()
        => AssertDecodes(new Ean8Symbology(), "96385074", BarcodeFormat.EAN_8, "96385074");

    [Fact]
    public void UpcA_RoundTrips()
        => AssertDecodes(new UpcASymbology(), "036000291452", BarcodeFormat.UPC_A, "036000291452");

    [Theory]
    [InlineData("01234565")]
    [InlineData("04252614")]
    [InlineData("16543214")]
    public void UpcE_RoundTrips(string text)
        => AssertDecodes(new UpcESymbology(), text, BarcodeFormat.UPC_E, text);

    [Fact]
    public void Isbn_RoundTrips()
        => AssertDecodes(new IsbnSymbology(), "978-0-306-40615-7", BarcodeFormat.EAN_13, "9780306406157");

    [Fact]
    public void Ismn_RoundTrips()
        => AssertDecodes(new IsmnSymbology(), "979-0-2600-0043-8", BarcodeFormat.EAN_13, "9790260000438");

    [Fact]
    public void Issn_RoundTrips()
        => AssertDecodes(new IssnSymbology(), "0317-8471", BarcodeFormat.EAN_13, "9770317847001");

    [Fact]
    public void Mands_RoundTrips()
        => AssertDecodes(new MandsSymbology(), "0642118", BarcodeFormat.EAN_8, "00642118");

    [Theory]
    [InlineData("CODE128")]
    [InlineData("ABC-123")]
    public void Code128_RoundTrips(string text)
        => AssertDecodes(new Code128Symbology(), text, BarcodeFormat.CODE_128, text);

    /// <summary>
    /// A GS1-128 symbol decodes to its element strings without the parentheses, which section 4.14
    /// rule 2c keeps to the human readable interpretation, and without the Function 1 characters, which
    /// are symbol overhead rather than data.
    /// </summary>
    [Theory]
    [InlineData("(01)09521234543213", "0109521234543213")]
    [InlineData("(10)ABC123", "10ABC123")]
    public void Gs1_128_RoundTrips(string text, string expected)
        => AssertDecodes(new Gs1128Symbology(), text, BarcodeFormat.CODE_128, expected);

    /// <summary>
    /// An EAN-14 decodes to its Application Identifier and the fourteen digit Global Trade Item Number.
    /// </summary>
    [Fact]
    public void Ean14_RoundTrips()
        => AssertDecodes(new Ean14Symbology(), "(01)09521234543213", BarcodeFormat.CODE_128, "0109521234543213");

    /// <summary>
    /// An SSCC-18 decodes to its Application Identifier and the eighteen digit Serial Shipping Container
    /// Code.
    /// </summary>
    [Fact]
    public void Sscc18_RoundTrips()
        => AssertDecodes(new Sscc18Symbology(), "(00)106141411234567897", BarcodeFormat.CODE_128, "00106141411234567897");

    /// <summary>
    /// A HIBC Code 128 decodes to the flag character, the data and the check character, which is what the
    /// symbol carries; the delimiters belong to the human readable interpretation alone.
    /// </summary>
    [Fact]
    public void HibcCode128_RoundTrips()
        => AssertDecodes(new HibcCode128Symbology(), "A123BJC5D6E71", BarcodeFormat.CODE_128, "+A123BJC5D6E71G");

    /// <summary>
    /// A Code 39 symbol decodes to its data. The start and stop character are symbol overhead, and the
    /// asterisks that stand for them belong to the human readable interpretation alone. The decoder is
    /// taken out of full ASCII mode, which A.3 calls a non-standard transmission mode, so a pair such as
    /// <c>+A</c> reads back as the two characters the symbol carries rather than as one lowercase letter.
    /// </summary>
    [Theory]
    [InlineData("CODE39")]
    [InlineData("ABC-123")]
    public void Code39_RoundTrips(string text)
        => AssertDecodes(
            new Code39Symbology(),
            text,
            BarcodeFormat.CODE_39,
            text,
            CreateOptions(),
            reader => reader.Options.UseCode39ExtendedMode = false);

    /// <summary>
    /// A PZN decodes to the identifier ISO/IEC 15418 gives it, the digits and the check digit, which is
    /// what the symbol carries; the term PZN and the spaces around the identifier are printed only.
    /// </summary>
    [Fact]
    public void Pzn8_RoundTrips()
        => AssertDecodes(
            new Pzn8Symbology(),
            "03752864",
            BarcodeFormat.CODE_39,
            "-03752864",
            CreateOptions(),
            reader => reader.Options.UseCode39ExtendedMode = false);

    /// <summary>
    /// A PZN7 carries one digit fewer than a PZN8, and the same identifier in front of it.
    /// </summary>
    [Fact]
    public void Pzn7_RoundTrips()
        => AssertDecodes(
            new Pzn7Symbology(),
            "1234562",
            BarcodeFormat.CODE_39,
            "-1234562",
            CreateOptions(),
            reader => reader.Options.UseCode39ExtendedMode = false);

    /// <summary>
    /// A HIBC Code 39 decodes to the flag character, the data and the check character, which is what the
    /// symbol carries; the delimiters belong to the human readable interpretation alone.
    /// </summary>
    [Fact]
    public void HibcCode39_RoundTrips()
        => AssertDecodes(
            new HibcCode39Symbology(),
            "A123BJC5D6E71",
            BarcodeFormat.CODE_39,
            "+A123BJC5D6E71G",
            CreateOptions(),
            reader => reader.Options.UseCode39ExtendedMode = false);

    /// <summary>
    /// A Code 39 Extended symbol is an ordinary Code 39 symbol, so A.3.1 needs the decoder put into full
    /// ASCII mode before the substituted pairs read back as the characters they stand for.
    /// </summary>
    [Theory]
    [InlineData("Code39")]
    [InlineData("abc")]
    [InlineData("a@b")]
    [InlineData("Hello, World!")]
    public void Code39Extended_RoundTrips(string text)
    {
        BarcodeOptions options = CreateOptions();
        AssertDecodes(
            new Code39ExtendedSymbology(),
            text,
            BarcodeFormat.CODE_39,
            text,
            options,
            reader => reader.Options.UseCode39ExtendedMode = true);
    }

    /// <summary>
    /// The decoder must also read the symbol with the human readable interpretation present, proving the
    /// text does not intrude into the bars or quiet zones.
    /// </summary>
    [Fact]
    public void Ean13_RoundTrips_WithText()
    {
        BarcodeOptions options = CreateOptions();
        options.Font = BarcodeFonts.OcrB.CreateFont(21.5F);
        AssertDecodes(new Ean13Symbology(), "5901234123457", BarcodeFormat.EAN_13, "5901234123457", options);
    }

    private static void AssertDecodes(BarcodeSymbology symbology, string text, BarcodeFormat format, string expected)
        => AssertDecodes(symbology, text, format, expected, CreateOptions(), null);

    private static void AssertDecodes(BarcodeSymbology symbology, string text, BarcodeFormat format, string expected, BarcodeOptions options)
        => AssertDecodes(symbology, text, format, expected, options, null);

    private static void AssertDecodes(
        BarcodeSymbology symbology,
        string text,
        BarcodeFormat format,
        string expected,
        BarcodeOptions options,
        Action<BarcodeReaderGeneric>? configureReader)
    {
        // The image holds the symbol and a margin on every side, measured rather than assumed, so a
        // symbology whose symbol is wider than the next is not silently clipped.
        PointF origin = new(20, 20);
        RectangleF bounds;
        using (Image<Rgba32> probe = new(1, 1))
        {
            RectangleF measured = default;
            probe.Mutate(x => x.Paint(canvas => measured = canvas.MeasureBarcode(symbology, text, options, origin)));
            bounds = measured;
        }

        using Image<Rgba32> image = new(
            (int)MathF.Ceiling(bounds.Right) + 20,
            (int)MathF.Ceiling(bounds.Bottom) + 20,
            Color.White.ToPixel<Rgba32>());

        image.Mutate(x => x.Paint(canvas => canvas.DrawBarcode(symbology, text, options, origin)));

        byte[] pixels = new byte[image.Width * image.Height * 4];
        image.CopyPixelDataTo(pixels);

        BarcodeReaderGeneric reader = new();
        reader.Options.PossibleFormats = [format];
        reader.Options.TryHarder = true;
        configureReader?.Invoke(reader);

        Result result = reader.Decode(new RGBLuminanceSource(pixels, image.Width, image.Height, RGBLuminanceSource.BitmapFormat.RGBA32));

        Assert.NotNull(result);
        Assert.Equal(format, result.BarcodeFormat);
        Assert.Equal(expected, result.Text);
    }

    private static BarcodeOptions CreateOptions()
        => new()
        {
            ModuleWidth = 3F,
            BarHeight = 120F,
        };
}
