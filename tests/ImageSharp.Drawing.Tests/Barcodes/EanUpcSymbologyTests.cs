// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Barcodes;

namespace SixLabors.ImageSharp.Drawing.Tests.Barcodes;

/// <summary>
/// Encoder tests for the EAN/UPC symbology family. Module sequences are asserted against reference vectors
/// generated with an independent reference implementation, so a matching error in the expected values
/// cannot conceal an error in our encodation tables. Structural assertions (symbol width, bar counts,
/// guard bar extension, quiet zones, text layout) follow ISO/IEC 15420 directly.
/// </summary>
public class EanUpcSymbologyTests
{
    [Theory]
    [InlineData("5901234123457", "1,1,1,3,1,1,2,1,1,2,3,1,2,2,2,2,1,2,2,1,4,1,1,2,3,1,1,1,1,1,1,1,2,2,2,1,2,1,2,2,1,4,1,1,1,1,3,2,1,2,3,1,1,3,1,2,1,1,1")]
    [InlineData("4006381333931", "1,1,1,3,2,1,1,1,1,2,3,1,1,1,4,1,4,1,1,3,1,2,1,1,2,2,2,1,1,1,1,1,1,4,1,1,1,4,1,1,1,4,1,1,3,1,1,2,1,4,1,1,2,2,2,1,1,1,1")]
    [InlineData("9780306406157", "1,1,1,1,3,1,2,3,1,2,1,1,1,2,3,1,4,1,1,1,1,2,3,1,1,1,4,1,1,1,1,1,1,1,3,2,3,2,1,1,1,1,1,4,2,2,2,1,1,2,3,1,1,3,1,2,1,1,1")]
    public void Ean13_EncodesExpectedModuleSequence(string text, string expectedRuns)
        => Assert.Equal(expectedRuns, RunsToString(Encode(new Ean13Symbology(), text)));

    [Fact]
    public void Ean13_ComputesMissingCheckDigit()
        => Assert.Equal(
            RunsToString(Encode(new Ean13Symbology(), "5901234123457")),
            RunsToString(Encode(new Ean13Symbology(), "590123412345")));

    [Fact]
    public void Ean13_Structure()
    {
        LinearBarcodeSymbol symbol = Encode(new Ean13Symbology(), "5901234123457");

        // ISO/IEC 15420: 95 modules, 30 bars, an 11 module leading and 7 module trailing quiet zone.
        Assert.Equal(95, symbol.WidthInModules);
        Assert.Equal(30, symbol.BarHeights.Length);
        Assert.Equal(11, symbol.LeadingQuietZone);
        Assert.Equal(7, symbol.TrailingQuietZone);

        AssertGuardExtension(new Ean13Symbology(), "5901234123457", [0, 1, 14, 15, 28, 29]);
    }

    [Fact]
    public void Ean13_TextPlacements()
    {
        BarcodeOptions options = CreateTextOptions();
        LinearBarcodeSymbol symbol = (LinearBarcodeSymbol)new Ean13Symbology().Encode("5901234123457", options);

        // ISO/IEC 15420: the leading digit prints in the leading quiet zone and every other digit prints
        // below its own symbol character, so the placement is per digit. Digits hang one module below the
        // digit bars; the renderer grows the drawn area to hold them.
        Assert.Equal(13, symbol.Text.Length);
        Assert.Equal("5901234123457", string.Concat(symbol.Text.Select(placement => placement.Text)));
        Assert.True(symbol.Text[0].Left < 0);
        Assert.All(symbol.Text, placement => Assert.Equal(BarcodeTextSide.BelowBars, placement.Side));
        Assert.All(symbol.Text, placement => Assert.Equal(symbol.BarHeights.Min(), placement.BarEdge));

        Assert.Empty(Encode(new Ean13Symbology(), "5901234123457").Text);
    }

    [Theory]
    [InlineData("12345678901")]
    [InlineData("12345678901234")]
    [InlineData("59012341234A")]
    [InlineData("5901234123450")]
    public void Ean13_RejectsInvalidInput(string text)
        => Assert.ThrowsAny<ArgumentException>(() => Encode(new Ean13Symbology(), text));

