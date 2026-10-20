// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Globalization;
using SixLabors.ImageSharp.Drawing.Barcodes;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace SixLabors.ImageSharp.Drawing.Tests.Barcodes;

/// <summary>
/// Encoder tests for the EAN/UPC symbology family. Structural assertions (symbol width, bar counts, guard bar extension, quiet zones, text layout) follow
/// section 5.2 of the GS1 General Specifications directly.
/// </summary>
public class EanUpcSymbologyTests
{
    /// <summary>
    /// Twelve digits get their check digit, so the symbol is the one of the thirteen digits with the check digit.
    /// </summary>
    [Fact]
    public void Ean13_ComputesMissingCheckDigit()
        => Assert.Equal(Runs(new Ean13Symbology(), "5901234123457"), Runs(new Ean13Symbology(), "590123412345"));

    /// <summary>
    /// An EAN-13 symbol is 95 modules and 30 bars, with a quiet zone of 11 modules before it and 7 after it. The left, centre and right guard bars extend
    /// 5 modules below the digit bars.
    /// </summary>
    [Fact]
    public void Ean13_Structure()
    {
        SymbolEncoder.Encode(new Ean13Symbology(), "5901234123457", new BarcodeOptions(), static (ref symbol) =>
        {
            // GS1 General Specifications: 95 modules, 30 bars, an 11 module leading and 7 module trailing quiet zone.
            Assert.Equal(95, symbol.WidthInModules);
            Assert.Equal(30, symbol.BarHeights.Length);
            Assert.Equal(11, symbol.LeadingQuietZone);
            Assert.Equal(7, symbol.TrailingQuietZone);
        });

        AssertGuardExtension(new Ean13Symbology(), "5901234123457", [0, 1, 14, 15, 28, 29]);
    }

    /// <summary>
    /// With a font, the thirteen digits print below the bars. The leading digit prints in the leading quiet zone. Without a font there is no text.
    /// </summary>
    [Fact]
    public void Ean13_TextPlacements()
    {
        BarcodeOptions options = CreateTextOptions();

        SymbolEncoder.Encode(new Ean13Symbology(), "5901234123457", options, static (ref symbol) =>
        {
            // GS1 General Specifications: the leading digit prints in the leading quiet zone and every other digit prints below its own symbol character, so
            // the placement is per digit. Digits hang one module below the digit bars; the renderer grows the drawn area to hold them.
            float textEdge = symbol.BarHeights.ToArray().Min() + BarcodeTextPlacement.Clearance;

            Assert.Equal(13, symbol.Text.Length);
            Assert.Equal("5901234123457", string.Concat(SymbolEncoder.Texts(ref symbol)));
            Assert.True(symbol.Text[0].Left < 0);
            Assert.All(symbol.Text.ToArray(), placement => Assert.Equal(BarcodeTextSide.BelowBars, placement.Side));
            Assert.All(symbol.Text.ToArray(), placement => Assert.Equal(textEdge, placement.TextEdge));
        });

        SymbolEncoder.Encode(new Ean13Symbology(), "5901234123457", new BarcodeOptions(), static (ref symbol) => Assert.True(symbol.Text.IsEmpty));
    }

    /// <summary>
    /// With a font, the eight digits of an EAN-8 symbol print below the bars.
    /// </summary>
    [Fact]
    public void Ean8_TextPlacements()
        => SymbolEncoder.Encode(new Ean8Symbology(), "96385074", CreateTextOptions(), static (ref symbol) =>
        {
            // GS1 General Specifications: every digit prints below its own symbol character, one module below the digit bars.
            float textEdge = symbol.BarHeights.ToArray().Min() + BarcodeTextPlacement.Clearance;

            Assert.Equal("96385074", string.Concat(SymbolEncoder.Texts(ref symbol)));
            Assert.All(symbol.Text.ToArray(), placement => Assert.Equal(BarcodeTextSide.BelowBars, placement.Side));
            Assert.All(symbol.Text.ToArray(), placement => Assert.Equal(textEdge, placement.TextEdge));
        });

