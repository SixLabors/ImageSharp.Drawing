// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Barcodes;

namespace SixLabors.ImageSharp.Drawing.Tests.Barcodes;

/// <summary>
/// Encoder tests for <see cref="TelepenSymbology"/> and <see cref="TelepenNumericSymbology"/>. Each
/// expected run string is the alternating bar and space module widths, starting with a bar, from the
/// reference implementation's raw encoding API. The reference emits the narrow space that ends the stop
/// code, so the test drops that trailing space before it compares.
/// </summary>
public class TelepenSymbologyTests
{
    /// <summary>
    /// The bars and spaces of every ASCII character, indexed by value, from the reference
    /// implementation's table. The encoder derives them from the bit rules, so every entry is a check of
    /// those rules.
    /// </summary>
    private static readonly string[] Patterns =
    [
        "31313131", "1131313111", "33313111", "1111313131", "3111313111", "11333131", "13133131", "111111313111",
        "31333111", "1131113131", "33113131", "1111333111", "3111113131", "1113133111", "1311133111", "111111113131",
        "3131113111", "11313331", "333331", "111131113111", "31113331", "1133113111", "1313113111", "1111113331",
        "31131331", "113111113111", "3311113111", "1111131331", "311111113111", "1113111331", "1311111331", "11111111113111",
        "31313311", "1131311131", "33311131", "1111313311", "3111311131", "11333311", "13133311", "111111311131",
        "31331131", "1131113311", "33113311", "1111331131", "3111113311", "1113131131", "1311131131", "111111113311",
        "3131111131", "1131131311", "33131311", "111131111131", "3111131311", "1133111131", "1313111131", "111111131311",
        "3113111311", "113111111131", "3311111131", "111113111311", "311111111131", "111311111311", "131111111311", "11111111111131",
        "3131311111", "11313133", "333133", "111131311111", "31113133", "1133311111", "1313311111", "1111113133",
        "313333", "113111311111", "3311311111", "11113333", "311111311111", "11131333", "13111333", "11111111311111",
        "31311133", "1131331111", "33331111", "1111311133", "3111331111", "11331133", "13131133", "111111331111",
        "3113131111", "1131111133", "33111133", "111113131111", "3111111133", "111311131111", "131111131111", "111111111133",
        "31311313", "113131111111", "3331111111", "1111311313", "311131111111", "11331313", "13131313", "11111131111111",
        "3133111111", "1131111313", "33111313", "111133111111", "3111111313", "111313111111", "131113111111", "111111111313",
        "313111111111", "1131131113", "33131113", "11113111111111", "3111131113", "113311111111", "131311111111", "111111131113",
        "3113111113", "11311111111111", "331111111111", "111113111113", "31111111111111", "111311111113", "131111111113", "1111111111111111",
    ];

    [Theory]
    [InlineData("ABC123", "111111111133113131333331331111313111111131131311331313111111311111311131311131331111111111")]
    [InlineData("A", "11111111113311313133131111111311331111111111")]
    [InlineData("Telepen", "111111111133311133111111331313311111131311331313313111111111113313131311131111111113131131331111111111")]
    public void MatchesReferenceRuns(string text, string reference)
        => Assert.Equal(reference[..^1], string.Concat(Encode(text).RunWidths));

    /// <summary>
    /// Every ASCII character encodes to the bars and spaces of the table. A symbol of one character is
    /// the start code, the character, its check character and the stop code. For a value from 1 to 126
    /// the check character has the value 127 minus it, for 127 it is 0, and for NUL alone it is
    /// "exceptionally ASCII 127".
    /// </summary>
    [Fact]
    public void EncodesEveryAsciiCharacter()
    {
        for (int value = 0; value < 128; value++)
        {
            int check = value == 0 ? 127 : (127 - (value % 127)) % 127;
            string expected = Patterns['_'] + Patterns[value] + Patterns[check] + Patterns['z'];
            Assert.Equal(expected[..^1], string.Concat(Encode(((char)value).ToString()).RunWidths));
        }
    }

