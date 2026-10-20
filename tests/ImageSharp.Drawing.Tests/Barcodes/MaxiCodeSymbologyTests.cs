// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Barcodes;

namespace SixLabors.ImageSharp.Drawing.Tests.Barcodes;

/// <summary>
/// Encoder tests for <see cref="MaxiCodeSymbology"/>. Each expected grid is the 33 x 30 module grid of an independent reference implementation, one row of
/// hexadecimal digits per line, four modules per digit and the last digit padded with light modules. The modules of the odd rows stand half a module to the
/// right.
/// </summary>
public class MaxiCodeSymbologyTests
{
    /// <summary>
    /// Gets the cases of <see cref="MatchesReferenceGrid"/>: the mode (0 for automatic), the text and the grid. The 93 character message is the message of
    /// Figure 2 of ISO/IEC 16023.
    /// </summary>
    public static TheoryData<int, string, string> MatchesReferenceGridData { get; } = new()
    {
        {
            0,
            "This is MaxiCode",
            "c230055c|" +
            "7153a008|" +
            "e8bf2aac|" +
            "55555558|" +
            "00000000|" +
            "aaaaaaa8|" +
            "55555558|" +
            "00000008|" +
            "aaaaaaa0|" +
            "557d2158|" +
            "007f8008|" +
            "aa60a6a8|" +
            "54602150|" +
            "00c00408|" +
            "aac00ea8|" +
            "55000d50|" +
            "02800800|" +
            "a8800aa0|" +
            "55401954|" +
            "01400000|" +
            "a9a012a8|" +
            "a3001618|" +
            "59eb724c|" +
            "b6a84a60|" +
            "b25790fc|" +
            "571a1bc8|" +
            "96d2e76c|" +
            "da929dc8|" +
            "f2d70dec|" +
            "fd24ffb0|" +
            "2a3be804|" +
            "efe8aac8|" +
            "b9ed2260"
        },
        {
            0,
            "THIS IS A 93 CHARACTER CODE SET A MESSAGE THAT FILLS A MODE 4, UNAPPENDED, MAXICODE SYMBOL...",
            "7d02089c|" +
            "10100a00|" +
            "2cc9b248|" +
            "81189000|" +
            "2c0a0acc|" +
            "e882c8f0|" +
            "80609000|" +
            "0a2489f0|" +
            "ee020600|" +
            "01b01460|" +
            "bc158c0c|" +
            "38a03a38|" +
            "1e002160|" +
            "88000720|" +
            "80801820|" +
            "0b800208|" +
            "e0800834|" +
            "60000890|" +
            "02c00944|" +
            "a8802730|" +
            "21801c28|" +
            "00003080|" +
            "aca84544|" +
            "8f2a6a28|" +
            "68015a7c|" +
            "073ffc50|" +
            "3a715870|" +
            "ddc86958|" +
            "05ba9730|" +
            "e3788f78|" +
            "bd7e16e4|" +
            "26fb6a70|" +
            "2c0f6420"
        },
        {
            4,
            "Mixed case, digits 123456789012345678 and symbols {}~@!",
            "8d00a62c|" +
            "23998fa8|" +
            "e32e3fdc|" +
            "40404af0|" +
            "ecfacebc|" +
            "c6d9ce08|" +
            "a00957d0|" +
            "1cf30280|" +
            "e9cf1ca8|" +
            "55fc2d50|" +
            "001e9c04|" +
            "aa0032a0|" +
            "55202d54|" +
            "03000000|" +
            "ab8016a4|" +
            "55000d58|" +
            "00800804|" +
            "a8000ea8|" +
            "57401558|" +
            "00000408|" +
            "aa6006a8|" +
            "55c01950|" +
            "00e2dc08|" +
            "aabe56a8|" +
            "55555aa8|" +
            "000009e8|" +
            "aaaaa8f8|" +
            "72b06870|" +
            "10405f94|" +
            "1b974b30|" +
            "27f7f424|" +
            "949bd9b8|" +
            "ac44e688"
        },
        {
            5,
            "Mode five",
            "1555555c|" +
            "80000000|" +
            "aaaaaaa8|" +
            "55555558|" +
            "00000008|" +
            "aaaaaaa0|" +
            "55555554|" +
            "00000008|" +
            "aaaaaaa0|" +
            "553d0d50|" +
            "003f9804|" +
            "aa2096a0|" +
            "57a02950|" +
            "00803800|" +
            "a9401eac|" +
            "55000950|" +
            "00800800|" +
            "a9000aa8|" +
            "56400158|" +
            "01801800|" +
            "aa6006ac|" +
            "07800418|" +
            "7b22eec8|" +
            "ad7c5ae8|" +
            "38abf450|" +
            "3eb21a20|" +
            "99f6cdf4|" +
            "cb3af340|" +
            "bc43e29c|" +
            "b3b6fc28|" +
            "fd5da610|" +
            "d265bf50|" +
            "be54c324"
        },
        {
            6,
            "READER PROGRAMMING",
            "0800155c|" +
            "e0f78000|" +
            "f6a9eaa4|" +
            "55555558|" +
            "00000004|" +
            "aaaaaaa0|" +
            "55555554|" +
            "00000000|" +
            "aaaaaaac|" +
            "55f20150|" +
            "00788c00|" +
            "aac046a8|" +
            "56800d54|" +
            "01000800|" +
            "a84012a4|" +
            "55000158|" +
            "02800808|" +
            "a8000ea0|" +
            "5480095c|" +
            "01401c08|" +
            "ab600aa8|" +
            "55405d50|" +
            "006a4804|" +
            "aab046a8|" +
            "55555ab0|" +
            "000004b8|" +
            "aaaaaec0|" +
            "dc692be0|" +
            "a38fd3c0|" +
            "d7a64c90|" +
            "321f9144|" +
            "d1292fc8|" +
            "568bb9e8"
        },
        {
            4,
            "Latin-1: café ÀÉÎÕÜ àéîõü ¡¿ «»",
            "d031f02c|" +
            "30b7f1e8|" +
            "fe6b026c|" +
            "75d80fd8|" +
            "9e4b4f40|" +
            "f6e98ae0|" +
            "75555550|" +
            "d0000000|" +
            "9aaaaaa4|" +
            "557c3158|" +
            "00ff880c|" +
            "aa000ea8|" +
            "57e00150|" +
            "02000c00|" +
            "a98012a8|" +
            "57000950|" +
            "00800804|" +
            "ab000ea0|" +
            "57000950|" +
            "01c01808|" +
            "aaa002a8|" +
            "55a02950|" +
            "0062540c|" +
            "aa6e6ea0|" +
            "55555e80|" +
            "00000290|" +
            "aaaaa714|" +
            "6c337a90|" +
            "5084d09c|" +
            "91a039b0|" +
            "4ead6854|" +
            "532740e8|" +
            "2154714c"
        },
        {
            2,
            "152382802\u001D840\u001D001\u001D1Z00004951\u001DUPSN\u001D06X610",
            "effffaac|" +
            "10098e00|" +
            "9002aa30|" +
            "555fef88|" +
            "000098fc|" +
            "aaa24490|" +
            "55555554|" +
            "00000000|" +
            "aaaaaaa0|" +
            "55341950|" +
            "003c0c00|" +
            "aa805ea8|" +
            "54001558|" +
            "02001808|" +
            "a90006ac|" +
            "55001d50|" +
            "00800800|" +
            "aa800aa8|" +
            "57c00d58|" +
            "02c00800|" +
            "aba01ea0|" +
            "55003550|" +
            "00e96400|" +
            "aa2a62a8|" +
            "5555541c|" +
            "00000788|" +
            "aaaaada0|" +
            "aabf9330|" +
            "ebe77310|" +
            "8b67c8d0|" +
            "eceabed0|" +
            "52f56380|" +
            "8f4ccdd4"
        },
        {
            2,
            "[)>\u001E01\u001D96152382802\u001D840\u001D001\u001D1Z00004951\u001DUPSN\u001D06X610\u001D159\u001D1234567\u001D1/1\u001D\u001DY\u001D634 ALPHA DR\u001DPITTSBURGH\u001DPA\u001E\u0004",
            "d76fbfbc|" +
            "5570d848|" +
            "db12a644|" +
            "e2abfff8|" +
            "3c2c980c|" +
            "272aa000|" +
            "efefeff8|" +
            "98327028|" +
            "462aa9c4|" +
            "de341bf0|" +
            "333c0ea4|" +
            "ba805f68|" +
            "a80016fc|" +
            "f6001b88|" +
            "a9000674|" +
            "21001c78|" +
            "4c800828|" +
            "02800a08|" +
            "13c00ea8|" +
            "0ac008c0|" +
            "83a01d80|" +
            "a2003680|" +
            "20e96694|" +
            "672a6020|" +
            "0a2c55e8|" +
            "9c3e0da8|" +
            "c894a034|" +
            "046d3af0|" +
            "5d290f68|" +
            "dceaadd0|" +
            "6f15174c|" +
            "1e987858|" +
            "128c07cc"
        },
        {
            3,
            "B1050\u001D056\u001D999\u001DA demonstration of mode 3",
            "1c00a88c|" +
            "0ebf2090|" +
            "8cadc620|" +
            "77004100|" +
            "03afeff8|" +
            "b38edf70|" +
            "5555555c|" +
            "00000000|" +
            "aaaaaaac|" +
            "55782550|" +
            "001c1400|" +
            "aa80d2a8|" +
            "57c02154|" +
            "00c01808|" +
            "aa401ea0|" +
            "55800150|" +
            "02800808|" +
            "ab000aa0|" +
            "55001d50|" +
            "02801c00|" +
            "abc00aa8|" +
            "55e01d58|" +
            "00e0780c|" +
            "aab646a8|" +
            "55555cb8|" +
            "00000f58|" +
            "aaaaa9a0|" +
            "6badf228|" +
            "05a067f0|" +
            "58142718|" +
            "dba000bc|" +
            "f275b320|" +
            "d9c20794"
        },
    };