    /// <summary>
    /// The encoder rejects EAN-13 text that is not 12 or 13 digits, and a wrong check digit.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    [Theory]
    [InlineData("12345678901")]
    [InlineData("12345678901234")]
    [InlineData("59012341234A")]
    [InlineData("5901234123450")]
    public void Ean13_RejectsInvalidInput(string text)
        => Assert.ThrowsAny<ArgumentException>(() => Encode(new Ean13Symbology(), text));

    /// <summary>
    /// An EAN-8 symbol is 67 modules and 22 bars, with quiet zones of 7 modules. Seven digits get their check digit. The left, centre and right guard bars
    /// extend 5 modules below the digit bars.
    /// </summary>
    [Fact]
    public void Ean8_Structure()
    {
        string expected = Runs(new Ean8Symbology(), "96385074");

        SymbolEncoder.Encode(new Ean8Symbology(), "9638507", new BarcodeOptions(), (ref symbol) =>
        {
            // GS1 General Specifications: 67 modules, 22 bars, 7 module quiet zones on both sides. The 7 digit input verifies check digit computation: 96385074
            // carries check digit 4.
            Assert.Equal(67, symbol.WidthInModules);
            Assert.Equal(22, symbol.BarHeights.Length);
            Assert.Equal(7, symbol.LeadingQuietZone);
            Assert.Equal(7, symbol.TrailingQuietZone);
            Assert.Equal(expected, RunsToString(ref symbol));
        });

        AssertGuardExtension(new Ean8Symbology(), "96385074", [0, 1, 10, 11, 20, 21]);
    }

    /// <summary>
    /// The encoder rejects EAN-8 text that is not 7 or 8 digits, and a wrong check digit.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    [Theory]
    [InlineData("963850")]
    [InlineData("963850741")]
    [InlineData("9638507A")]
    [InlineData("96385071")]
    public void Ean8_RejectsInvalidInput(string text)
        => Assert.ThrowsAny<ArgumentException>(() => Encode(new Ean8Symbology(), text));

    /// <summary>
    /// A UPC-A symbol is 95 modules with quiet zones of 9 modules. Eleven digits get their check digit. The guard bars and the bars of the first and last
    /// symbol characters extend 5 modules below the digit bars.
    /// </summary>
    [Fact]
    public void UpcA_Structure()
    {
        string expected = Runs(new UpcASymbology(), "036000291452");

        SymbolEncoder.Encode(new UpcASymbology(), "03600029145", new BarcodeOptions(), (ref symbol) =>
        {
            // GS1 General Specifications: the UPC-A symbol shares the 95 module EAN-13 structure with 9 module quiet zones. Section 5.2.3.2 extends the bars of
            // the first and last symbol characters. The 11 digit input verifies check digit computation: 036000291452 carries check digit 2.
            Assert.Equal(95, symbol.WidthInModules);
            Assert.Equal(9, symbol.LeadingQuietZone);
            Assert.Equal(9, symbol.TrailingQuietZone);
            Assert.Equal(expected, RunsToString(ref symbol));
        });

        AssertGuardExtension(new UpcASymbology(), "036000291452", [0, 1, 2, 3, 14, 15, 26, 27, 28, 29]);
    }

    /// <summary>
    /// With a font, the twelve digits of a UPC-A symbol print. The number system digit prints in the leading quiet zone and the check digit prints in the
    /// trailing quiet zone.
    /// </summary>
    [Fact]
    public void UpcA_TextPlacements()
    {
        BarcodeOptions options = CreateTextOptions();

        SymbolEncoder.Encode(new UpcASymbology(), "036000291452", options, static (ref symbol) =>
        {
            // The number system digit prints in the leading quiet zone, the check digit in the trailing quiet zone, and every other digit below its own symbol
            // character.
            Assert.Equal(12, symbol.Text.Length);
            Assert.Equal("036000291452", string.Concat(SymbolEncoder.Texts(ref symbol)));
            Assert.True(symbol.Text[0].Right < 0);
            Assert.True(symbol.Text[11].Left > symbol.WidthInModules);
        });
    }

