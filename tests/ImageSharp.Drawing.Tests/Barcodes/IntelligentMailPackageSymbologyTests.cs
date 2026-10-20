// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.Fonts;
using SixLabors.ImageSharp.Drawing.Barcodes;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace SixLabors.ImageSharp.Drawing.Tests.Barcodes;

/// <summary>
/// Encoder tests for <see cref="IntelligentMailPackageSymbology"/>. The check digits follow USPS2000508 Appendix E. The C01 PIC is the worked example of
/// Publication 199 section 4.6, whose check digit is 0.
/// </summary>
public class IntelligentMailPackageSymbologyTests
{
    /// <summary>
    /// Section 2.4.1 of USPS2000508 suppresses the (420) Application Identifier and the ZIP Code from the human readable interpretation, and Appendix D prints
    /// the PIC in groups of four digits with the remaining digits in a group at the end. Section 2.4.2 prints the service banner above the bars.
    /// </summary>
    /// <param name="text">The element strings.</param>
    /// <param name="expected">The printed PIC.</param>
    [Theory]
    [InlineData("(420)123456789(92)12391234567812345670", "9212 3912 3456 7812 3456 70")]
    [InlineData("(420)12345(92)123912345678123456789012", "9212 3912 3456 7812 3456 7890 12")]
    [InlineData("(94)1231291234567812345678901232", "9412 3129 1234 5678 1234 5678 9012 32")]
    public void PrintsThePicInGroupsOfFourAndTheBanner(string text, string expected)
    {
        BarcodeOptions options = new() { Font = BarcodeFonts.OcrB.CreateFont(12F) };
        string[] texts = SymbolEncoder.Read(new IntelligentMailPackageSymbology("USPS TRACKING #"), text, options, SymbolEncoder.Texts);

        Assert.Equal([expected, "USPS TRACKING #"], texts);
    }

    /// <summary>
    /// Section 2.2.4 of USPS2000508: "The overall minimum height of the barcode shall measure at least 0.75 inch". A bar height of 0.75 inch encodes, and a
    /// bar height under 0.75 inch throws.
    /// </summary>
    [Fact]
    public void RejectsABarHeightUnderThreeQuartersOfAnInch()
    {
        IntelligentMailPackageSymbology symbology = new("USPS TRACKING #");
        Font font = BarcodeFonts.OcrB.CreateFont(12F);
        BarcodeOptions minimum = new() { Dpi = SymbolEncoder.InchDpi, Font = font, BarHeight = 0.75 * 25.4 };
        BarcodeOptions under = new() { Dpi = SymbolEncoder.InchDpi, Font = font, BarHeight = 0.749 * 25.4 };

        Assert.Null(Record.Exception(() => SymbolEncoder.Encode(symbology, "(92)12391234567812345670", minimum)));
        Assert.Throws<ArgumentException>(() => SymbolEncoder.Encode(symbology, "(92)12391234567812345670", under));
    }