    /// <summary>
    /// The worked example of "Telepen - Barcode Symbology Information and History": the values of
    /// Telepen add to 717, which leaves 82 after division by 127, and 127 minus 82 is 45, the minus sign.
    /// A sum that divides exactly gives 0, and NUL characters alone give 127.
    /// </summary>
    /// <param name="values">The character values.</param>
    /// <param name="expected">The value of the check character.</param>
    [Theory]
    [InlineData(new[] { 84, 101, 108, 101, 112, 101, 110 }, 45)]
    [InlineData(new[] { 127 }, 0)]
    [InlineData(new[] { 0, 0 }, 127)]
    public void CalculatesTheCheckCharacterOfTheDocument(int[] values, int expected)
        => Assert.Equal(expected, TelepenEncoder.CheckCharacter(values));

    /// <summary>
    /// Numeric mode packs a pair of digits into the character 27 plus the pair as a number, and a digit
    /// before X into the character 17 plus the digit. The expected runs are the reference implementation's
    /// for the same input in numeric mode.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    /// <param name="reference">The reference run string.</param>
    [Theory]
    [InlineData("1234", "1111111111331111113111311113111113111111131331331111111111")]
    [InlineData("1X", "111111111133333331111313111111331111111111")]
    [InlineData("1X23", "11111111113333333133131311111113111311331111111111")]
    [InlineData("0123456789", "1111111111333111111131113313131131333313111113111131111311131133113111331111111111")]
    public void PacksDigitsInNumericMode(string text, string reference)
        => Assert.Equal(reference[..^1], string.Concat(EncodeNumeric(text).RunWidths));

    /// <summary>
    /// The printed line shows the text as given. It never shows the start code, the check character or
    /// the stop code.
    /// </summary>
    [Fact]
    public void PrintsTheTextAsGiven()
    {
        BarcodeOptions options = new() { Font = BarcodeFonts.OcrB.CreateFont(12F) };

        LinearBarcodeSymbol symbol = (LinearBarcodeSymbol)new TelepenSymbology().Encode("ABC123", options);
        Assert.Equal("ABC123", Assert.Single(symbol.Text).Text);

        LinearBarcodeSymbol numeric = (LinearBarcodeSymbol)new TelepenNumericSymbology().Encode("1X23", options);
        Assert.Equal("1X23", Assert.Single(numeric.Text).Text);
    }

    /// <summary>
    /// Every character is sixteen modules, the start code, the check character and the stop code are
    /// three more, and the runs end on the last bar of the stop code, one module before its end.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    [Theory]
    [InlineData("ABC123")]
    [InlineData("A")]
    public void SpansSixteenModulesPerCharacter(string text)
    {
        int widthInModules = 0;
        foreach (int run in Encode(text).RunWidths)
        {
            widthInModules += run;
        }

        Assert.Equal((16 * (text.Length + 3)) - 1, widthInModules);
    }

    [Theory]
    [InlineData("")]
    [InlineData("é")]
    [InlineData("１")]
    public void RejectsMalformedInput(string text)
        => Assert.ThrowsAny<ArgumentException>(() => Encode(text));

    [Theory]
    [InlineData("")]
    [InlineData("123")]
    [InlineData("X1")]
    [InlineData("12X4")]
    [InlineData("12A4")]
    [InlineData("１２")]
    public void RejectsMalformedNumericInput(string text)
        => Assert.ThrowsAny<ArgumentException>(() => EncodeNumeric(text));

    private static LinearBarcodeSymbol Encode(string text)
        => (LinearBarcodeSymbol)new TelepenSymbology().Encode(text, new BarcodeOptions());

    private static LinearBarcodeSymbol EncodeNumeric(string text)
        => (LinearBarcodeSymbol)new TelepenNumericSymbology().Encode(text, new BarcodeOptions());
}