    /// <summary>
    /// Section 5.2.5 of the GS1 General Specifications: "the size of the first and last digits should be reduced to a maximum width equivalent to four
    /// modules". "The right-hand side of the first digit is positioned five module widths to the left of the leftmost guard bar. The left-hand side of the last
    /// digit is positioned five module widths to the right of the rightmost guard bar for UPC-A barcodes and three module widths for UPC-E barcodes. The bottom
    /// edge of the first and last digit SHALL be aligned with the bottom edge of the remaining full size digits." A rasterized edge can add one pixel. The
    /// module draws at the nearest whole number of pixels, 8 pixels for 0.33 mm at 600 dots per inch.
    /// The digits 1, 2 and 4 of OCR-B stand flat on the baseline, so their ink bottom is the bottom edge of the digit. A round digit overshoots the baseline
    /// in proportion to its size. The first digit of UPC-E is the number system digit, which is always a round 0, so only its last digit bottom is compared.
    /// </summary>
    /// <param name="upcE">Whether the symbology is UPC-E, otherwise UPC-A.</param>
    /// <param name="text">The text to encode.</param>
    /// <param name="lastDigitGap">The modules between the rightmost guard bar and the last digit.</param>
    [Theory]
    [InlineData(false, "121212121212", 5F)]
    [InlineData(true, "02121244", 3F)]
    public void PrintsTheQuietZoneDigitsAtTheGuardBars(bool upcE, string text, float lastDigitGap)
    {
        const float dpi = 600F;
        BarcodeSymbology symbology = upcE ? new UpcESymbology() : new UpcASymbology();
        BarcodeOptions options = new() { Dpi = dpi, Font = BarcodeFonts.OcrB.CreateFont(21.5F) };
        float module = MathF.Round((float)(EanUpcEncoder.NominalXDimension / 25.4 * dpi));
        RectangleF bounds = BarcodeMeasurer.MeasureRenderableBounds(symbology, options, PointF.Empty, text);

        using Image<L8> image = new((int)MathF.Ceiling(bounds.Width), (int)MathF.Ceiling(bounds.Height), new L8(255));
        image.Mutate(x => x.Paint(canvas => canvas.DrawBarcode(symbology, options, new PointF(-bounds.X, -bounds.Y), text)));

        // The top row holds only bars, and its first and last dark columns are the outer edges of the guard bars.
        (int barsLeft, int barsRight) = DarkColumns(image, 0, image.Width, 0, 1);
        (int firstLeft, int firstRight) = DarkColumns(image, 0, barsLeft, 0, image.Height);
        (int lastLeft, int lastRight) = DarkColumns(image, barsRight + 1, image.Width, 0, image.Height);

        int firstWidth = firstRight - firstLeft + 1;
        int lastWidth = lastRight - lastLeft + 1;

        Assert.True(firstWidth <= (4F * module) + 1F, $"The first digit is {firstWidth} pixels wide, 4 modules are {4F * module}.");
        Assert.InRange(barsLeft - firstRight - 1, (5F * module) - 1F, (5F * module) + 1F);
        Assert.True(lastWidth <= (4F * module) + 1F, $"The last digit is {lastWidth} pixels wide, 4 modules are {4F * module}.");
        Assert.InRange(lastLeft - barsRight - 1, (lastDigitGap * module) - 1F, (lastDigitGap * module) + 1F);

        // The full size digits print below the bars, and the guard bars reach less far down than they do.
        int fullBottom = DarkRows(image, barsLeft, barsRight + 1).Bottom;
        if (!upcE)
        {
            Assert.InRange(DarkRows(image, 0, barsLeft).Bottom, fullBottom - 1, fullBottom + 1);
        }

        Assert.InRange(DarkRows(image, barsRight + 1, image.Width).Bottom, fullBottom - 1, fullBottom + 1);
    }

    /// <summary>
    /// A UPC-E symbol is 51 modules and 17 bars, with a quiet zone of 9 modules before it and 7 after it. Seven digits get their check digit. The two left
    /// guard bars and the three bars of the special right guard pattern extend 5 modules below the digit bars.
    /// </summary>
    [Fact]
    public void UpcE_Structure()
    {
        string expected = Runs(new UpcESymbology(), "01234565");

        SymbolEncoder.Encode(new UpcESymbology(), "0123456", new BarcodeOptions(), (ref symbol) =>
        {
            // GS1 General Specifications: 51 modules, 17 bars, a 9 module leading and 7 module trailing quiet zone. All five guard bars are extended: two in
            // the left guard pattern and three in the special right guard pattern.
            Assert.Equal(51, symbol.WidthInModules);
            Assert.Equal(17, symbol.BarHeights.Length);
            Assert.Equal(9, symbol.LeadingQuietZone);
            Assert.Equal(7, symbol.TrailingQuietZone);
            Assert.Equal(expected, RunsToString(ref symbol));
        });

        AssertGuardExtension(new UpcESymbology(), "01234565", [0, 1, 14, 15, 16]);
    }

