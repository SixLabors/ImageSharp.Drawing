// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Barcodes;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace SixLabors.ImageSharp.Drawing.Tests.Barcodes;

/// <summary>
/// Encoder tests for <see cref="IntelligentMailPackageSymbology"/>. Each expected run string is the alternating bar and space module widths, starting with a
/// bar, taken from an independent reference implementation through its raw encoding API for the same element strings as GS1-128. The check digits follow
/// USPS2000508 Appendix E. The C01 PIC is the worked example of Publication 199 section 4.6, whose check digit is 0.
/// </summary>
public class IntelligentMailPackageSymbologyTests
{
    /// <summary>
    /// One input for each channel Application Identifier, with and without a routing code. Every symbol starts with the Start C character and stays in code set
    /// C, as section 2.2.1 of USPS2000508 requires.
    /// </summary>
    /// <param name="text">The element strings.</param>
    /// <param name="expected">The reference runs.</param>
    [Theory]
    [InlineData(
        "(420)123456789(92)12391234567812345670",
        "2112324111311121332221223121311131231411222121414111311111431122322113131122321311233311212411121122321311233311211124122221222331112")]
    [InlineData(
        "(420)12345(92)123912345678123456789012",
        "2112324111311121332221223121311131234111311111431122322113131122321311233311212411121122321311233311212411122141211122322412112331112")]
    [InlineData(
        "(92)12391234567812345670",
        "2112324111311111431122322113131122321311233311212411121122321311233311211124123321112331112")]
    [InlineData(
        "(420)123456789(93)12312345612345678906",
        "2112324111311121332221223121311131231411222121414111311113411122322123213121311131232214113121311131231411222121411222131122322331112")]
    [InlineData(
        "(94)1231291234567812345678901232",
        "2112324111311311411122322123213222111122321311233311212411121122321311233311212411122141211122322321212212132331112")]
    [InlineData(
        "(420)123456789(95)12311234561234123453",
        "2112324111311121332221223121311131231411222121414111311141131122322123211122321311233311211122321311231122321311232131311112242331112")]
    public void MatchesReferenceRuns(string text, string expected)
        => Assert.Equal(
            expected,
            SymbolEncoder.Read(new IntelligentMailPackageSymbology("USPS TRACKING #"), text, new BarcodeOptions(), SymbolEncoder.RunString));

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
    /// Section 2.4.2 of USPS2000508: "A service banner shall be printed in all uppercase letters".
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
    /// gives the identification bars a thickness of 0.031 to 0.063 inch. Figure 1 keeps them at least 0.031 inch from the text.
    /// </summary>
    [Fact]
    public void NominalSymbolMeetsThePhysicalRequirements()
    {
        BarcodeOptions options = new() { Font = BarcodeFonts.OcrB.CreateFont(12F) };
        (double xDimension, float barHeight, float thickness, float clearance) = SymbolEncoder.Read(
            new IntelligentMailPackageSymbology("USPS TRACKING #"),
            "(92)12391234567812345670",
            options,
            static (ref symbol) => (symbol.XDimension, symbol.BarHeights[0], symbol.IdentificationBarThickness, symbol.IdentificationBarClearance));

        const double inch = 25.4;
        Assert.InRange(xDimension, 0.015 * inch, 0.017 * inch);
        Assert.True(barHeight * xDimension >= (0.75 * inch) - 1E-6);
        Assert.InRange(thickness * xDimension, 0.031 * inch, 0.063 * inch);
        Assert.True(clearance * xDimension >= (0.031 * inch) - 1E-6);
    }

    /// <summary>
    /// Sections 2.4.1 and 2.4.2 of USPS2000508: "The printed height of the characters shall be between 0.090 to 0.125 inch", whatever font size the caller asks
    /// for. At 300 dots per inch that is 27 to 37.5 pixels, and a rasterized edge can add one pixel.
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
        List<(int Start, int End)> runs = [];
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
                runs.Add((start, y));
                start = -1;
            }
        }

        if (start >= 0)
        {
            runs.Add((start, image.Height));
        }

        // The banner and the PIC are the second and the fourth band.
        Assert.Equal(5, runs.Count);
        float minimum = 0.090F * 300F;
        float maximum = 0.125F * 300F;
        Assert.InRange(runs[1].End - runs[1].Start, minimum - 1F, maximum + 1F);
        Assert.InRange(runs[3].End - runs[3].Start, minimum - 1F, maximum + 1F);
    }

    private static string Encode(string text)
        => SymbolEncoder.Read(new IntelligentMailPackageSymbology("USPS TRACKING #"), text, new BarcodeOptions(), SymbolEncoder.RunString);
}