    [Fact]
    public void Ean8_EncodesExpectedModuleSequence()
        => Assert.Equal(
            "1,1,1,3,1,1,2,1,1,1,4,1,4,1,1,1,2,1,3,1,1,1,1,1,1,2,3,1,3,2,1,1,1,3,1,2,1,1,3,2,1,1,1",
            RunsToString(Encode(new Ean8Symbology(), "96385074")));

    [Fact]
    public void Ean8_Structure()
    {
        LinearBarcodeSymbol symbol = Encode(new Ean8Symbology(), "9638507");

        // ISO/IEC 15420: 67 modules, 22 bars, 7 module quiet zones on both sides. The 7 digit input
        // verifies check digit computation: 96385074 carries check digit 4.
        Assert.Equal(67, symbol.WidthInModules);
        Assert.Equal(22, symbol.BarHeights.Length);
        Assert.Equal(7, symbol.LeadingQuietZone);
        Assert.Equal(7, symbol.TrailingQuietZone);
        Assert.Equal(RunsToString(Encode(new Ean8Symbology(), "96385074")), RunsToString(symbol));

        AssertGuardExtension(new Ean8Symbology(), "96385074", [0, 1, 10, 11, 20, 21]);
    }

    [Theory]
    [InlineData("963850")]
    [InlineData("963850741")]
    [InlineData("9638507A")]
    [InlineData("96385071")]
    public void Ean8_RejectsInvalidInput(string text)
        => Assert.ThrowsAny<ArgumentException>(() => Encode(new Ean8Symbology(), text));

    [Fact]
    public void UpcA_EncodesExpectedModuleSequence()
        => Assert.Equal(
            "1,1,1,3,2,1,1,1,4,1,1,1,1,1,4,3,2,1,1,3,2,1,1,3,2,1,1,1,1,1,1,1,2,1,2,2,3,1,1,2,2,2,2,1,1,1,3,2,1,2,3,1,2,1,2,2,1,1,1",
            RunsToString(Encode(new UpcASymbology(), "036000291452")));

    [Fact]
    public void UpcA_Structure()
    {
        LinearBarcodeSymbol symbol = Encode(new UpcASymbology(), "03600029145");

        // ISO/IEC 15420: the UPC-A symbol shares the 95 module EAN-13 structure with 9 module quiet zones.
        // The bars of the first and last symbol characters are extended because their digits print in the
        // quiet zones. The 11 digit input verifies check digit computation: 036000291452 carries check digit 2.
        Assert.Equal(95, symbol.WidthInModules);
        Assert.Equal(9, symbol.LeadingQuietZone);
        Assert.Equal(9, symbol.TrailingQuietZone);
        Assert.Equal(RunsToString(Encode(new UpcASymbology(), "036000291452")), RunsToString(symbol));

        AssertGuardExtension(new UpcASymbology(), "036000291452", [0, 1, 2, 3, 14, 15, 26, 27, 28, 29]);
    }

    [Fact]
    public void UpcA_TextPlacements()
    {
        BarcodeOptions options = CreateTextOptions();
        LinearBarcodeSymbol symbol = (LinearBarcodeSymbol)new UpcASymbology().Encode("036000291452", options);

        // ISO/IEC 15420: the number system digit prints in the leading quiet zone, the check digit in the
        // trailing quiet zone, and every other digit below its own symbol character. The quiet zone digits
        // print in smaller type: 10/12 of the digit size, matching the reference implementation.
        Assert.Equal(12, symbol.Text.Length);
        Assert.Equal("036000291452", string.Concat(symbol.Text.Select(placement => placement.Text)));
        Assert.True(symbol.Text[0].Left < 0);
        Assert.True(symbol.Text[11].Left > symbol.WidthInModules);
        Assert.Equal(10F / 12F, symbol.Text[0].FontScale);
        Assert.Equal(10F / 12F, symbol.Text[11].FontScale);
        Assert.All(symbol.Text.Skip(1).Take(10), placement => Assert.Equal(1F, placement.FontScale));
    }