    /// <summary>
    /// Section 5.2.2.4.1 of the GS1 General Specifications: "D1 SHALL always be zero", so a leading 1 or 2 is rejected. Each zero suppression rule needs one
    /// digit to be nonzero: D6 when D11 is 5 to 9, D5 for the rule that sets X6 to 4, and D4 from 3 to 9 for the rule that sets X6 to 3. Digits without it
    /// are not the zero suppression of a GTIN-12. The other inputs have the wrong length or a wrong character or check digit.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    [Theory]
    [InlineData("1654321")]
    [InlineData("2123456")]
    [InlineData("0123405")]
    [InlineData("0123054")]
    [InlineData("0120453")]
    [InlineData("0122453")]
    [InlineData("012345")]
    [InlineData("012345678")]
    [InlineData("012345A")]
    [InlineData("01234560")]
    public void UpcE_RejectsInvalidInput(string text)
        => Assert.ThrowsAny<ArgumentException>(() => Encode(new UpcESymbology(), text));

    /// <summary>
    /// An EAN-5 add-on symbol is 47 modules and 16 bars, and all its bars have the same height and top.
    /// </summary>
    [Fact]
    public void Ean5_Structure()
        => SymbolEncoder.Encode(new Ean5Symbology(), "90200", new BarcodeOptions(), static (ref symbol) =>
        {
            // GS1 General Specifications Table 5-4: the 4 module add-on guard, five 7 module characters and four 2 module delineators make 47 modules. All
            // add-on bars are uniform in height.
            float first = symbol.BarHeights[0];

            Assert.Equal(47, symbol.WidthInModules);
            Assert.Equal(16, symbol.BarHeights.Length);
            Assert.All(symbol.BarHeights.ToArray(), height => Assert.Equal(first, height));
            Assert.All(symbol.BarTops.ToArray(), top => Assert.Equal(0, top));
        });

    /// <summary>
    /// With a font, the five digits of an EAN-5 add-on symbol print above the bars, and the bars keep their tops at the symbol top.
    /// </summary>
    [Fact]
    public void Ean5_TextPrintsAboveBars()
    {
        BarcodeOptions options = CreateTextOptions();

        SymbolEncoder.Encode(new Ean5Symbology(), "90200", options, static (ref symbol) =>
        {
            // GS1 General Specifications: the add-on interpretation prints above the bars, one digit above its own symbol character. The room it needs belongs
            // to the renderer, which is what knows the font, so the symbol itself leaves every bar top aligned.
            Assert.Equal(5, symbol.Text.Length);
            Assert.Equal("90200", string.Concat(SymbolEncoder.Texts(ref symbol)));
            Assert.All(symbol.Text.ToArray(), placement => Assert.Equal(BarcodeTextSide.AboveBars, placement.Side));
            Assert.All(symbol.Text.ToArray(), placement => Assert.Equal(-BarcodeTextPlacement.Clearance, placement.TextEdge));
            Assert.All(symbol.BarTops.ToArray(), top => Assert.Equal(0, top));
        });
    }

    /// <summary>
    /// The encoder rejects EAN-5 text that is not 5 digits.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    [Theory]
    [InlineData("9020")]
    [InlineData("902000")]
    [InlineData("9020A")]
    public void Ean5_RejectsInvalidInput(string text)
        => Assert.ThrowsAny<ArgumentException>(() => Encode(new Ean5Symbology(), text));

    /// <summary>
    /// An EAN-2 add-on symbol is 20 modules and 7 bars.
    /// </summary>
    [Fact]
    public void Ean2_Structure()
        => SymbolEncoder.Encode(new Ean2Symbology(), "05", new BarcodeOptions(), static (ref symbol) =>
        {
            // GS1 General Specifications Table 5-4: the 4 module add-on guard, two 7 module characters and one 2 module delineator make 20 modules. The
            // structure is fixed: 3 guard runs, 4 runs per digit character and 2 delineator runs, so 13 runs and therefore 7 bars.
            Assert.Equal(20, symbol.WidthInModules);
            Assert.Equal(7, symbol.BarHeights.Length);
        });

