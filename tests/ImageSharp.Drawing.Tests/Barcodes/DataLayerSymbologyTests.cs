// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Barcodes;

namespace SixLabors.ImageSharp.Drawing.Tests.Barcodes;

/// <summary>
/// Encoder tests for the data layers over EAN-13 and EAN-8: ISBN (ISO 2108), ISMN (ISO 10957),
/// ISSN (ISO 3297) and the Marks &amp; Spencer in-house symbology. Module sequences are asserted against
/// reference vectors generated with an independent reference implementation; caption and layout assertions
/// follow the standards and, for M&amp;S which has no public specification, that implementation itself.
/// </summary>
public class DataLayerSymbologyTests
{
    /// <summary>
    /// All accepted ISBN input forms encode the same EAN-13: full ISBN-13, ISBN-13 without its check digit,
    /// ISBN-10 with its modulus 11 check digit, and ISBN-10 without it (the second pair converts through the
    /// 978 prefix; the last case computes an X check internally, proving X handling).
    /// </summary>
    [Theory]
    [InlineData("978-0-306-40615-7", "1,1,1,1,3,1,2,3,1,2,1,1,1,2,3,1,4,1,1,1,1,2,3,1,1,1,4,1,1,1,1,1,1,1,3,2,3,2,1,1,1,1,1,4,2,2,2,1,1,2,3,1,1,3,1,2,1,1,1")]
    [InlineData("978-0-306-40615", "1,1,1,1,3,1,2,3,1,2,1,1,1,2,3,1,4,1,1,1,1,2,3,1,1,1,4,1,1,1,1,1,1,1,3,2,3,2,1,1,1,1,1,4,2,2,2,1,1,2,3,1,1,3,1,2,1,1,1")]
    [InlineData("0-306-40615-2", "1,1,1,1,3,1,2,3,1,2,1,1,1,2,3,1,4,1,1,1,1,2,3,1,1,1,4,1,1,1,1,1,1,1,3,2,3,2,1,1,1,1,1,4,2,2,2,1,1,2,3,1,1,3,1,2,1,1,1")]
    [InlineData("3-540-49698", "1,1,1,1,3,1,2,3,1,2,1,1,1,4,1,1,2,3,1,2,3,1,1,3,2,1,1,1,1,1,1,1,1,1,3,2,3,1,1,2,1,1,1,4,3,1,1,2,1,2,1,3,1,1,3,2,1,1,1")]
    public void Isbn_EncodesExpectedModuleSequence(string text, string expectedRuns)
        => Assert.Equal(expectedRuns, RunsToString(Encode(new IsbnSymbology(), text)));

    [Fact]
    public void Isbn_CaptionAndLayout()
    {
        BarcodeOptions options = CreateTextOptions();
        LinearBarcodeSymbol symbol = (LinearBarcodeSymbol)new IsbnSymbology().Encode("978-0-306-40615", options);

        // The caption prints the hyphenated ISBN with the EAN-13 check digit above the bars, spanning the
        // main body of the symbol, and the bars shift down to clear it. The emitter derives the caption
        // size from that span, so the placement scale is neutral.
        BarcodeTextPlacement caption = symbol.Text[symbol.Text.Length - 1];
        Assert.Equal("ISBN 978-0-306-40615-7", caption.Text);
        Assert.Equal(BarcodeTextSide.AboveBars, caption.Side);
        Assert.Equal(0, caption.BarEdge);
        Assert.Equal(0, caption.Left);
        Assert.Equal(95, caption.Right);
        Assert.Equal(1F, caption.FontScale);
        Assert.True(caption.IsCaption);
        Assert.Equal(14, symbol.Text.Length);

        // The room a caption needs above the bars belongs to the renderer, which is what knows the font,
        // so the symbol itself leaves every bar top aligned whether or not it carries a caption.
        Assert.All(symbol.BarTops, top => Assert.Equal(0, top));

        // An ISBN-10 input captions its converted 978 form.
        symbol = (LinearBarcodeSymbol)new IsbnSymbology().Encode("0-306-40615-2", options);
        Assert.Equal("ISBN 978-0-306-40615-7", symbol.Text[symbol.Text.Length - 1].Text);

        // Without a font there is no caption and no strip.
        symbol = Encode(new IsbnSymbology(), "978-0-306-40615-7");
        Assert.Empty(symbol.Text);
        Assert.All(symbol.BarTops, top => Assert.Equal(0, top));
    }

