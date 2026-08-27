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

    /// <summary>
    /// The decoder must also read the symbol with the human readable interpretation present, proving the
    /// text does not intrude into the bars or quiet zones.
    /// </summary>
    [Fact]
    public void Ean13_RoundTrips_WithText()
    {
        BarcodeOptions options = CreateOptions();
        options.Font = BarcodeFonts.OcrA.CreateFont(23.15F);
        AssertDecodes(new Ean13Symbology(), "5901234123457", BarcodeFormat.EAN_13, "5901234123457", options);
    }

    private static void AssertDecodes(BarcodeSymbology symbology, string text, BarcodeFormat format, string expected)
        => AssertDecodes(symbology, text, format, expected, CreateOptions());

    private static void AssertDecodes(BarcodeSymbology symbology, string text, BarcodeFormat format, string expected, BarcodeOptions options)
    {
        using Image<Rgba32> image = new(400, 220, Color.White.ToPixel<Rgba32>());
        image.Mutate(x => x.Paint(canvas => canvas.DrawBarcode(symbology, text, options, new PointF(20, 20))));

        byte[] pixels = new byte[image.Width * image.Height * 4];
        image.CopyPixelDataTo(pixels);

        BarcodeReaderGeneric reader = new();
        reader.Options.PossibleFormats = [format];
        reader.Options.TryHarder = true;

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
