// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Barcodes;

namespace SixLabors.ImageSharp.Drawing.Tests.Barcodes;

/// <summary>
/// Encoder tests for the data layers over EAN-13 and EAN-8: ISBN (ISO 2108), ISMN (ISO 10957), ISSN (ISO 3297) and the Marks &amp; Spencer in-house symbology.
/// The caption and layout tests follow the standards. M&amp;S has no public specification, so its layout test follows a reference implementation.
/// </summary>
public class DataLayerSymbologyTests
{
    /// <summary>
    /// With a font, the ISBN symbol prints a caption above the bars. The caption is the hyphenated ISBN-13 with its check digit, and an ISBN-10 input prints
    /// its converted 978 form. Without a font, the symbol has no text. In both cases every bar top is at zero.
    /// </summary>
    [Fact]
    public void Isbn_CaptionAndLayout()
    {
        BarcodeOptions options = CreateTextOptions();

        SymbolEncoder.Encode(new IsbnSymbology(), "978-0-306-40615", options, (ref symbol) =>
        {
            // The caption prints the hyphenated ISBN with the EAN-13 check digit above the bars, spanning the main body of the symbol, and the bars shift down
            // to clear it. The emitter derives the caption size from that span, so the placement scale is neutral.
            BarcodeTextPlacement caption = symbol.Text[^1];
            Assert.Equal("ISBN 978-0-306-40615-7", symbol.TextOf(caption).ToString());
            Assert.Equal(BarcodeTextSide.AboveBars, caption.Side);
            Assert.Equal(-BarcodeTextPlacement.Clearance, caption.TextEdge);
            Assert.Equal(0, caption.Left);
            Assert.Equal(95, caption.Right);
            Assert.Equal(1F, caption.FontScale);
            Assert.True(caption.IsCaption);
            Assert.Equal(14, symbol.Text.Length);

            // The room a caption needs above the bars belongs to the renderer, which is what knows the font, so the symbol itself leaves every bar top aligned
            // whether or not it carries a caption.
            Assert.All(symbol.BarTops.ToArray(), top => Assert.Equal(0, top));
        });

        // An ISBN-10 input captions its converted 978 form.
        Assert.Equal("ISBN 978-0-306-40615-7", SymbolEncoder.Read(new IsbnSymbology(), "0-306-40615-2", options, SymbolEncoder.Texts)[^1]);

        // Without a font there is no caption and no strip.
        SymbolEncoder.Encode(new IsbnSymbology(), "978-0-306-40615-7", new BarcodeOptions(), (ref symbol) =>
        {
            Assert.True(symbol.Text.IsEmpty);
            Assert.All(symbol.BarTops.ToArray(), top => Assert.Equal(0, top));
        });
    }

    /// <summary>
    /// The inputs have a wrong check digit, a prefix outside 978 and 979, the wrong length or a wrong character. The GS1 General Specifications allocate the
    /// prefix 9790 to the ISMN, so the ISMN 979-0-3452-4680-5 of the ISMN users' manual is not an ISBN. The ISBN symbology rejects each input.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    [Theory]
    [InlineData("978-0-306-40615-5")]
    [InlineData("0-306-40615-3")]
    [InlineData("5901234123457")]
    [InlineData("12345")]
    [InlineData("97X-0-306-40615")]
    [InlineData("979-0-3452-4680-5")]
    public void Isbn_RejectsInvalidInput(string text)
        => Assert.Throws<ArgumentException>(() => Encode(new IsbnSymbology(), text));

    /// <summary>
    /// An ISBN-10 is the ISBN-13 with the prefix 978 and a new check digit, and the symbology calculates a missing check digit. The ISBN-10, the ISBN-13 and the
    /// ISBN-13 without its check digit give the same bars.
    /// </summary>
    [Fact]
    public void Isbn_EncodesEachFormOfOneIsbnTheSameWay()
    {
        string expected = Encode(new IsbnSymbology(), "978-0-306-40615-7");

        Assert.Equal(expected, Encode(new IsbnSymbology(), "978-0-306-40615"));
        Assert.Equal(expected, Encode(new IsbnSymbology(), "0-306-40615-2"));
    }

    /// <summary>
    /// The ISMN caption prints the 979-0 form with hyphens between the four elements of the number, for each input form.
    /// </summary>
    [Fact]
    public void Ismn_Caption()
    {
        BarcodeOptions options = CreateTextOptions();

        // ISO 10957:2009 displays an ISMN only in its 979-0 form, so an M form input converts for the caption.
        Assert.Equal("ISMN 979-0-2306-7118-7", SymbolEncoder.Read(new IsmnSymbology(), "M-2306-7118", options, SymbolEncoder.Texts)[^1]);

        // Hyphens are not data, so a hyphen in front of the M leaves the caption the same.
        Assert.Equal("ISMN 979-0-2306-7118-7", SymbolEncoder.Read(new IsmnSymbology(), "-M-2306-7118", options, SymbolEncoder.Texts)[^1]);

        Assert.Equal("ISMN 979-0-2600-0043-8", SymbolEncoder.Read(new IsmnSymbology(), "979-0-2600-0043-8", options, SymbolEncoder.Texts)[^1]);

        // Section 2.2 of the ISMN Users' Manual, 2008: "while there cannot be a publisher with the identifier 299 or 29910, there can be a publisher with the
        // identifier 2991", so the four elements of 9790299102349 print apart.
        Assert.Equal("ISMN 979-0-2991-0234-9", SymbolEncoder.Read(new IsmnSymbology(), "9790299102349", options, SymbolEncoder.Texts)[^1]);
    }