    /// <summary>
    /// Section 2.4.2 of USPS2000508: "A service banner shall be printed in all uppercase letters". The constructor rejects an empty banner, a blank banner
    /// and a banner with lowercase letters.
    /// </summary>
    /// <param name="banner">The banner.</param>
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("USPS Tracking #")]
    public void RejectsABannerThatIsEmptyOrNotUppercase(string banner)
        => Assert.ThrowsAny<ArgumentException>(() => new IntelligentMailPackageSymbology(banner));

    /// <summary>
    /// A null banner is a caller error.
    /// </summary>
    [Fact]
    public void RejectsNullBanner()
        => Assert.Throws<ArgumentNullException>(() => new IntelligentMailPackageSymbology(null!));

    /// <summary>
    /// Empty text matches no construct.
    /// </summary>
    [Fact]
    public void RejectsEmptyText()
        => Assert.ThrowsAny<ArgumentException>(() => Encode(string.Empty));

    /// <summary>
    /// Section 2.5.1.7 of USPS2000508: "Every barcode construct shall utilize a 1-digit, Mod 10 Check Digit as the final digit in the barcode data string."
    /// </summary>
    [Fact]
    public void RejectsWrongCheckDigit()
        => Assert.Throws<ArgumentException>(() => Encode("(420)123456789(92)12391234567812345671"));

    /// <summary>
    /// Section 2.5.2 of USPS2000508: "Barcodes shall conform to one of the approved barcode constructs". The constructs fix the channel, the length of the ZIP
    /// Code, the length of the mailer ID and the length of the PIC, and section 1.4 of Publication 199 requires a six digit mailer ID to begin with 0 to 8 and
    /// a nine digit mailer ID with 9. Every PIC here carries a correct check digit, so only the construct rule rejects it.
    /// </summary>
    /// <param name="text">The element strings.</param>
    [Theory]
    [InlineData("(420)123456789(92)123912345678123456789012")]
    [InlineData("(420)1234567(92)12391234567812345670")]
    [InlineData("(91)12391234567812345671")]
    [InlineData("(92)12311234567812345678")]
    [InlineData("(93)12392345612345678908")]
    [InlineData("(94)123129123456781234567896")]
    [InlineData("(95)123112345612341234567891")]
    [InlineData("(420)123456789")]
    [InlineData("(01)09521234543213")]
    [InlineData("(420)12345(92)12391234567812345670(10)ABC")]
    public void RejectsInputOutsideTheConstructs(string text)
        => Assert.ThrowsAny<ArgumentException>(() => Encode(text));

    /// <summary>
    /// Section 2.2.2 of USPS2000508 prefers an X dimension of 0.015 to 0.017 inch, section 2.2.4 sets a bar height of at least 0.75 inch, and section 2.4.3
    /// gives the identification bars a thickness of 0.031 to 0.063 inch. Figure 1 keeps them at least 0.031 inch from the text. Section 4.10.1 of Publication
    /// 199 recommends "A minimum left/right clear zone of 0.25 inch", and section 2.3.2 of USPS2000508 keeps "at least 0.125 inch" above and below the bars.
    /// </summary>
    [Fact]
    public void NominalSymbolMeetsThePhysicalRequirements()
    {
        BarcodeOptions options = new() { Dpi = SymbolEncoder.InchDpi, Font = BarcodeFonts.OcrB.CreateFont(12F) };
        (double xDimension, float barHeight, float thickness, float clearance, float sideZone, float endZone) = SymbolEncoder.Read(
            new IntelligentMailPackageSymbology("USPS TRACKING #"),
            "(92)12391234567812345670",
            options,
            static (ref symbol) => (
                symbol.XDimension,
                symbol.BarHeights[0],
                symbol.IdentificationBarThickness,
                symbol.IdentificationBarClearance,
                MathF.Min(symbol.LeadingQuietZone, symbol.TrailingQuietZone),
                MathF.Min(symbol.TopQuietZone, symbol.BottomQuietZone)));

        const double inch = 25.4;

        Assert.InRange(xDimension, 0.015 * inch, 0.017 * inch);
        Assert.True(barHeight * xDimension >= (0.75 * inch) - 1E-6);
        Assert.InRange(thickness * xDimension, 0.031 * inch, 0.063 * inch);
        Assert.True(clearance * xDimension >= (0.031 * inch) - 1E-6);
        Assert.True(sideZone * xDimension >= (0.25 * inch) - 1E-6);
        Assert.True(endZone * xDimension >= (0.125 * inch) - 1E-6);
    }

    /// <summary>
    /// Sections 2.4.1 and 2.4.2 of USPS2000508: "The printed height of the characters shall be between 0.090 to 0.125 inch", whatever font size the caller asks
    /// for. At 300 dots per inch that is 27 to 37.5 pixels, and a rasterized edge can move the count of dark rows by one pixel.
    /// </summary>
    /// <param name="points">The font size the caller asks for.</param>
    [Theory]
    [InlineData(4F)]
    [InlineData(40F)]
    public void PrintsTheCharactersAtTheStatedHeight(float points)
    {
        BarcodeOptions options = new() { Dpi = 300F, Font = BarcodeFonts.OcrB.CreateFont(points) };
        IntelligentMailPackageSymbology symbology = new("USPS TRACKING #");
        const string text = "(420)123456789(92)12391234567812345670";
        RectangleF bounds = BarcodeMeasurer.MeasureRenderableBounds(symbology, options, PointF.Empty, text);

        using Image<L8> image = new((int)MathF.Ceiling(bounds.Width), (int)MathF.Ceiling(bounds.Height), new L8(255));
        image.Mutate(x => x.Paint(canvas => canvas.DrawBarcode(symbology, options, new PointF(-bounds.X, -bounds.Y), text)));

        // A row is dark when any pixel in it is dark. The dark rows form bands from the top down: the upper identification bar, the banner, the bars, the PIC
        // and the lower identification bar.
        List<(int Start, int End)> runs = DarkBands(image);

        // The banner and the PIC are the second and the fourth band.
        Assert.Equal(5, runs.Count);

        const float minimum = 90F * 300F / 1000F;
        const float maximum = 125F * 300F / 1000F;

        Assert.InRange(runs[1].End - runs[1].Start, minimum - 1F, maximum + 1F);
        Assert.InRange(runs[3].End - runs[3].Start, minimum - 1F, maximum + 1F);
    }

    /// <summary>
    /// Section 2.4: "The IM Package Barcode structure shall include the elements defined in 2.4.1 through 2.4.3". Two of them are text, so the symbol needs a
    /// font.
    /// </summary>
    [Fact]
    public void RejectsOptionsWithoutAFont()
        => Assert.Throws<ArgumentException>(
            () => SymbolEncoder.Encode(new IntelligentMailPackageSymbology("USPS TRACKING #"), "(92)12391234567812345670", new BarcodeOptions()));

    /// <summary>
    /// Section 2.2.2 of USPS2000508: "The mailer shall print the barcode with an X-dimension measuring between 0.013 inch (13.0 mils) and 0.021 inch (21.0
    /// mils)."
    /// </summary>
    /// <param name="inches">The X dimension in inches.</param>
    /// <param name="expected">The X dimension the symbol draws at in inches, the nearest limit of the range.</param>
    [Theory]
    [InlineData(0.012, 0.013)]
    [InlineData(0.022, 0.021)]
    public void ClampsAnXDimensionOutsideTheRange(double inches, double expected)
        => SymbolEncoder.Encode(
            new IntelligentMailPackageSymbology("USPS TRACKING #"),
            "(92)12391234567812345670",
            new BarcodeOptions { Dpi = SymbolEncoder.InchDpi, XDimension = inches * 25.4, Font = BarcodeFonts.OcrB.CreateFont(12F) },
            (ref symbol) => Assert.Equal(expected * 25.4, symbol.XDimension, 1e-9));

    /// <summary>
    /// Section 2.4.2 of USPS2000508: "The human-readable text shall not exceed the total combined length of the barcode and the minimum clear zones to left and
    /// right of the barcode", and section 2.3.1 sets those clear zones at ten times the X dimension. Section 4.9.1 of Publication 199 gives the longest
    /// banner, "USPS SIGNATURE TRACKING #". The test draws at the smallest X dimension, 0.013 inch, and a rasterized edge can add one pixel.
    /// </summary>
    [Fact]
    public void PrintsTheBannerWithinTheBarcodeAndItsClearZones()
    {
        const float dpi = 300F;
        const double xDimension = 0.013 * 25.4;
        BarcodeOptions options = new() { Dpi = dpi, XDimension = xDimension, Font = BarcodeFonts.OcrB.CreateFont(40F) };
        IntelligentMailPackageSymbology symbology = new("USPS SIGNATURE TRACKING #");
        const string text = "(92)12391234567812345670";
        RectangleF bounds = BarcodeMeasurer.MeasureRenderableBounds(symbology, options, PointF.Empty, text);

        using Image<L8> image = new((int)MathF.Ceiling(bounds.Width), (int)MathF.Ceiling(bounds.Height), new L8(255));
        image.Mutate(x => x.Paint(canvas => canvas.DrawBarcode(symbology, options, new PointF(-bounds.X, -bounds.Y), text)));

        // The dark rows form bands from the top down: the upper identification bar, the banner, the bars, the PIC and the lower identification bar.
        List<(int Start, int End)> bands = DarkBands(image);

        Assert.Equal(5, bands.Count);

        (int bannerLeft, int bannerRight) = DarkColumns(image, bands[1]);
        (int barsLeft, int barsRight) = DarkColumns(image, bands[2]);
        float clearZone = 10F * MathF.Round((float)(xDimension / 25.4 * dpi));

        Assert.True(bannerLeft >= barsLeft - clearZone - 1F, $"The banner starts at {bannerLeft}, the left clear zone at {barsLeft - clearZone}.");
        Assert.True(bannerRight <= barsRight + clearZone + 1F, $"The banner ends at {bannerRight}, the right clear zone at {barsRight + clearZone}.");
    }

    /// <summary>
    /// Section 4.9.2 of Publication 199: "Human-readable numbers cannot extend beyond the length of the identification bars." The longest PIC at the largest
    /// printed height and the smallest X dimension is wider than the barcode and its clear zones, so the identification bars extend to span it. A rasterized
    /// edge can add one pixel.
    /// </summary>
    [Fact]
    public void PrintsThePicWithinTheIdentificationBars()
    {
        BarcodeOptions options = new() { Dpi = 300F, XDimension = 0.013 * 25.4, Font = BarcodeFonts.OcrB.CreateFont(40F) };
        IntelligentMailPackageSymbology symbology = new("USPS TRACKING #");
        const string text = "(94)1231291234567812345678901232";
        RectangleF bounds = BarcodeMeasurer.MeasureRenderableBounds(symbology, options, PointF.Empty, text);

        using Image<L8> image = new((int)MathF.Ceiling(bounds.Width), (int)MathF.Ceiling(bounds.Height), new L8(255));
        image.Mutate(x => x.Paint(canvas => canvas.DrawBarcode(symbology, options, new PointF(-bounds.X, -bounds.Y), text)));

        List<(int Start, int End)> bands = DarkBands(image);

        Assert.Equal(5, bands.Count);

        (int picLeft, int picRight) = DarkColumns(image, bands[3]);
        (int barLeft, int barRight) = DarkColumns(image, bands[4]);

        Assert.True(picLeft >= barLeft - 1, $"The PIC starts at {picLeft}, the identification bar at {barLeft}.");
        Assert.True(picRight <= barRight + 1, $"The PIC ends at {picRight}, the identification bar at {barRight}.");
    }

    /// <summary>
    /// A banner that does not fit the barcode and its clear zones at the smallest printed height, 0.090 inch, cannot meet both requirements of section 2.4.2.
    /// </summary>
    [Fact]
    public void RejectsABannerThatCannotFitAtTheSmallestHeight()
    {
        BarcodeOptions options = new() { Dpi = 300F, XDimension = 0.013 * 25.4, Font = BarcodeFonts.OcrB.CreateFont(12F) };
        IntelligentMailPackageSymbology symbology = new("USPS TRACKING # USPS TRACKING # USPS TRACKING # USPS TRACKING #");

        Assert.Throws<ArgumentException>(() => BarcodeMeasurer.Measure(symbology, options, PointF.Empty, "(92)12391234567812345670"));
    }

    /// <summary>
    /// Sections 2.4.1 and 2.4.2 of USPS2000508: the PIC and the banner "shall be printed in a boldface* sans serif font". The OCR-B font has no bold face,
    /// and the symbology draws its text bold, so the drawing with the regular font is the drawing with the font that requests bold.
    /// </summary>
    [Fact]
    public void DrawsTheTextBold()
    {
        BarcodeOptions regular = new() { Dpi = 300F, Font = BarcodeFonts.OcrB.CreateFont(12F) };
        BarcodeOptions bold = new() { Dpi = 300F, Font = BarcodeFonts.OcrB.CreateFont(12F, FontStyle.Bold) };

        Assert.Equal(Draw(bold), Draw(regular));
    }

    /// <summary>
    /// Draws a symbol and returns its pixels.
    /// </summary>
    /// <param name="options">The options.</param>
    /// <returns>The 8-bit grey pixels of the drawing.</returns>
    private static byte[] Draw(BarcodeOptions options)
    {
        IntelligentMailPackageSymbology symbology = new("USPS TRACKING #");
        const string text = "(92)12391234567812345670";
        RectangleF bounds = BarcodeMeasurer.MeasureRenderableBounds(symbology, options, PointF.Empty, text);

        using Image<L8> image = new((int)MathF.Ceiling(bounds.Right), (int)MathF.Ceiling(bounds.Bottom), new L8(255));
        image.Mutate(x => x.Paint(canvas => canvas.DrawBarcode(symbology, options, PointF.Empty, text)));

        byte[] pixels = new byte[image.Width * image.Height];
        image.CopyPixelDataTo(pixels);

        return pixels;
    }

    /// <summary>
    /// Encodes the text with the "USPS TRACKING #" banner and a 12 point OCR-B font, which the symbology requires.
    /// </summary>
    /// <param name="text">The element strings.</param>
    /// <returns>The run widths of the symbol.</returns>
    private static string Encode(string text)
        => SymbolEncoder.Read(
            new IntelligentMailPackageSymbology("USPS TRACKING #"),
            text,
            new BarcodeOptions { Font = BarcodeFonts.OcrB.CreateFont(12F) },
            SymbolEncoder.RunString);

    /// <summary>
    /// Returns the bands of rows that hold a dark pixel, from the top down.
    /// </summary>
    /// <param name="image">The rendered image.</param>
    /// <returns>The start and the end of each band.</returns>
    private static List<(int Start, int End)> DarkBands(Image<L8> image)
    {
        List<(int Start, int End)> bands = [];
        int start = -1;

        for (int y = 0; y < image.Height; y++)
        {
            bool dark = false;
            for (int x = 0; x < image.Width && !dark; x++)
            {
                dark = image[x, y].PackedValue < 128;
            }

            if (dark && start < 0)
            {
                start = y;
            }
            else if (!dark && start >= 0)
            {
                bands.Add((start, y));
                start = -1;
            }
        }

        if (start >= 0)
        {
            bands.Add((start, image.Height));
        }

        return bands;
    }

    /// <summary>
    /// Returns the first and the last column that hold a dark pixel in a band of rows.
    /// </summary>
    /// <param name="image">The rendered image.</param>
    /// <param name="band">The band of rows.</param>
    /// <returns>The first and the last dark column.</returns>
    private static (int Left, int Right) DarkColumns(Image<L8> image, (int Start, int End) band)
    {
        int left = int.MaxValue;
        int right = int.MinValue;

        for (int y = band.Start; y < band.End; y++)
        {
            for (int x = 0; x < image.Width; x++)
            {
                if (image[x, y].PackedValue < 128)
                {
                    left = Math.Min(left, x);
                    right = Math.Max(right, x);
                }
            }
        }

        return (left, right);
    }
}