    /// <summary>
    /// One vector per zero suppression pattern of ISO/IEC 15420: the last compressed digit selects how the
    /// UPC-E digits expand to a UPC-A number, and the check digit is computed over the expanded number.
    /// The final case covers number system 1, which inverts the parity pattern.
    /// </summary>
    [Theory]
    [InlineData("01234565", "1,1,1,1,2,2,2,2,1,2,2,1,4,1,1,2,3,1,1,1,3,2,1,1,1,1,4,1,1,1,1,1,1")]
    [InlineData("0425261", "1,1,1,2,3,1,1,2,1,2,2,1,3,2,1,2,2,1,2,1,1,1,4,2,2,2,1,1,1,1,1,1,1")]
    [InlineData("0123453", "1,1,1,1,2,2,2,2,2,1,2,1,4,1,1,2,3,1,1,1,2,3,1,1,4,1,1,1,1,1,1,1,1")]
    [InlineData("0291944", "1,1,1,2,2,1,2,2,1,1,3,2,2,2,1,2,1,1,3,1,1,3,2,1,1,3,2,1,1,1,1,1,1")]
    [InlineData("1654321", "1,1,1,1,1,1,4,1,3,2,1,1,1,3,2,1,4,1,1,2,2,1,2,1,2,2,2,1,1,1,1,1,1")]
    public void UpcE_EncodesExpectedModuleSequence(string text, string expectedRuns)
        => Assert.Equal(expectedRuns, RunsToString(Encode(new UpcESymbology(), text)));

    [Fact]
    public void UpcE_Structure()
    {
        LinearBarcodeSymbol symbol = Encode(new UpcESymbology(), "0123456");

        // ISO/IEC 15420: 51 modules, 17 bars, a 9 module leading and 7 module trailing quiet zone. All five
        // guard bars are extended: two in the left guard pattern and three in the special right guard pattern.
        Assert.Equal(51, symbol.WidthInModules);
        Assert.Equal(17, symbol.BarHeights.Length);
        Assert.Equal(9, symbol.LeadingQuietZone);
        Assert.Equal(7, symbol.TrailingQuietZone);
        Assert.Equal(RunsToString(Encode(new UpcESymbology(), "01234565")), RunsToString(symbol));

        AssertGuardExtension(new UpcESymbology(), "01234565", [0, 1, 14, 15, 16]);
    }

    [Theory]
    [InlineData("2123456")]
    [InlineData("012345")]
    [InlineData("012345678")]
    [InlineData("012345A")]
    [InlineData("01234560")]
    public void UpcE_RejectsInvalidInput(string text)
        => Assert.ThrowsAny<ArgumentException>(() => Encode(new UpcESymbology(), text));

    [Theory]
    [InlineData("90200", "1,1,2,2,1,1,3,1,1,3,2,1,1,1,1,2,1,2,2,1,1,3,2,1,1,1,1,1,1,2,3")]
    [InlineData("52495", "1,1,2,1,3,2,1,1,1,2,1,2,2,1,1,2,3,1,1,1,1,3,1,1,2,1,1,1,2,3,1")]
    public void Ean5_EncodesExpectedModuleSequence(string text, string expectedRuns)
        => Assert.Equal(expectedRuns, RunsToString(Encode(new Ean5Symbology(), text)));

    [Fact]
    public void Ean5_Structure()
    {
        LinearBarcodeSymbol symbol = Encode(new Ean5Symbology(), "90200");

        // GS1 General Specifications: 47 modules once the leading space module of the add-on guard pattern
        // is folded into the quiet zone; all add-on bars are uniform in height.
        Assert.Equal(47, symbol.WidthInModules);
        Assert.Equal(16, symbol.BarHeights.Length);
        Assert.All(symbol.BarHeights, height => Assert.Equal(symbol.BarHeights[0], height));
        Assert.All(symbol.BarTops, top => Assert.Equal(0, top));
    }