    [Theory]
    [InlineData("978-0-306-40615-5")]
    [InlineData("0-306-40615-3")]
    [InlineData("5901234123457")]
    [InlineData("12345")]
    [InlineData("97X-0-306-40615")]
    public void Isbn_RejectsInvalidInput(string text)
        => Assert.Throws<ArgumentException>(() => Encode(new IsbnSymbology(), text));

    /// <summary>
    /// Both ISMN input forms: the thirteen digit 9790 form with its check digit, and the older M form
    /// without one. ISO 10957:2009 defines the check digit of both forms as the EAN-13 check digit.
    /// </summary>
    [Theory]
    [InlineData("979-0-2600-0043-8", "1,1,1,1,3,1,2,2,1,1,3,1,1,2,3,2,1,2,2,4,1,1,1,3,2,1,1,1,1,1,1,1,3,2,1,1,3,2,1,1,3,2,1,1,1,1,3,2,1,4,1,1,1,2,1,3,1,1,1")]
    [InlineData("M-2306-7118", "1,1,1,1,3,1,2,2,1,1,3,1,1,2,3,2,1,2,2,1,1,4,1,3,2,1,1,1,1,1,1,1,1,1,1,4,1,3,1,2,2,2,2,1,2,2,2,1,1,2,1,3,1,3,1,2,1,1,1")]
    public void Ismn_EncodesExpectedModuleSequence(string text, string expectedRuns)
        => Assert.Equal(expectedRuns, RunsToString(Encode(new IsmnSymbology(), text)));

    [Fact]
    public void Ismn_Caption()
    {
        BarcodeOptions options = CreateTextOptions();

        // ISO 10957:2009 displays an ISMN only in its 979-0 form, so an M form input converts for the caption.
        LinearBarcodeSymbol symbol = (LinearBarcodeSymbol)new IsmnSymbology().Encode("M-2306-7118", options);
        Assert.Equal("ISMN 979-0-2306-7118-7", symbol.Text[symbol.Text.Length - 1].Text);

        symbol = (LinearBarcodeSymbol)new IsmnSymbology().Encode("979-0-2600-0043-8", options);
        Assert.Equal("ISMN 979-0-2600-0043-8", symbol.Text[symbol.Text.Length - 1].Text);
    }

    [Theory]
    [InlineData("M-2306-7118-5")]
    [InlineData("978-0-2600-0043")]
    [InlineData("M-2306-711")]
    [InlineData("M-2306-711A")]
    public void Ismn_RejectsInvalidInput(string text)
        => Assert.Throws<ArgumentException>(() => Encode(new IsmnSymbology(), text));

    /// <summary>
    /// The ISSN encodes 977, the seven data digits and the sequence variant; the second case supplies an
    /// X check character and an explicit 05 variant.
    /// </summary>
    [Theory]
    [InlineData("0317-8471", "1,1,1,1,3,1,2,2,1,3,1,1,1,2,3,1,4,1,1,1,2,2,2,1,3,1,2,1,1,1,1,1,1,2,1,3,1,1,3,2,1,3,1,2,3,2,1,1,3,2,1,1,2,2,2,1,1,1,1")]
    [InlineData("2434-561X 05", "1,1,1,1,3,1,2,2,1,3,1,2,2,1,2,1,1,3,2,1,1,4,1,1,1,3,2,1,1,1,1,1,1,2,3,1,1,1,1,4,2,2,2,1,3,2,1,1,1,2,3,1,2,2,2,1,1,1,1")]
    public void Issn_EncodesExpectedModuleSequence(string text, string expectedRuns)
        => Assert.Equal(expectedRuns, RunsToString(Encode(new IssnSymbology(), text)));