    /// <summary>
    /// The symbol is 33 rows of 30 modules, and its module grid matches the reference grid.
    /// </summary>
    /// <param name="mode">The mode, or 0 for automatic.</param>
    /// <param name="text">The text to encode.</param>
    /// <param name="expected">The reference grid, one row of hexadecimal digits per "|" separated part.</param>
    [Theory]
    [MemberData(nameof(MatchesReferenceGridData))]
    public void MatchesReferenceGrid(int mode, string text, string expected)
    {
        MaxiCodeSymbology symbology = mode == 0 ? new MaxiCodeSymbology() : new MaxiCodeSymbology((MaxiCodeMode)mode);

        SymbolEncoder.Encode(symbology, text, new BarcodeOptions(), (ref symbol) =>
        {
            Assert.Equal((33, 30), (symbol.MatrixRows, symbol.Columns));
            Assert.Equal(expected, SymbolEncoder.ModuleGrid(ref symbol));
        });
    }

    /// <summary>
    /// Section 4.11.5 of ISO/IEC 16023: "1W on the left and right sides" and "1Y on the top and bottom", and section 4.11.6: the symbol with its quiet zones
    /// "is 32X wide by (34Y + V) high".
    /// </summary>
    [Fact]
    public void QuietZonesAndSizeFollowSection411()
        => SymbolEncoder.Encode(new MaxiCodeSymbology(), "This is MaxiCode", new BarcodeOptions(), static (ref symbol) =>
        {
            double y = Math.Sqrt(3) / 2;
            double v = 2 / Math.Sqrt(3);

            Assert.Equal(32F, symbol.WidthInModules + symbol.LeadingQuietZone + symbol.TrailingQuietZone);
            Assert.Equal((34 * y) + v, symbol.HeightInModules + symbol.TopQuietZone + symbol.BottomQuietZone, 4);
        });