    /// <summary>
    /// The encoder rejects EAN-2 text that is not 2 digits.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    [Theory]
    [InlineData("5")]
    [InlineData("053")]
    [InlineData("5A")]
    public void Ean2_RejectsInvalidInput(string text)
        => Assert.ThrowsAny<ArgumentException>(() => Encode(new Ean2Symbology(), text));

    /// <summary>
    /// A bar height in the options sets the height of the digit bars in place of the nominal height. The guard bars stay 5 modules longer.
    /// </summary>
    [Fact]
    public void BarHeightOption_OverridesNominalHeight()
    {
        BarcodeOptions options = CreateTextOptions();
        options.Dpi = SymbolEncoder.MillimetreDpi;
        options.BarHeight = 50F * EanUpcEncoder.NominalXDimension;
        ApproximateFloatComparer comparer = new(1e-3F);

        SymbolEncoder.Encode(new Ean13Symbology(), "5901234123457", options, (ref symbol) =>
        {
            // 16.5 mm at the 0.33 mm X dimension is 50 modules for the digit bars. The guard bars gain the 5 module extension of section 5.2.3.2 of the GS1
            // General Specifications on top of that.
            Assert.Equal(50F, symbol.BarHeights[2], comparer);
            Assert.Equal(55F, symbol.BarHeights[0], comparer);
            Assert.Equal(55F, symbol.HeightInModules, comparer);
        });
    }

    /// <summary>
    /// Encodes the text with the default options.
    /// </summary>
    /// <param name="symbology">The symbology to encode with.</param>
    /// <param name="text">The text to encode.</param>
    private static void Encode(BarcodeSymbology symbology, string text)
        => SymbolEncoder.Encode(symbology, text, new BarcodeOptions());

    /// <summary>
    /// Encodes the text with the default options.
    /// </summary>
    /// <param name="symbology">The symbology to encode with.</param>
    /// <param name="text">The text to encode.</param>
    /// <returns>The run widths of the symbol, separated by commas.</returns>
    private static string Runs(BarcodeSymbology symbology, string text)
        => SymbolEncoder.Read(symbology, text, new BarcodeOptions(), RunsToString);

    /// <summary>
    /// Returns the first and the last column that hold a dark pixel in a region of the image.
    /// </summary>
    /// <param name="image">The rendered image.</param>
    /// <param name="left">The first column of the region.</param>
    /// <param name="right">The column after the region.</param>
    /// <param name="top">The first row of the region.</param>
    /// <param name="bottom">The row after the region.</param>
    /// <returns>The first and the last dark column.</returns>
    private static (int Left, int Right) DarkColumns(Image<L8> image, int left, int right, int top, int bottom)
    {
        int first = int.MaxValue;
        int last = int.MinValue;
        for (int y = top; y < bottom; y++)
        {
            for (int x = left; x < right; x++)
            {
                if (image[x, y].PackedValue < 128)
                {
                    first = Math.Min(first, x);
                    last = Math.Max(last, x);
                }
            }
        }

        return (first, last);
    }

    /// <summary>
    /// Returns the first and the last row that hold a dark pixel in a band of columns.
    /// </summary>
    /// <param name="image">The rendered image.</param>
    /// <param name="left">The first column of the band.</param>
    /// <param name="right">The column after the band.</param>
    /// <returns>The first and the last dark row.</returns>
    private static (int Top, int Bottom) DarkRows(Image<L8> image, int left, int right)
    {
        int first = int.MaxValue;
        int last = int.MinValue;
        for (int y = 0; y < image.Height; y++)
        {
            for (int x = left; x < right; x++)
            {
                if (image[x, y].PackedValue < 128)
                {
                    first = Math.Min(first, y);
                    last = Math.Max(last, y);
                }
            }
        }

        return (first, last);
    }