    [Fact]
    public void Issn_Caption()
    {
        BarcodeOptions options = CreateTextOptions();
        LinearBarcodeSymbol symbol = (LinearBarcodeSymbol)new IssnSymbology().Encode("0317-8471", options);
        Assert.Equal("ISSN 0317-8471", symbol.Text[symbol.Text.Length - 1].Text);

        // The caption prints the ISSN check character, X for ten, not the sequence variant.
        symbol = (LinearBarcodeSymbol)new IssnSymbology().Encode("2434-561X 05", options);
        Assert.Equal("ISSN 2434-561X", symbol.Text[symbol.Text.Length - 1].Text);
    }

    [Theory]
    [InlineData("0317-8476")]
    [InlineData("03178471")]
    [InlineData("0317-8471 5")]
    [InlineData("0317-84X1")]
    public void Issn_RejectsInvalidInput(string text)
        => Assert.Throws<ArgumentException>(() => Encode(new IssnSymbology(), text));

    /// <summary>
    /// A seven character M&amp;S number zero pads to the same eight digit EAN-8 as its full form, so both
    /// inputs produce identical bars.
    /// </summary>
    [Theory]
    [InlineData("0642118")]
    [InlineData("00642118")]
    public void Mands_EncodesExpectedModuleSequence(string text)
        => Assert.Equal(
            "1,1,1,3,2,1,1,3,2,1,1,1,1,1,4,1,1,3,2,1,1,1,1,1,2,1,2,2,2,2,2,1,2,2,2,1,1,2,1,3,1,1,1",
            RunsToString(Encode(new MandsSymbology(), text)));

    [Fact]
    public void Mands_Layout()
    {
        BarcodeOptions options = CreateTextOptions();
        LinearBarcodeSymbol symbol = (LinearBarcodeSymbol)new MandsSymbology().Encode("0642118", options);

        // Per the reference implementation: the centre guard bars stay at digit height while the outer guards extend, the padded
        // leading zero and the final cell are hidden, and M and S print in the quiet zones.
        float digitHeight = symbol.BarHeights.Min();
        Assert.Equal(digitHeight, symbol.BarHeights[10]);
        Assert.Equal(digitHeight, symbol.BarHeights[11]);
        Assert.Equal(digitHeight + 5, symbol.BarHeights[0]);
        Assert.Equal(digitHeight + 5, symbol.BarHeights[21]);

        Assert.Equal(9, symbol.Text.Length);
        Assert.Equal("0642118", string.Concat(symbol.Text.Take(7).Select(placement => placement.Text)));
        Assert.Equal("M", symbol.Text[7].Text);
        Assert.Equal("S", symbol.Text[8].Text);
        Assert.True(symbol.Text[7].Left < 0);
        Assert.True(symbol.Text[8].Left > symbol.WidthInModules);
    }

    [Theory]
    [InlineData("064211")]
    [InlineData("006421187")]
    [InlineData("0642119")]
    [InlineData("064211A")]
    public void Mands_RejectsInvalidInput(string text)
        => Assert.Throws<ArgumentException>(() => Encode(new MandsSymbology(), text));

    private static LinearBarcodeSymbol Encode(BarcodeSymbology symbology, string text)
        => (LinearBarcodeSymbol)symbology.Encode(text, new BarcodeOptions());

    private static BarcodeOptions CreateTextOptions()
        => new()
        {
            ModuleWidth = 2F,
            BarHeight = 100F,
            Font = BarcodeFonts.OcrB.CreateFont(21.5F),
        };

    private static string RunsToString(LinearBarcodeSymbol symbol)
        => string.Join(',', symbol.RunWidths);
}