    /// <summary>
    /// A structured carrier message needs its postal code, country code and class of service, each followed by a group separator. A mode 2 postal code is 1 to
    /// 9 digits, a mode 3 postal code 1 to 6 characters, and the country code and the class of service are three digits.
    /// </summary>
    /// <param name="mode">The mode.</param>
    /// <param name="text">The text.</param>
    [Theory]
    [InlineData(2, "152382802\u001D840\u001D001")]
    [InlineData(2, "1234567890\u001D840\u001D001\u001D")]
    [InlineData(2, "12A45\u001D840\u001D001\u001D")]
    [InlineData(3, "B10500X\u001D056\u001D999\u001D")]
    [InlineData(3, "b1050\u001D056\u001D999\u001D")]
    [InlineData(2, "152382802\u001D84\u001D001\u001D")]
    [InlineData(2, "152382802\u001D840\u001D01A\u001D")]
    public void RejectsMalformedStructuredCarrier(int mode, string text)
        => Assert.Throws<ArgumentException>(() => SymbolEncoder.Encode(new MaxiCodeSymbology((MaxiCodeMode)mode), text, new BarcodeOptions()));

    /// <summary>
    /// Without a mode the symbol takes mode 5 when the data fits and mode 4 otherwise, so it matches the symbol of that mode. The data ends in code set C,
    /// which has no pad codeword: 71 or 80 characters of code set A, the lock into code set C and four characters of code set C need 77 or 86 codewords. 77
    /// fill mode 5 with no latch back to code set A, and 86 take mode 4, whose pads follow a latch back to code set A.
    /// </summary>
    /// <param name="capitals">The number of capital letters before the four accented letters.</param>
    /// <param name="mode">The mode the data takes.</param>
    [Theory]
    [InlineData(71, MaxiCodeMode.FullErrorCorrection)]
    [InlineData(80, MaxiCodeMode.Standard)]
    public void AutomaticModeMatchesTheModeItTakes(int capitals, MaxiCodeMode mode)
    {
        string text = new string('A', capitals) + "ÀÀÀÀ";
        string expected = null;
        SymbolEncoder.Encode(new MaxiCodeSymbology(mode), text, new BarcodeOptions(), (ref symbol) => expected = SymbolEncoder.ModuleGrid(ref symbol));

        SymbolEncoder.Encode(new MaxiCodeSymbology(), text, new BarcodeOptions(), (ref symbol) => Assert.Equal(expected, SymbolEncoder.ModuleGrid(ref symbol)));
    }