    /// <summary>
    /// Creates options with an OCR-B font, so the symbol carries its human readable text.
    /// </summary>
    /// <returns>The options.</returns>
    private static BarcodeOptions CreateTextOptions()
        => new()
        {
            Font = BarcodeFonts.OcrB.CreateFont(21.5F),
        };

    /// <summary>
    /// Returns the run widths of a symbol in whole modules, separated by commas.
    /// </summary>
    /// <param name="symbol">The encoded symbol.</param>
    /// <returns>The run widths.</returns>
    private static string RunsToString(ref BarcodeSymbol symbol)
        => SymbolEncoder.ModuleRuns(ref symbol);

    /// <summary>
    /// Section 5.2.3.1 of the GS1 General Specifications: for the characters 1, 2, 7 and 8 "the bars (dark bars) and spaces (light bars) are reduced or
    /// enlarged by one-thirteenth of a module", as Table 5-10 gives. In number set A a bar of 1 and 2 is reduced and a space enlarged, and 7 and 8 the
    /// opposite. Number sets B and C take the opposite signs. The runs count thirteenths of a module. 5901234123457 has the leading digit 5, so its left half
    /// takes the number sets A, B, B, A, A, B of Table 5-5. Its third left character is 1 in number set B, space 1, bar 2, space 2, bar 2 modules wide, and
    /// its fourth is 2 in number set A, 2, 1, 2, 2. Its right half takes number set C, bar first: 1 is 2, 2, 2, 1, 2 is 2, 1, 2, 2 and 7 is 1, 3, 1, 2.
    /// </summary>
    [Fact]
    public void CorrectsTheCharactersOfTableFiveTen()
        => SymbolEncoder.Encode(new Ean13Symbology(), "5901234123457", new BarcodeOptions(), static (ref symbol) =>
        {
            int[] runs = symbol.Runs.ToArray();

            Assert.Equal(1F / 13F, symbol.RunUnit);

            // The normal guard is 3 runs and each character 4, so the third left character starts at run 11 and the fourth at run 15. The centre guard is 5
            // runs, so the right half starts at run 32.
            Assert.Equal([12, 27, 25, 27], runs[11..15]);
            Assert.Equal([27, 12, 27, 25], runs[15..19]);
            Assert.Equal([27, 25, 27, 12], runs[32..36]);
            Assert.Equal([27, 12, 27, 25], runs[36..40]);
            Assert.Equal([12, 40, 12, 27], runs[52..56]);

            // The 0 in number set B at the second left character takes no correction.
            Assert.Equal([13, 13, 26, 39], runs[7..11]);
        });

    /// <summary>
    /// Bar edges round onto the pixel grid. At 2 and 3 pixels a module the one thirteenth of a module that Table 5-10 of the GS1 General Specifications
    /// corrects is 0.15 and 0.23 pixel, less than half a pixel, so every bar and space of 5901234123457 draws at its whole modules of pixels, wherever the
    /// symbol starts between two pixels. The modules are the whole module widths of the encoded symbol.
    /// </summary>
    /// <param name="dpi">The resolution, at a one point X dimension.</param>
    /// <param name="originX">The horizontal position of the symbol.</param>
    [Theory]
    [InlineData(144F, 0F)]
    [InlineData(144F, 0.25F)]
    [InlineData(144F, 0.4F)]
    [InlineData(144F, 0.5F)]
    [InlineData(144F, 0.6F)]
    [InlineData(144F, 0.75F)]
    [InlineData(216F, 0F)]
    [InlineData(216F, 0.4F)]
    [InlineData(216F, 0.5F)]
    [InlineData(216F, 0.75F)]
    public void DrawsWholeModulesWhereTheCorrectionIsUnderHalfAPixel(float dpi, float originX)
    {
        string modules = Runs(new Ean13Symbology(), "5901234123457");
        int pixels = (int)(dpi / 72F);
        string expected = string.Join(',', modules.Split(',').Select(m => int.Parse(m, CultureInfo.InvariantCulture) * pixels));

        Assert.Equal(expected, PixelRuns(new Ean13Symbology(), "5901234123457", dpi, originX));
    }