    /// <summary>
    /// The ISMN symbology rejects an M form with a wrong check digit, a prefix that is not 9790, an M form that is too short and an M form with a letter in
    /// the number.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    [Theory]
    [InlineData("M-2306-7118-5")]
    [InlineData("978-0-2600-0043")]
    [InlineData("M-2306-711")]
    [InlineData("M-2306-711A")]
    public void Ismn_RejectsInvalidInput(string text)
        => Assert.Throws<ArgumentException>(() => Encode(new IsmnSymbology(), text));

    /// <summary>
    /// The ISSN caption prints the hyphenated ISSN with its check character. It does not print the sequence variant.
    /// </summary>
    [Fact]
    public void Issn_Caption()
    {
        BarcodeOptions options = CreateTextOptions();

        Assert.Equal("ISSN 0317-8471", SymbolEncoder.Read(new IssnSymbology(), "0317-8471", options, SymbolEncoder.Texts)[^1]);

        // The caption prints the ISSN check character, X for ten, not the sequence variant.
        Assert.Equal("ISSN 2434-561X", SymbolEncoder.Read(new IssnSymbology(), "2434-561X 05", options, SymbolEncoder.Texts)[^1]);
    }

    /// <summary>
    /// The ISSN symbology rejects a wrong check character, an ISSN without its hyphen, a sequence variant of one digit and an X that is not the check
    /// character.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    [Theory]
    [InlineData("0317-8476")]
    [InlineData("03178471")]
    [InlineData("0317-8471 5")]
    [InlineData("0317-84X1")]
    public void Issn_RejectsInvalidInput(string text)
        => Assert.Throws<ArgumentException>(() => Encode(new IssnSymbology(), text));

    /// <summary>
    /// The M&amp;S symbol keeps the centre guard bars at digit height and extends the outer guard bars. It prints the seven digits of the number, and M and S
    /// in the quiet zones.
    /// </summary>
    [Fact]
    public void Mands_Layout()
    {
        BarcodeOptions options = CreateTextOptions();

        SymbolEncoder.Encode(new MandsSymbology(), "0642118", options, (ref symbol) =>
        {
            // Per the reference implementation: the centre guard bars stay at digit height while the outer guards extend, the padded leading zero and the final
            // cell are hidden, and M and S print in the quiet zones.
            float digitHeight = symbol.BarHeights.ToArray().Min();
            Assert.Equal(digitHeight, symbol.BarHeights[10]);
            Assert.Equal(digitHeight, symbol.BarHeights[11]);
            Assert.Equal(digitHeight + 5, symbol.BarHeights[0]);
            Assert.Equal(digitHeight + 5, symbol.BarHeights[21]);

            string[] texts = SymbolEncoder.Texts(ref symbol);
            Assert.Equal(9, texts.Length);
            Assert.Equal("0642118", string.Concat(texts.Take(7)));
            Assert.Equal("M", texts[7]);
            Assert.Equal("S", texts[8]);
            Assert.True(symbol.Text[7].Left < 0);
            Assert.True(symbol.Text[8].Left > symbol.WidthInModules);
        });
    }

    /// <summary>
    /// The M&amp;S symbology rejects a number that is too short, a number that is too long, a wrong check digit and a letter in the number.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    [Theory]
    [InlineData("064211")]
    [InlineData("006421187")]
    [InlineData("0642119")]
    [InlineData("064211A")]
    public void Mands_RejectsInvalidInput(string text)
        => Assert.Throws<ArgumentException>(() => Encode(new MandsSymbology(), text));

    /// <summary>
    /// Encodes the text with the default options.
    /// </summary>
    /// <param name="symbology">The symbology to encode with.</param>
    /// <param name="text">The text to encode.</param>
    /// <returns>The run widths of the symbol, separated by commas.</returns>
    private static string Encode(BarcodeSymbology symbology, string text)
        => SymbolEncoder.Read(symbology, text, new BarcodeOptions(), SymbolEncoder.ModuleRuns);

    /// <summary>
    /// Creates options at the default X dimension, a bar height of 50 default X dimensions, 144 dots per inch and the OCR-B font at 10.75 points.
    /// </summary>
    /// <returns>The options.</returns>
    private static BarcodeOptions CreateTextOptions()
        => new()
        {
            Dpi = 144F,
            XDimension = BarcodeSymbology.DefaultXDimension,
            BarHeight = 50F * BarcodeSymbology.DefaultXDimension,
            Font = BarcodeFonts.OcrB.CreateFont(10.75F),
        };
}