    /// <summary>
    /// A mode 5 symbol holds 77 codewords of data. Data that needs more codewords throws.
    /// </summary>
    [Fact]
    public void RejectsDataBeyondModeCapacity()
        => Assert.Throws<ArgumentException>(
            () => SymbolEncoder.Encode(new MaxiCodeSymbology(MaxiCodeMode.FullErrorCorrection), new string('A', 78), new BarcodeOptions()));

    /// <summary>
    /// MaxiCode carries the 256 byte values, U+0000 to U+00FF. A character above U+00FF throws.
    /// </summary>
    [Fact]
    public void RejectsCharactersAboveLatin1()
        => Assert.Throws<ArgumentException>(() => SymbolEncoder.Encode(new MaxiCodeSymbology(), "Ā", new BarcodeOptions()));

    /// <summary>
    /// Table 7 gives W = X = L/29 = 0.88 mm at L = 25.50 mm, and section 4.11.1: "The acceptable range of L is 24.00 mm to 27.00 mm." The hexagonal modules
    /// draw at the exact X dimension, so an X dimension inside that range draws as given and one outside it draws at the nearest limit.
    /// </summary>
    /// <param name="xDimension">The X dimension of the caller in millimetres, or <see langword="null"/> for the default.</param>
    /// <param name="expected">The X dimension the symbol draws at in millimetres.</param>
    [Theory]
    [InlineData(null, 0.88)]
    [InlineData(0.9, 0.9)]
    [InlineData(0.5, 24.00 / 29)]
    [InlineData(2.0, 27.00 / 29)]
    public void ClampsTheXDimensionToTheRangeOfSectionFourElevenOne(double? xDimension, double expected)
        => SymbolEncoder.Encode(
            new MaxiCodeSymbology(),
            "This is MaxiCode",
            new BarcodeOptions { XDimension = xDimension },
            (ref symbol) => Assert.Equal(expected, symbol.XDimension, 1e-12));
}