    /// <summary>
    /// At 13 pixels a module the one thirteenth of a module of Table 5-10 is one pixel, so each run draws at its run units of pixels, the corrected widths of
    /// <see cref="CorrectsTheCharactersOfTableFiveTen"/>, wherever the symbol starts between two pixels.
    /// </summary>
    /// <param name="originX">The horizontal position of the symbol.</param>
    [Theory]
    [InlineData(0F)]
    [InlineData(0.4F)]
    [InlineData(0.5F)]
    public void DrawsTheCorrectionAtThirteenPixelsAModule(float originX)
    {
        string runs = SymbolEncoder.Read(
            new Ean13Symbology(),
            "5901234123457",
            new BarcodeOptions(),
            static (ref symbol) => string.Join(',', symbol.Runs.ToArray()));

        Assert.Equal(runs, PixelRuns(new Ean13Symbology(), "5901234123457", 13F * 72F, originX));
    }

    /// <summary>
    /// Draws a symbol without quiet zones at a one point X dimension and returns the widths in pixels of its bars and spaces, read along one pixel row through
    /// the bars from the first dark pixel to the last.
    /// </summary>
    /// <param name="symbology">The symbology.</param>
    /// <param name="text">The text to encode.</param>
    /// <param name="dpi">The resolution.</param>
    /// <param name="originX">The horizontal position of the symbol.</param>
    /// <returns>The widths, separated by commas.</returns>
    private static string PixelRuns(BarcodeSymbology symbology, string text, float dpi, float originX)
    {
        BarcodeOptions options = new() { Dpi = dpi, XDimension = SymbolEncoder.PointXDimension, IncludeQuietZones = false };
        RectangleF bounds = BarcodeMeasurer.MeasureRenderableBounds(symbology, options, new PointF(originX, 0F), text);

        using Image<L8> image = new((int)MathF.Ceiling(bounds.Right) + 2, (int)MathF.Ceiling(bounds.Bottom), new L8(255));
        image.Mutate(x => x.Paint(canvas => canvas.DrawBarcode(symbology, options, new PointF(originX, 0F), text)));

        List<int> runs = [];
        bool dark = true;
        int width = 0;
        int y = (int)bounds.Top + 1;
        (int left, int right) = DarkColumns(image, 0, image.Width, y, y + 1);
        for (int x = left; x <= right; x++)
        {
            bool pixel = image[x, y].PackedValue < 128;
            if (pixel != dark)
            {
                runs.Add(width);
                dark = pixel;
                width = 0;
            }

            width++;
        }

        runs.Add(width);

        return string.Join(',', runs);
    }

    /// <summary>
    /// Checks the bar heights of a symbol with and without text. Section 5.2.3.2 of the GS1 General Specifications: the bars "forming the left, centre and
    /// right guard bar patterns SHALL be extended downward by 5x", and "This SHALL also apply to the bars (dark bars) of the first and last symbol characters
    /// of the UPC-A barcode." The rule does not depend on the human readable interpretation.
    /// </summary>
    /// <param name="symbology">The symbology to encode with.</param>
    /// <param name="text">The text to encode.</param>
    /// <param name="guardBars">The indexes of the bars that extend below the digit bars.</param>
    private static void AssertGuardExtension(BarcodeSymbology symbology, string text, int[] guardBars)
    {
        SymbolEncoder.Encode(symbology, text, new BarcodeOptions(), (ref plain) => AssertGuardBars(ref plain, guardBars));
        SymbolEncoder.Encode(symbology, text, CreateTextOptions(), (ref withText) => AssertGuardBars(ref withText, guardBars));
    }

    /// <summary>
    /// Checks that the listed bars descend exactly 5 modules below the digit bars and that every bar is top aligned.
    /// </summary>
    /// <param name="symbol">The encoded symbol.</param>
    /// <param name="guardBars">The indexes of the bars that extend below the digit bars.</param>
    private static void AssertGuardBars(ref BarcodeSymbol symbol, int[] guardBars)
    {
        float digitHeight = symbol.BarHeights.ToArray().Min();
        for (int i = 0; i < symbol.BarHeights.Length; i++)
        {
            float expected = Array.IndexOf(guardBars, i) >= 0 ? digitHeight + 5 : digitHeight;

            Assert.Equal(expected, symbol.BarHeights[i]);
            Assert.Equal(0, symbol.BarTops[i]);
        }
    }
}