    [Fact]
    public void Ean5_TextPrintsAboveBars()
    {
        BarcodeOptions options = CreateTextOptions();
        LinearBarcodeSymbol symbol = (LinearBarcodeSymbol)new Ean5Symbology().Encode("90200", options);

        // GS1 General Specifications: the add-on interpretation prints above the bars, one digit above its
        // own symbol character. The room it needs belongs to the renderer, which is what knows the font,
        // so the symbol itself leaves every bar top aligned.
        Assert.Equal(5, symbol.Text.Length);
        Assert.Equal("90200", string.Concat(symbol.Text.Select(placement => placement.Text)));
        Assert.All(symbol.Text, placement => Assert.Equal(BarcodeTextSide.AboveBars, placement.Side));
        Assert.All(symbol.Text, placement => Assert.Equal(0, placement.BarEdge));
        Assert.All(symbol.BarTops, top => Assert.Equal(0, top));
    }

    [Theory]
    [InlineData("9020")]
    [InlineData("902000")]
    [InlineData("9020A")]
    public void Ean5_RejectsInvalidInput(string text)
        => Assert.ThrowsAny<ArgumentException>(() => Encode(new Ean5Symbology(), text));

    [Theory]
    [InlineData("05", "1,1,2,3,2,1,1,1,1,1,3,2,1")]
    [InlineData("53", "1,1,2,1,2,3,1,1,1,1,1,4,1")]
    public void Ean2_EncodesExpectedModuleSequence(string text, string expectedRuns)
        => Assert.Equal(expectedRuns, RunsToString(Encode(new Ean2Symbology(), text)));

    [Fact]
    public void Ean2_Structure()
    {
        LinearBarcodeSymbol symbol = Encode(new Ean2Symbology(), "05");

        // GS1 General Specifications: 20 modules once the leading space module of the add-on guard pattern
        // is folded into the quiet zone. The structure is fixed: 3 guard runs, 4 runs per digit character
        // and 2 delineator runs, so 13 runs and therefore 7 bars.
        Assert.Equal(20, symbol.WidthInModules);
        Assert.Equal(7, symbol.BarHeights.Length);
    }

    [Theory]
    [InlineData("5")]
    [InlineData("053")]
    [InlineData("5A")]
    public void Ean2_RejectsInvalidInput(string text)
        => Assert.ThrowsAny<ArgumentException>(() => Encode(new Ean2Symbology(), text));

    [Fact]
    public void BarHeightOption_OverridesNominalHeight()
    {
        BarcodeOptions options = CreateTextOptions();
        options.ModuleWidth = 2F;
        options.BarHeight = 100F;

        LinearBarcodeSymbol symbol = (LinearBarcodeSymbol)new Ean13Symbology().Encode("5901234123457", options);

        // 100 pixels at 2 pixels per module is 50 modules for the digit bars; with text enabled the guard
        // bars gain the 5 module extension of ISO/IEC 15420 on top of that.
        Assert.Equal(50, symbol.BarHeights[2]);
        Assert.Equal(55, symbol.BarHeights[0]);
        Assert.Equal(55, symbol.HeightInModules);
    }

    private static LinearBarcodeSymbol Encode(BarcodeSymbology symbology, string text)
        => (LinearBarcodeSymbol)symbology.Encode(text, new BarcodeOptions());

    private static BarcodeOptions CreateTextOptions()
        => new()
        {
            Font = BarcodeFonts.OcrB.CreateFont(21.5F),
        };

    private static string RunsToString(LinearBarcodeSymbol symbol)
        => string.Join(',', symbol.RunWidths);

    private static void AssertGuardExtension(BarcodeSymbology symbology, string text, ReadOnlySpan<int> guardBars)
    {
        // The guard extension flanks the text row, so it applies only when text is enabled: without a font
        // every bar is uniform (matching the reference implementation); with a font the listed bars descend exactly 5 modules below
        // the digit bars. Every bar is top aligned in both modes.
        LinearBarcodeSymbol plain = Encode(symbology, text);
        Assert.All(plain.BarHeights, height => Assert.Equal(plain.BarHeights[0], height));

        LinearBarcodeSymbol withText = (LinearBarcodeSymbol)symbology.Encode(text, CreateTextOptions());
        float digitHeight = withText.BarHeights.Min();
        for (int i = 0; i < withText.BarHeights.Length; i++)
        {
            float expected = guardBars.IndexOf(i) >= 0 ? digitHeight + 5 : digitHeight;
            Assert.Equal(expected, withText.BarHeights[i]);
            Assert.Equal(0, withText.BarTops[i]);
        }
    }
}
