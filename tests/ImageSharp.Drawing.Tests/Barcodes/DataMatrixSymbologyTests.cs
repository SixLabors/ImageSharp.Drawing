// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Text;
using SixLabors.ImageSharp.Drawing.Barcodes;
using SixLabors.ImageSharp.Drawing.Helpers;
using SixLabors.ImageSharp.Drawing.Tests.TestUtilities;

namespace SixLabors.ImageSharp.Drawing.Tests.Barcodes;

/// <summary>
/// Encoder tests for <see cref="DataMatrixSymbology"/>, <see cref="DataMatrixRectangularSymbology"/> and
/// <see cref="DataMatrixRectangularExtensionSymbology"/>. Each expected grid is the module grid of an
/// independent reference implementation, taken through its raw encoding API, so the encodation schemes,
/// the symbol size, the pad codewords, the error correction and the module placement all show up here.
/// A grid row is the modules from the left as hexadecimal digits, the leftmost module the most significant
/// bit, padded with light modules to whole digits.
/// </summary>
public class DataMatrixSymbologyTests
{
    /// <summary>
    /// The 45 characters of the Royal Mail fields of the example in Table 5 of the Mailmark barcode
    /// definition document.
    /// </summary>
    private const string Mailmark2DFields = "JGB 012100123412345678AB19XY1A 0             ";

    /// <summary>
    /// ISO/IEC 16022 Table 2: an ASCII character is its value plus 1, and a pair of digits is 130 plus the
    /// value of the pair.
    /// </summary>
    [Fact]
    public void DataMatrix_AsciiEncodationMatchesTableTwo()
    {
        Assert.Equal(['A' + 1], Codewords("A", [3]));
        Assert.Equal([130 + 12, 130 + 34], Codewords("1234", [3]));
    }

    /// <summary>
    /// ISO/IEC 16022 Table 2: a character above 127 takes the Upper Shift, 235, then its value less 128,
    /// plus 1.
    /// </summary>
    [Fact]
    public void DataMatrix_ExtendedAsciiTakesTheUpperShift()
        => Assert.Equal([235, 0xE9 - 128 + 1], Codewords("é", [3]));

    /// <summary>
    /// Section 5.6.2 of the GS1 General Specifications: the Function 1 Symbol Character is encoded at the
    /// beginning of the data, and ISO/IEC 16022 Table 2 gives it the codeword 232.
    /// </summary>
    [Fact]
    public void Gs1DataMatrix_StartsWithFnc1()
        => Assert.Equal([232, 130 + 1], Codewords([-1, '0', '1'], [3], 0));

    /// <summary>
    /// Section 5.6.2 of the GS1 General Specifications: the finder pattern is a solid L one module wide on
    /// the left and bottom edges, and the opposite edges alternate, with the upper right corner module
    /// light. Section 5.6.3.2: a symbol of 32 x 32 or more is divided into data regions by alignment
    /// patterns.
    /// </summary>
    [Theory]
    [InlineData("1234", 10, 10)]
    [InlineData("01234567890123456789012345678901234567890123456789", 22, 22)]
    [InlineData("0123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789", 32, 32)]
    public void DataMatrix_FinderPatternFollowsSectionFiveSixTwo(string text, int rows, int columns)
        => SymbolEncoder.Encode(new DataMatrixSymbology(), text, new BarcodeOptions(), (ref symbol) =>
        {
            Assert.Equal(rows, symbol.MatrixRows);
            Assert.Equal(columns, symbol.Columns);

            ReadOnlySpan<bool> modules = symbol.Modules;
            for (int r = 0; r < rows; r++)
            {
                Assert.True(modules[r * columns], $"Left edge row {r}");
                Assert.Equal(r % 2 == 1, modules[(r * columns) + columns - 1]);
            }

            for (int c = 0; c < columns; c++)
            {
                Assert.True(modules[((rows - 1) * columns) + c], $"Bottom edge column {c}");
                Assert.Equal(c % 2 == 0, modules[c]);
            }

            Assert.False(modules[columns - 1]);
        });

    /// <summary>
    /// Tables 5-32 and 5-33 of the GS1 General Specifications: the data codeword capacity of each size.
    /// Data of exactly that many codewords takes that size, and one codeword more takes the next size.
    /// </summary>
    [Theory]
    [InlineData(3, 10, 10, 12, 12)]
    [InlineData(5, 12, 12, 14, 14)]
    [InlineData(44, 26, 26, 32, 32)]
    [InlineData(204, 52, 52, 64, 64)]
    public void DataMatrix_SquareCapacityMatchesTableFiveThirtyTwo(int capacity, int rows, int columns, int nextRows, int nextColumns)
    {
        Assert.Equal((rows, columns), Size(new DataMatrixSymbology(), Digits(2 * capacity)));
        Assert.Equal((nextRows, nextColumns), Size(new DataMatrixSymbology(), Digits(2 * (capacity + 1))));
    }

    /// <summary>
    /// Table 5-33 of the GS1 General Specifications: the rectangular sizes hold 5, 10, 16, 22, 32 and 49
    /// data codewords.
    /// </summary>
    [Theory]
    [InlineData(5, 8, 18)]
    [InlineData(10, 8, 32)]
    [InlineData(16, 12, 26)]
    [InlineData(22, 12, 36)]
    [InlineData(32, 16, 36)]
    [InlineData(49, 16, 48)]
    public void DataMatrixRectangular_CapacityMatchesTableFiveThirtyThree(int capacity, int rows, int columns)
        => Assert.Equal((rows, columns), Size(new DataMatrixRectangularSymbology(), Digits(2 * capacity)));

    /// <summary>
    /// Section 5.6.2 of the GS1 General Specifications: the largest symbol carries 3116 digits.
    /// </summary>
    [Fact]
    public void DataMatrix_RejectsDataLargerThanTheLargestSymbol()
        => Assert.ThrowsAny<ArgumentException>(() => Encode(new DataMatrixSymbology(), Digits(3117)));

    [Fact]
    public void DataMatrix_RejectsDataLargerThanTheFixedSize()
        => Assert.Throws<ArgumentException>(() => Encode(new DataMatrixSymbology(10, 10), Digits(8)));

    [Fact]
    public void DataMatrix_RejectsCharactersAboveTheByteRange()
        => Assert.Throws<ArgumentException>(() => Encode(new DataMatrixSymbology(), "DMĀ"));

    [Fact]
    public void DataMatrix_RejectsEmptyData()
        => Assert.ThrowsAny<ArgumentException>(() => Encode(new DataMatrixSymbology(), string.Empty));

    [Fact]
    public void DataMatrix_RejectsSizesOutsideTheTables()
    {
        Assert.Throws<ArgumentException>(() => new DataMatrixSymbology(11, 11));
        Assert.Throws<ArgumentException>(() => new DataMatrixSymbology(8, 48));
        Assert.Throws<ArgumentException>(() => new DataMatrixRectangularExtensionSymbology(10, 10));
    }

    /// <summary>
    /// Section 5.6.2 of the GS1 General Specifications: the quiet zone is one module wide on all four
    /// sides.
    /// </summary>
    [Fact]
    public void DataMatrix_QuietZoneIsOneModule()
        => SymbolEncoder.Encode(new DataMatrixSymbology(), "1234", new BarcodeOptions(), static (ref symbol) =>
        {
            Assert.Equal(1, symbol.LeadingQuietZone);
            Assert.Equal(1, symbol.TrailingQuietZone);
            Assert.Equal(1, symbol.TopQuietZone);
            Assert.Equal(1, symbol.BottomQuietZone);
        });

    /// <summary>
    /// Gets the cases of <see cref="DataMatrix_MatchesReferenceGrid"/>.
    /// </summary>
    public static TheoryData<string, string, string, int, int, string> DataMatrix_MatchesReferenceGridData { get; } = new()
    {
        {
            "This is Data Matrix!",
            "square",
            string.Empty,
            18,
            18,
            "aaaa8|98cec|9b2f8|f079c|e85f0|cbabc|b6c98|9ce64|8bd18|81364|dca48|ffa44|c0d40|c7714|a2ca8|94d84|ba680|ffffc"
        },
        { "1234", "square", string.Empty, 10, 10, "aa8|c7c|c88|c14|da0|95c|8a0|f4c|818|ffc" },
        { "123456", "square", string.Empty, 10, 10, "aa8|cb4|c10|c74|c20|83c|ec0|f64|9d0|ffc" },
        { "A", "square", string.Empty, 10, 10, "aa8|d8c|8d0|9ac|940|92c|d30|cf4|c20|ffc" },
        {
            "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789",
            "square",
            string.Empty,
            22,
            22,
            "aaaaa8|" +
                "a6949c|" +
                "b2ad90|" +
                "9909cc|" +
                "a9e170|" +
                "8680a4|" +
                "c2f5e0|" +
                "f5d5dc|" +
                "f26930|" +
                "d9f9dc|" +
                "bef9b0|" +
                "aee314|" +
                "ad72a8|" +
                "cc251c|" +
                "b0a1f0|" +
                "814834|" +
                "9f8188|" +
                "ebe78c|" +
                "adb038|" +
                "85dbbc|" +
                "d75258|" +
                "fffffc"
        },
        {
            "abcdefghijklmnopqrstuvwxyz",
            "square",
            string.Empty,
            20,
            20,
            "aaaaa|a694f|b2adc|99087|e9e14|c6821|c2f4a|f5ea5|f1d42|db925|b51fa|afca3|a6d2c|c3637|cf6be|d914b|d741e|f07f5|d6152|fffff"
        },
        {
            "ABC*DEF>GHI\r123*XYZ>",
            "square",
            string.Empty,
            18,
            18,
            "aaaa8|a499c|bc518|9e7fc|d66f0|b103c|bf0b0|8078c|b19b8|c261c|e1d78|913b4|bfb40|9421c|b7438|bfb84|947d8|ffffc"
        },
        {
            "ABC.DEF/GHI-JKL@MNO^PQR",
            "square",
            string.Empty,
            20,
            20,
            "aaaaa|82927|830de|c5061|919d2|90c4b|8d0aa|a84cd|f6892|d5d99|8b9e2|a1c35|f3810|9ef7f|f89f6|dc13b|c6924|9c0dd|cfeea|fffff"
        },
        { "éèêëàâäöüß", "square", string.Empty, 16, 16, "aaaa|8baf|dfe2|c5ab|9d34|eae7|ede4|fa4f|8716|990f|c0a4|f0d7|fae8|b535|bd0a|ffff" },
        {
            "Hello, World! 0123456789 ¢",
            "square",
            string.Empty,
            20,
            20,
            "aaaaa|f0043|d9498|faa71|cf230|f76ed|a7fbc|de029|fba88|c9f39|e180c|b92f1|f3bf8|a87f7|9f73c|97d79|edaac|ac805|e9ca2|fffff"
        },
        {
            "a;b<c>d@e[f\\g]h_i`j~k!l",
            "square",
            string.Empty,
            22,
            22,
            "aaaaa8|" +
                "8fba8c|" +
                "f6d488|" +
                "c889b4|" +
                "88d078|" +
                "bb69a4|" +
                "fc55c8|" +
                "8f954c|" +
                "ad29f0|" +
                "b2f96c|" +
                "c5fd10|" +
                "eee90c|" +
                "9f5e38|" +
                "ee3d1c|" +
                "a2fe90|" +
                "817a4c|" +
                "dc6ce0|" +
                "cda724|" +
                "860000|" +
                "92caec|" +
                "bb0ad0|" +
                "fffffc"
        },
        {
            "MiXeD cAsE 42 MiXeD",
            "square",
            string.Empty,
            18,
            18,
            "aaaa8|a61ac|d7450|a116c|cb520|a127c|cc750|8919c|83a48|b968c|e20d8|cb4ec|908c0|852cc|c43f0|8a69c|ad450|ffffc"
        },
        { "[)>\u001e05\u001d0123456789\u001e\u0004", "square", string.Empty, 14, 14, "aaa8|c6bc|8fc0|b1e4|fb90|9fa4|9890|8ffc|a7f0|bfb4|e028|8934|90e0|fffc" },
        { "[)>\u001e06\u001dABC123\u001e\u0004", "square", string.Empty, 14, 14, "aaa8|ac5c|8a10|a354|cd30|f354|a270|969c|a390|fbf4|f598|e284|9610|fffc" },
        {
            "The quick brown fox jumps over the lazy dog 0123456789 THE QUICK BROWN FOX JUMPS OVER THE LAZY DOG",
            "square",
            string.Empty,
            36,
            36,
            "aaaaaaaaa|" +
                "9b30fb7a1|" +
                "909a3e20c|" +
                "f5447ac97|" +
                "ff4b2f9a4|" +
                "da6d6136f|" +
                "809ebbad4|" +
                "9de27b1ed|" +
                "a14738c54|" +
                "d86a74b23|" +
                "9a533cd9c|" +
                "8d6468955|" +
                "a8ff3d5d2|" +
                "8c3cff0a7|" +
                "db723c812|" +
                "86dd76515|" +
                "c82527990|" +
                "fffffffff|" +
                "aaaaaaaaa|" +
                "d150e9213|" +
                "8fe831d00|" +
                "e6c1ecd1b|" +
                "c0523678a|" +
                "85116fde3|" +
                "8dd636e8e|" +
                "c9b6fe1f5|" +
                "c10cb9eae|" +
                "f79e7f32d|" +
                "83952870c|" +
                "e492734fd|" +
                "efbd3bb48|" +
                "ae5973d69|" +
                "ebd2b87f8|" +
                "9214fd121|" +
                "99bb30aba|" +
                "fffffffff"
        },
        {
            "01234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789" +
                "012345678901234567890123456789012345678901234567890123456789",
            "square",
            string.Empty,
            40,
            40,
            "aaaaaaaaaa|" +
                "dc55df1b1f|" +
                "bed9afa260|" +
                "97711dd5d5|" +
                "a6e629d8de|" +
                "c45ed97b77|" +
                "988b8efee6|" +
                "fbbb7c4445|" +
                "afe228e898|" +
                "ec56fb7b7b|" +
                "8edfae6e26|" +
                "d771bc6edd|" +
                "e6fa298ff8|" +
                "c45d5f5155|" +
                "989d8b7b36|" +
                "fb17bddf7d|" +
                "ae6feb99c4|" +
                "edc4591e9f|" +
                "888e8a3382|" +
                "ffffffffff|" +
                "aaaaaaaaaa|" +
                "dbb7bd32fd|" +
                "fee6ca4f0c|" +
                "c6c6fa6a47|" +
                "e898cf21e8|" +
                "f5751b82a5|" +
                "f6352bad44|" +
                "dedd9cbc17|" +
                "bfbc2d307e|" +
                "911b5f65bd|" +
                "ba2d2b4140|" +
                "dec15941bd|" +
                "9b316c1af2|" +
                "9b6f585c2d|" +
                "e2bbc969b0|" +
                "d77cdaefbb|" +
                "d5c208b426|" +
                "f309fb52e9|" +
                "c5782ddeb8|" +
                "ffffffffff"
        },
        {
            "01234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789" +
                "01234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789" +
                "01234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789" +
                "01234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789" +
                "01234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789",
            "square",
            string.Empty,
            72,
            72,
            "aaaaaaaaaaaaaaaaaa|" +
                "dc55f71b1ec5c75771|" +
                "bed9afa2689beb66fa|" +
                "977165d5dd7177c45d|" +
                "a6e629d8ce226b9898|" +
                "c45ef17b7eec477b1b|" +
                "988ba6feefb98a2e62|" +
                "fbbb7c4459b7beedd5|" +
                "afe228e8ea22fb88dc|" +
                "ec56fb7b7d6ec75b77|" +
                "8edfae6e6df8eb7ee6|" +
                "d771ec6c5f1577c445|" +
                "e6fa2989afb66be89c|" +
                "c45d77571ddc477b77|" +
                "989da36229b98a6e3a|" +
                "fb17edeed917b66e8d|" +
                "ae6fbbfb8e22e38fb4|" +
                "ffffffffffffffffff|" +
                "aaaaaaaaaaaaaaaaaa|" +
                "edc4711b7eeedf5153|" +
                "888ea3a22bf88b7a34|" +
                "dbb7edd6fb15b7dfa5|" +
                "fee6b9df8bb7eb9d44|" +
                "c6c6f1715ddc470ecf|" +
                "e898a6fb69be8a6cf6|" +
                "f5757c5dd917b7e0b5|" +
                "f636289b8e26e28be0|" +
                "dedefb117ec6ee18fd|" +
                "bfbfae622b98fb4bc0|" +
                "9111edeefb7516fb95|" +
                "ba3a28bf8a37aa6440|" +
                "dedd7bb15eddf65c93|" +
                "9b9dbe3b6fb9fa2c86|" +
                "9b17655dd910ce61f5|" +
                "e26fad9bea27131950|" +
                "ffffffffffffffffff|" +
                "aaaaaaaaaaaaaaaaaa|" +
                "d5c5f7117edf9ef0f7|" +
                "d889ae626b896bf616|" +
                "fbb165ec7bb74ea98d|" +
                "fee628b98be2ebac0a|" +
                "c6defbb75c66de48d9|" +
                "e88bbe236eb012b26e|" +
                "f5bb656ddfb4df97e1|" +
                "f7e3adfb8e2d4b898c|" +
                "dc55f7111d722ebba1|" +
                "bed9afa26d5dc3ba18|" +
                "977165eddf859f169d|" +
                "a6e629b8adb7bb6d78|" +
                "c45ef1bb1f3de7bacb|" +
                "988ba63e0ca59b51fa|" +
                "fbbb7d46fc92472b3d|" +
                "afe22deb2f4282c4d8|" +
                "ffffffffffffffffff|" +
                "aaaaaaaaaaaaaaaaaa|" +
                "ec56f77bdaa147fb83|" +
                "8edfae6108eb527052|" +
                "d7716454bbac6ffc05|" +
                "e6fa29d968c44b9854|" +
                "c45ef7789e39e698d7|" +
                "989ba2ea0d02825492|" +
                "fb1bec7a584a77248d|" +
                "ae63b881ea962253ac|" +
                "edd47b5edbf2666489|" +
                "88deacfa4a8562a538|" +
                "db77efc2bc79c7f505|" +
                "fee6204ca9e36bcd84|" +
                "c44575167f0077af47|" +
                "e89dad616d52c25dea|" +
                "f37ffc873fb92f64c5|" +
                "f723b5dbaae9720cdc|" +
                "ffffffffffffffffff"
        },
        {
            "01234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789" +
                "01234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789" +
                "01234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789" +
                "01234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789" +
                "01234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789" +
                "01234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789" +
                "01234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789" +
                "01234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789" +
                "01234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789" +
                "01234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789" +
                "01234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789" +
                "01234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789" +
                "01234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789" +
                "01234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789" +
                "01234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789" +
                "01234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789" +
                "01234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789" +
                "01234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789" +
                "01234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789" +
                "01234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789" +
                "01234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789" +
                "01234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789" +
                "012345678901234567890123456789012345",
            "square",
            string.Empty,
            144,
            144,
            "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa|" +
                "dc55dd9b1b17c55dc7b1b171d5dc6d9b1713|" +
                "bed9bea2626eed9be8a626fad9be88e26fb6|" +
                "977117d5d5c5f71175dd5c5df11757d5c5df|" +
                "a6e626d8d888ee62768d889ae62762d889be|" +
                "c45ec5fb7bb1c5ec5fb7bb11dec5edfbb111|" +
                "988b9afefee688b9beefee628b9bfafee63a|" +
                "fbbb71c446dfbbb711c46defbb7111c6dedd|" +
                "afe222e8e88afe223a8e88bee223a2e88b98|" +
                "ec56edfb75bbc56edfb75bb1d6ededf5bb13|" +
                "8edfb8ee77e2edfb9ae77e3adfb9b8f7e276|" +
                "d771b1ec5c55f71b1bc5c55df1b1b1dc55dd|" +
                "e6fa2689bed8efa2629bed9afa2626bed8bc|" +
                "c45d5dd71771c5d5d5f17711dd5d5d977b11|" +
                "989d8ce226e689d8d8a26e629d8d88a6fe3a|" +
                "fb17b7eec45fb17b7bec45ed97b7bbc446dd|" +
                "ae6feefb988ae6fefeb988b8efefee98eb9c|" +
                "edc4459b7bbbdc4447b7bbb7c4446dfb7b1f|" +
                "888e8ea22fe288e8e8a2fe228e8e88ae626e|" +
                "dbb7b7d6ec57bb7b75eec56fb7b75bec55ed|" +
                "fee6e6df8edeee6e76f8edfae6e77e89d888|" +
                "c6c6c5f15771ec6c5d95771bc6c5c5d77b51|" +
                "e8989afb66fa8989beb66fa2989bece2ff66|" +
                "ffffffffffffffffffffffffffffffffffff|" +
                "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa|" +
                "f57571ddc45dd75717dc45d5f57177ec45dd|" +
                "f636229b989ce36226b989d8b6226ef8eb8c|" +
                "dedeed917b17edeec597b17bdeec459b71bf|" +
                "bfbfb8e22e6efbfb98a2e6febfb98eae63e2|" +
                "9111b7eeedc5911b7beedc4591b7b7ec6c55|" +
                "ba3a22bf888ea3a22ef888e8ba22e689bedc|" +
                "dedd6fb15bb7edd6ed95bb7bdd6ec5d7b777|" +
                "9b9df8bb7ee6b9df8eb7ee6e9df89ce226e6|" +
                "9b1715ddc6c7b17157dc6c6d971577ed4445|" +
                "e26fb69be898a6fb66be8988efb62efd9898|" +
                "d5c5dd917575dc5dc5975757c5dec5977b7b|" +
                "d889b8e27636889b98a7636289bf8eae2e36|" +
                "fbb117ec5edfbb117bc5edefb111b7c6eed9|" +
                "fee622b9bfbeee622e9bfbfae63ae68f8fd8|" +
                "c6deefb71111edeeedf1111bdedec7b15197|" +
                "e88bf8a23a3a88bf88a3a3a28b989afb7af8|" +
                "f5bb15eededddbb15bededd7bb157bddd9f5|" +
                "f7e3b6fb9b9cfe3b7eb9b9dee276229b99c2|" +
                "dc55dd9b1b17c55dc7b1b171d5ded5911541|" +
                "bed9bea2626eed9be8a626fad8bfd8e2720a|" +
                "977117d5d5c5f71175dd5c5dfb1177edb705|" +
                "a6e626d8d888ee62768d889afe3ae2b8a2d4|" +
                "ffffffffffffffffffffffffffffffffffff|" +
                "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa|" +
                "c45ec5fb7bb1c5ec5fb7bb11c6dc6fbb10a1|" +
                "988b9afefee688b9beefee62eb98f8be6872|" +
                "fbbb71c446dfbbb711c46dedfb1b15c698fd|" +
                "afe222e8e88afe223a8e88b8e26fb6ebe278|" +
                "ec56edfb75bbc56edfb75bb1d5eddde7ca33|" +
                "8edfb8ee77e2edfb9ae77e26d889b8e7007a|" +
                "d771b1ec5c55f71b1bc5c55dfb5111d52acd|" +
                "e6fa2689bed8efa2629bed8aff6626c4017c|" +
                "c45d5dd71771c5d5d5f177b1c5deddd1b447|" +
                "989d8ce226e689d8d8a26fe2eb8b88c542da|" +
                "fb17b7eec45fb17b7bec446df1bbab96ab35|" +
                "ae6feefb988ae6fefeb98eb8e3e3e4971d84|" +
                "edc4459b7bbbdc4447b7b7b1ec543db3e71f|" +
                "888e8ea22fe288e8e8a2e626bede8ab0a726|" +
                "dbb7b7d6ec57bb7b75eec55fb77613d9ce61|" +
                "fee6e6df8edeee6e76f89d88a6e6c699e24c|" +
                "c6c6c5f15771ec6c5d9577b5c44003c53991|" +
                "e8989afb66fa8989beb62ff6989ad897ee10|" +
                "f57571ddc45dd75717dec45dfb76bbc1132d|" +
                "f636229b989ce36226bf8eb8ae3c16d3fdd4|" +
                "dedeed917b17edeec591b71bee8169d49a0f|" +
                "bfbfb8e22e6efbfb98bae63e8ff0fa82009e|" +
                "ffffffffffffffffffffffffffffffffffff|" +
                "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa|" +
                "9111b7eeedc5911b7bdec6c5d0c3bbb04041|" +
                "ba3a22bf888ea3a22e989becfa2b42ecb004|" +
                "dedd6fb15bb7edd6ed957b77d84e1d994027|" +
                "9b9df8bb7ee6b9df88f6226e9b1a9ac9f18a|" +
                "9b1715ddc6c7b17157ded445801055e2282d|" +
                "e26fb69be898a6fb62bfd988e9696efebd20|" +
                "d5c5dd917575dc5ded9177b7b958599924db|" +
                "d889b8e27636889bf8bae2e2f3d17eb6ea16|" +
                "fbb117ec5edfbb111bdc6ee9b8a9e7f49061|" +
                "fee622b9bfbeee63ae98f8fe83bfe68d7928|" +
                "c6deefb71111ededed9b1509bc3a35e93705|" +
                "e88bf8a23a3a88b988efb7b6f23370855f48|" +
                "f5bb15eededddbb157edddc9c15605e7a0ad|" +
                "f7e3b6fb9b9cfe276289b99cd56feaec958a|" +
                "dc55dd9b1b17c55dedd1112fd0d63fa891b9|" +
                "bed9bea2626eed8bfce6273ca133eaf1e95e|" +
                "977117d5d5c5f7b117ded8e5ef6f93ef31b5|" +
                "a6e626d8d888efe3ae8b8b84e1c76ea454bc|" +
                "c45ec5fb7bb1c46dc7bbac4bb5974dc4041f|" +
                "988b9afefee68eb98ee3fc78cea0d2fe998a|" +
                "fbbb71c446dfb7b1b1d433a195c6f5dd34e5|" +
                "afe222e8e88ae626fadedf22b5df74c05810|" +
                "ffffffffffffffffffffffffffffffffffff|" +
                "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa|" +
                "ec56edfb75bbc55eddf724c5e0457ba2e2c3|" +
                "8edfb8ee77e29d889ae674d6afb2c8d9e43a|" +
                "d771b1ec5c55f7b511c4be4d99e68981e22d|" +
                "e6fa2689bed8aff6629c7710df8b1ed5767c|" +
                "c45d5dd7177bc45dedfb31e9d494f3c4dde1|" +
                "989d8ce226fe8eb8b8b8a91cbce242cedac6|" +
                "fb17b7eec447b71bbbbbe01384cf4de05f6d|" +
                "ae6feefb98eae63e3ec20594a4eafcce94e4|" +
                "edc4459b7b7bc6c5458143bbea1443dbe37f|" +
                "888e8ea22e629bedecc1c37ead2084f83db6|" +
                "dbb7b7d6ec55fb7777c38741f94e2fbb17ad|" +
                "fee6e6df89d8a26e6c80b2de88dd9ec0f370|" +
                "c6c6c5f1577bd444459d11bfdaa01f887d27|" +
                "e8989afb62fed989809ee538ab55569876de|" +
                "f57571ddec45f7b7b1c725c9dc3c49b6226d|" +
                "f636229bf8eae2e2c0aef64e87016aade24c|" +
                "dedeed911b71eeeec78c424db28e7b894fef|" +
                "bfbfb8e3ae62f8f8009c5c4ea91c4af745fe|" +
                "9111b7edec6d95151d8f2eb9cc47f782e701|" +
                "ba3a22b989beb7b418aef87a988056ae8c04|" +
                "dedd6fb157b7dddc01820d559d2c59cc4757|" +
                "9b9df8a76226b9b03acf78acfb60f6c48f9a|" +
                "ffffffffffffffffffffffffffffffffffff|" +
                "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa|" +
                "9b1715dded45911011faf6ab8a3f87c4d81d|" +
                "e26fb68bfd98a6002ea4d234cd5ae6937aa0|" +
                "d5c5dfb1177bded573f2cd43f0a1e3c9a77b|" +
                "d889bee3ae2e8b2eaeafa0b09f225089384a|" +
                "fbb111edc6efbbc0338ac22bc5e117bf8d69|" +
                "fee63ab98f8ee06b14975192b33cfc9fa64c|" +
                "c6dedfb1b151d51e29a8ea0599f1ed985445|" +
                "e88b98a6fb7add6bfefd0508837aea8439ea|" +
                "f5bb15deddddf8306db5de53b86ad3a54ba5|" +
                "f7e276889b9ad49eec92b2b687dc40ee667a|" +
                "dc55dfb51111f9678f9bc0a3e6247b806131|" +
                "bed8bef66262a09a9cc862f48ba144ca8622|" +
                "977b11ddededf54fedb7eb07f62e639da611|" +
                "a6fe3ab8b8bc99585a8446ecb829f0b14f18|" +
                "c446dd9bbbb3820aad8a1f79f77bad94f031|" +
                "98eb98be3e0ca8917ccc8364f74294f8719a|" +
                "fb7b1bc54555fb2207aea8f5b546dfe117bd|" +
                "ae626eededd2ea7f7e988eb2a4cd9497ac38|" +
                "ec55edf7779dc4f045f34811b7adab8e84d3|" +
                "89d888ee6d4cfab2f2c7725aff4792b9f0c2|" +
                "d77bd1c467fb92383586065599b903e4e8d1|" +
                "d37e36ddba38f763f6cbbb94fc3572b8b9dc|" +
                "ffffffffffffffffffffffffffffffffffff"
        },
        { "1234", "rectangle", string.Empty, 8, 18, "aaaa8|c5f64|c6550|ccdb4|e75e0|940dc|f8e78|ffffc" },
        {
            "This is Data Matrix!",
            "rectangle",
            string.Empty,
            12,
            36,
            "aaaaaaaaa|98b97ed9d|9b6faf13e|f1c4fd487|e88637fb4|c82c7c0c7|b5f821056|879961269|85fe21052|d69aeb6c5|e89939826|fffffffff"
        },
        {
            "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789",
            "rectangle",
            string.Empty,
            16,
            36,
            "aaaaaaaaa|" +
                "a694fea69|" +
                "b2adbfb58|" +
                "9908f8587|" +
                "a9e135edc|" +
                "868467867|" +
                "c2f0a4a00|" +
                "f5c164371|" +
                "f27eb613a|" +
                "d9e8ffb43|" +
                "bea7261b2|" +
                "af06ec695|" +
                "adfeb46b2|" +
                "ce9e7b023|" +
                "f549328b8|" +
                "fffffffff"
        },
        { "1234", "dmre", string.Empty, 8, 18, "aaaa8|c5f64|c6550|ccdb4|e75e0|940dc|f8e78|ffffc" },
        {
            "This is Data Matrix rectangular extension",
            "dmre",
            string.Empty,
            8,
            80,
            "aaaaaaaaaaaaaaaaaaaa|" +
                "98831f78cdd73fd9b081|" +
                "9b524d07fef88b4b0288|" +
                "f073ff346fee6df9a007|" +
                "e86c89b556d67bcfefd4|" +
                "cd56f9b3cddea03d236f|" +
                "f6ba4d36d0de4c09f0ec|" +
                "ffffffffffffffffffff"
        },
        {
            "012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789",
            "dmre",
            string.Empty,
            16,
            64,
            "aaaaaaaaaaaaaaaa|" +
                "dc55f577c46fc6f7|" +
                "bed8f66ee8febff2|" +
                "9771dc45fb09a5a5|" +
                "a6e6b988efbe8b18|" +
                "c45d97b1edf1c273|" +
                "9888a2e689ead962|" +
                "fbbbeeddd0519bb9|" +
                "afeef88cc234ec8c|" +
                "ec6d95b7c38dbaf9|" +
                "8e8eb7eef184f100|" +
                "d757dc47a7fdcbad|" +
                "e766be86dface6ec|" +
                "c5c797b1ccc3d237|" +
                "ddd8b630de3aa614|" +
                "ffffffffffffffff"
        },
        {
            "1234",
            "dmre",
            "8x96",
            8,
            96,
            "aaaaaaaaaaaaaaaaaaaaaaaa|" +
                "c5f7b59e2e599b7ea1ade5e9|" +
                "c619a2aed4dabe187acb2492|" +
                "cc65f7be62a3d9d14bc4458d|" +
                "c7c628922eb6b1d72e96ac98|" +
                "8c2a379e91a3cb71c7f30917|" +
                "d8d4d4e787e8917768acce50|" +
                "ffffffffffffffffffffffff"
        },
        {
            "1234",
            "square",
            "24x24",
            24,
            24,
            "aaaaaa|" +
                "c5812f|" +
                "c65fbc|" +
                "cd2bad|" +
                "c6aa20|" +
                "8ec0cd|" +
                "9f2d00|" +
                "8b7577|" +
                "b28afe|" +
                "f77b7f|" +
                "957a8c|" +
                "e2e9f3|" +
                "f37d40|" +
                "9e3649|" +
                "fa24ce|" +
                "c13221|" +
                "dd36f8|" +
                "ccb737|" +
                "c08036|" +
                "cf2069|" +
                "bc92cc|" +
                "adbf9d|" +
                "90d85a|" +
                "ffffff"
        },
        {
            "1234",
            "square",
            "12x36",
            12,
            36,
            "aaaaaaaaa|c5eefe6e5|c67d2e096|ccb0fd73d|c710326a8|8f547507f|99fe24076|86a266fdf|beac30c8c|c050edc4f|8ea138918|fffffffff"
        },
    };

    [Theory]
    [MemberData(nameof(DataMatrix_MatchesReferenceGridData))]
    public void DataMatrix_MatchesReferenceGrid(string text, string format, string version, int rows, int columns, string expected)
        => AssertGrid(Create(format, version), text, rows, columns, expected);

    /// <summary>
    /// Capital letters, then pairs of a small and a capital letter. The pairs cost the same in ASCII and C40
    /// encodation, so no scheme wins early and each look-ahead test reads to the end of the pairs. The test
    /// runs with each vector width and without vector hardware, so the vector scan and the one-at-a-time scan
    /// of the look-ahead test must make the same choices. The grids come from bwip-js (dmlookahead.mjs).
    /// </summary>
    [Fact]
    public void DataMatrix_LookAheadOverLongRuns_MatchesReferenceGrid()
        => FeatureTestRunner.RunWithHwIntrinsicsFeature(
            RunLookAheadOverLongRuns,
            HwIntrinsics.AllowAll | HwIntrinsics.DisableAVX512F | HwIntrinsics.DisableAVX | HwIntrinsics.DisableHWIntrinsic);

    /// <summary>
    /// Gets the cases of <see cref="DataMatrixDataLayers_MatchReferenceGrid"/>.
    /// </summary>
    public static TheoryData<string, string, int, int, string> DataMatrixDataLayers_MatchReferenceGridData { get; } = new()
    {
        {
            "gs1datamatrix",
            "(01)09521234543213(17)120508(10)ABCD1234(410)9501101020917",
            22,
            22,
            "aaaaa8|" +
                "cf504c|" +
                "8b1550|" +
                "b3208c|" +
                "ec1628|" +
                "a5c54c|" +
                "a16000|" +
                "f6113c|" +
                "8aaa10|" +
                "cccca4|" +
                "9b1458|" +
                "f9361c|" +
                "b81d20|" +
                "ad0cb4|" +
                "80a3d8|" +
                "b36c94|" +
                "e658b0|" +
                "d57c8c|" +
                "e47108|" +
                "c11d7c|" +
                "c26a50|" +
                "fffffc"
        },
        { "gs1datamatrix", "(01)10012345678902", 16, 16, "aaaa|d01b|89f6|b74d|e2a6|aee7|ff4c|93a9|a3e6|c409|895a|c32f|f9b6|c555|a9ba|ffff" },
        {
            "gs1datamatrix",
            "(01)09521234543213(10)ABC123(21)XYZ789",
            20,
            20,
            "aaaaa|cf3b7|8b0e4|b2cb9|ec20e|a2d73|a07a8|f266b|853de|d1fbd|956c4|f7beb|b74b0|97c8f|848b0|f0905|dd69c|d752d|9b102|fffff"
        },
        {
            "gs1datamatrixrectangular",
            "(01)09521234543213(17)120508(10)ABCD1234(410)9501101020917",
            16,
            36,
            "aaaaaaaaa|" +
                "cf507304b|" +
                "8b15272e6|" +
                "b3216c787|" +
                "ec16259f4|" +
                "a5c5665e9|" +
                "a16034302|" +
                "f613e3cff|" +
                "8aa6bbd10|" +
                "ccd5eb721|" +
                "9b662f792|" +
                "f945e63fd|" +
                "b87fa03ac|" +
                "acfc6b7fb|" +
                "c1a930a40|" +
                "fffffffff"
        },
        { "gs1datamatrixrectangular", "(01)10012345678902", 8, 32, "aaaaaaaa|d0718ba3|89b0a074|b7b98ae9|e200cdb0|ed45f62b|f9a0aca2|ffffffff" },
        { "hibcdatamatrix", "A123BJC5D6E71", 16, 16, "aaaa|e0db|c388|eadf|cd08|b049|c4f4|8a55|acf0|b04f|844e|9f8f|8d52|dba5|cb42|ffff" },
        { "hibcdatamatrix", "A999BJC5D6E71", 16, 16, "aaaa|e55b|c734|e4dd|e796|b1b9|c354|8f85|aea4|b33f|8b5a|98b9|829a|cb35|e38a|ffff" },
        {
            "hibcdatamatrixrectangular",
            "A999BJC5D6E71",
            12,
            26,
            "aaaaaa8|e51bcac|c73b478|e4f12c4|e6d24d0|b315534|c0fa9e0|89a4b6c|bca6978|aa3a4b4|b86cff8|ffffffc"
        },
        {
            "hibcdatamatrixrectangular",
            "A123BJC5D6E71",
            12,
            26,
            "aaaaaa8|e0e8f44|c3f86e0|eae1aa4|ccd3670|b317014|c0fabd0|89a92fc|beba728|a894e9c|b664970|ffffffc"
        },
    };

    /// <summary>
    /// The GS1 and HIBC data layers over Data Matrix: the Function 1 Symbol Character first and between
    /// variable length element strings, or the HIBC flag character, data and check character.
    /// </summary>
    /// <param name="symbology">The symbology name of the reference implementation.</param>
    /// <param name="text">The text to encode.</param>
    /// <param name="rows">The expected number of rows.</param>
    /// <param name="columns">The expected number of columns.</param>
    /// <param name="expected">The expected grid.</param>
    [Theory]
    [MemberData(nameof(DataMatrixDataLayers_MatchReferenceGridData))]
    public void DataMatrixDataLayers_MatchReferenceGrid(string symbology, string text, int rows, int columns, string expected)
    {
        BarcodeSymbology created = symbology switch
        {
            "gs1datamatrix" => new Gs1DataMatrixSymbology(),
            "gs1datamatrixrectangular" => new Gs1DataMatrixRectangularSymbology(),
            "hibcdatamatrix" => new HibcDataMatrixSymbology(),
            _ => new HibcDataMatrixRectangularSymbology(),
        };

        AssertGrid(created, text, rows, columns, expected);
    }

    [Fact]
    public void HibcDataMatrix_RejectsAnIncorrectCheckCharacter()
    {
        Assert.Throws<ArgumentException>(() => Encode(new HibcDataMatrixSymbology(true), "A123BJC5D6E70"));
        Assert.Throws<ArgumentException>(() => Encode(new HibcDataMatrixRectangularSymbology(true), "A123BJC5D6E70"));
    }

    [Fact]
    public void Gs1DataMatrix_RejectsDataOutsideElementStringSyntax()
        => Assert.Throws<ArgumentException>(() => Encode(new Gs1DataMatrixSymbology(), "0109521234543213"));

    /// <summary>
    /// Example 1 of section 2.3 of the GS1 DataMatrix Guideline prints the element strings below the symbol,
    /// one on each line, with the Application Identifiers in parentheses, as Figure 4-6 of the GS1 General
    /// Specifications prefers.
    /// </summary>
    [Fact]
    public void Gs1DataMatrix_PrintsOneElementStringOnEachLine()
    {
        const string text = "(01)03453120000011(17)191125(10)ABCD1234";
        const string expected = "(01)03453120000011\n(17)191125\n(10)ABCD1234";
        BarcodeOptions options = new() { Font = BarcodeFonts.OcrB.CreateFont(12F) };

        Assert.Equal(expected, Assert.Single(SymbolEncoder.Read(new Gs1DataMatrixSymbology(), text, options, SymbolEncoder.Texts)));
        Assert.Equal(expected, Assert.Single(SymbolEncoder.Read(new Gs1DataMatrixRectangularSymbology(), text, options, SymbolEncoder.Texts)));
    }

    /// <summary>
    /// Rule 3b of section 4.14 of the GS1 General Specifications: when Application Identifier (8200)
    /// appears, its URL does not appear in the human readable interpretation.
    /// </summary>
    [Fact]
    public void Gs1DataMatrix_LeavesTheUrlOfApplicationIdentifier8200OutOfTheText()
    {
        BarcodeOptions options = new() { Font = BarcodeFonts.OcrB.CreateFont(12F) };
        string[] texts = SymbolEncoder.Read(new Gs1DataMatrixSymbology(), "(01)09520123456788(8200)http://www.abc.net", options, SymbolEncoder.Texts);
        Assert.Equal("(01)09520123456788", Assert.Single(texts));
    }

    /// <summary>
    /// Rule 1c of section 4.14 of the GS1 General Specifications: the human readable interpretation follows
    /// the order of the encoded data, which for a GS1 Digital Link URI is the primary key and its qualifiers
    /// in the path, then the data attributes in the query string. The values print decoded, and the URI
    /// itself does not print.
    /// </summary>
    [Fact]
    public void Gs1DigitalLinkDataMatrix_PrintsTheElementStringsOfTheUri()
    {
        BarcodeOptions options = new() { Font = BarcodeFonts.OcrB.CreateFont(12F) };
        string[] texts = SymbolEncoder.Read(
            new Gs1DigitalLinkDataMatrixSymbology(),
            "https://id.gs1.org/01/09521234543213/22/ABC%2D123?99=XYZ-987",
            options,
            SymbolEncoder.Texts);

        Assert.Equal("(01)09521234543213\n(22)ABC-123\n(99)XYZ-987", Assert.Single(texts));
    }

    /// <summary>
    /// Figure 3 of ANSI/HIBC 2.6 prints the same string below a Data Matrix symbol as below the linear
    /// symbols, between asterisks.
    /// </summary>
    [Fact]
    public void HibcDataMatrix_PrintsTheDataBetweenDelimiters()
    {
        const string expected = "*+A123BJC5D6E71G*";
        BarcodeOptions options = new() { Font = BarcodeFonts.OcrB.CreateFont(12F) };

        Assert.Equal(expected, Assert.Single(SymbolEncoder.Read(new HibcDataMatrixSymbology(), "A123BJC5D6E71", options, SymbolEncoder.Texts)));
        Assert.Equal(expected, Assert.Single(SymbolEncoder.Read(new HibcDataMatrixRectangularSymbology(), "A123BJC5D6E71", options, SymbolEncoder.Texts)));
    }

    /// <summary>
    /// The figure of section 4.3.3 of ANSI/HIBC 2.6 prints concatenated primary and secondary data on two
    /// lines, broken after the "/" that joins them, with the check character of the whole string.
    /// </summary>
    [Fact]
    public void HibcDataMatrix_BreaksConcatenatedDataAfterTheJoin()
    {
        BarcodeOptions options = new() { Font = BarcodeFonts.OcrB.CreateFont(12F) };
        string[] texts = SymbolEncoder.Read(new HibcDataMatrixSymbology(), "A123BJC5D6E71/$$52001510X3", options, SymbolEncoder.Texts);
        Assert.Equal("*+A123BJC5D6E71/\n$$52001510X3C*", Assert.Single(texts));
    }

    /// <summary>
    /// Section 4.14 rule 2a.iii of the GS1 General Specifications: the font is at least 2 mm high. A point is
    /// 1/72 inch, so a smaller font prints as the font of 2 mm and measures the same. ANSI/HIBC 2.6 states
    /// no size, so HIBC text keeps the size of the caller.
    /// </summary>
    [Fact]
    public void Gs1DataMatrix_TextIsAtLeastTwoMillimetresHigh()
    {
        BarcodeOptions small = new() { Dpi = 300F, Font = BarcodeFonts.OcrB.CreateFont(3F) };
        BarcodeOptions minimum = new() { Dpi = 300F, Font = BarcodeFonts.OcrB.CreateFont(2F * 72F / 25.4F) };

        const string gs1 = "(01)03453120000011(17)191125(10)ABCD1234";
        Assert.Equal(
            BarcodeMeasurer.MeasureRenderableBounds(new Gs1DataMatrixSymbology(), gs1, minimum, PointF.Empty),
            BarcodeMeasurer.MeasureRenderableBounds(new Gs1DataMatrixSymbology(), gs1, small, PointF.Empty));

        Assert.Equal(
            BarcodeMeasurer.MeasureRenderableBounds(new Gs1DigitalLinkDataMatrixSymbology(), "https://id.gs1.org/01/09521234543213", minimum, PointF.Empty),
            BarcodeMeasurer.MeasureRenderableBounds(new Gs1DigitalLinkDataMatrixSymbology(), "https://id.gs1.org/01/09521234543213", small, PointF.Empty));

        Assert.NotEqual(
            BarcodeMeasurer.MeasureRenderableBounds(new HibcDataMatrixSymbology(), "A123BJC5D6E71", minimum, PointF.Empty),
            BarcodeMeasurer.MeasureRenderableBounds(new HibcDataMatrixSymbology(), "A123BJC5D6E71", small, PointF.Empty));
    }

    /// <summary>
    /// The human readable interpretation prints only when the caller sets a font.
    /// </summary>
    [Fact]
    public void Gs1AndHibcDataMatrix_PrintNoTextWithoutAFont()
    {
        BarcodeOptions options = new();

        Assert.Empty(SymbolEncoder.Read(new Gs1DataMatrixSymbology(), "(01)03453120000011", options, SymbolEncoder.Texts));
        Assert.Empty(SymbolEncoder.Read(new Gs1DigitalLinkDataMatrixSymbology(), "https://id.gs1.org/01/09521234543213", options, SymbolEncoder.Texts));
        Assert.Empty(SymbolEncoder.Read(new HibcDataMatrixSymbology(), "A123BJC5D6E71", options, SymbolEncoder.Texts));
    }

    /// <summary>
    /// Section 2.3 and Table 4 of the Mailmark barcode definition document: the data starts with the latch
    /// to C40 encodation, codeword 230, and the Royal Mail fields encode in C40 encodation.
    /// </summary>
    [Fact]
    public void Mailmark2D_StartsWithTheC40Latch()
    {
        string text = Mailmark2DFields + "ABC123";
        int[] message = new int[text.Length];
        for (int i = 0; i < text.Length; i++)
        {
            message[i] = text[i];
        }

        int[] codewords = Codewords(message, [36], 45);
        Assert.Equal(230, codewords[0]);

        // C40 encodation packs three values as 1600 x C1 + 40 x C2 + C3 + 1, and the basic set gives the
        // capital letters A to Z the values 14 to 39, so "JGB" is 23, 20 and 15.
        int packed = (1600 * 23) + (40 * 20) + 15 + 1;
        Assert.Equal(packed >> 8, codewords[1]);
        Assert.Equal(packed & 0xFF, codewords[2]);
    }

    /// <summary>
    /// Section 2.4 of the Mailmark barcode definition document: a clear zone of four modules, and the
    /// recommended module size of 0.5 mm.
    /// </summary>
    [Fact]
    public void Mailmark2D_ClearZoneAndModuleSizeFollowSectionTwoFour()
    {
        Mailmark2DSymbology symbology = new(Mailmark2DType.Type7);
        Assert.Equal(0.5, symbology.NominalXDimension);
        SymbolEncoder.Encode(symbology, Mailmark2DFields, new BarcodeOptions(), static (ref symbol) =>
        {
            Assert.Equal(4, symbol.LeadingQuietZone);
            Assert.Equal(4, symbol.TrailingQuietZone);
            Assert.Equal(4, symbol.TopQuietZone);
            Assert.Equal(4, symbol.BottomQuietZone);
        });
    }

    /// <summary>
    /// Gets the cases of <see cref="Mailmark2D_MatchesReferenceGrid"/>.
    /// </summary>
    public static TheoryData<Mailmark2DType, string, int, int, string> Mailmark2D_MatchesReferenceGridData { get; } = new()
    {
        {
            Mailmark2DType.Type7,
            "JGB 012100123412345678AB19XY1A 0             ",
            24,
            24,
            "aaaaaa|" +
                "c145e5|" +
                "a5c678|" +
                "a81261|" +
                "8d1be2|" +
                "a1810f|" +
                "f00204|" +
                "a6836f|" +
                "b53ee8|" +
                "f6937d|" +
                "9f268a|" +
                "fa3e61|" +
                "ddec70|" +
                "d13419|" +
                "ba1642|" +
                "c38d49|" +
                "fca3c8|" +
                "90d965|" +
                "ad56b4|" +
                "bd7a2d|" +
                "9a46e2|" +
                "e630f5|" +
                "ee0ffa|" +
                "ffffff"
        },
        {
            Mailmark2DType.Type7,
            "JGB 012100123412345678AB19XY1A 0             ABC123",
            24,
            24,
            "aaaaaa|" +
                "c145a5|" +
                "a5c63c|" +
                "a8121b|" +
                "8d1bc2|" +
                "a18127|" +
                "f00204|" +
                "a683e7|" +
                "b53f00|" +
                "f6938d|" +
                "9f2336|" +
                "fa374b|" +
                "dddc7e|" +
                "d12d71|" +
                "ba0042|" +
                "c31ad9|" +
                "fc7b10|" +
                "91b279|" +
                "a52c24|" +
                "b9ded9|" +
                "abbc3c|" +
                "e7710d|" +
                "e637ca|" +
                "ffffff"
        },
        {
            Mailmark2DType.Type9,
            "JGB 012100123412345678AB19XY1A 0             ",
            32,
            32,
            "aaaaaaaa|" +
                "c145f92f|" +
                "a5c69c00|" +
                "a81399c5|" +
                "8d1afbde|" +
                "a181cc25|" +
                "f0029a9e|" +
                "a683cb2f|" +
                "b53ee734|" +
                "f6939c07|" +
                "9f24d3d4|" +
                "fa3fb799|" +
                "ddc29a0a|" +
                "d115f36f|" +
                "ba8ed6b2|" +
                "ffffffff|" +
                "aaaaaaaa|" +
                "c381b851|" +
                "fcf2ade2|" +
                "93c9b3e1|" +
                "af18878c|" +
                "b5b1c65d|" +
                "999487c4|" +
                "aad1b651|" +
                "9b26b138|" +
                "9d6df5b3|" +
                "e004d06c|" +
                "984faf4d|" +
                "edd0a370|" +
                "8bf58ac9|" +
                "c786fe08|" +
                "ffffffff"
        },
        {
            Mailmark2DType.Type9,
            "JGB 012100123412345678AB19XY1A 0             REFERENCE 12300AB",
            32,
            32,
            "aaaaaaaa|" +
                "c145fccf|" +
                "a5c681a0|" +
                "a81392c5|" +
                "8d1af9de|" +
                "a181ca25|" +
                "f002969e|" +
                "a683d32f|" +
                "b53c9f34|" +
                "f691dc07|" +
                "9f28f0d4|" +
                "fa33b57d|" +
                "ddce9ec2|" +
                "d12df13f|" +
                "ba66eee8|" +
                "ffffffff|" +
                "aaaaaaaa|" +
                "c38189d9|" +
                "ffb2948a|" +
                "93cb8c29|" +
                "a71eafe4|" +
                "b1b9ef45|" +
                "95a4eb98|" +
                "82d1d6f5|" +
                "9be29fbc|" +
                "9ddda49b|" +
                "a38a9cfc|" +
                "9bb7df75|" +
                "e5fe9096|" +
                "87ef8049|" +
                "c980fcc4|" +
                "ffffffff"
        },
        {
            Mailmark2DType.Type9,
            "JGB 01A100012300000001NE11AA9Z 4SW1A1AA      https://example.com/12345",
            32,
            32,
            "aaaaaaaa|" +
                "c1479f2b|" +
                "a5c2be00|" +
                "a85998c5|" +
                "9ce6f95c|" +
                "9c49ce25|" +
                "c31a949e|" +
                "a583c32f|" +
                "b01cd734|" +
                "d911f40f|" +
                "c7aea0cc|" +
                "e6bde581|" +
                "e3ceff0a|" +
                "f1c9e931|" +
                "8480eee8|" +
                "ffffffff|" +
                "aaaaaaaa|" +
                "d903bdf1|" +
                "8b76b73a|" +
                "feb58201|" +
                "c89e92e4|" +
                "92f38d01|" +
                "a6ece206|" +
                "f819e435|" +
                "8a709422|" +
                "f2cfddc1|" +
                "fb0ee7dc|" +
                "af6dd461|" +
                "b39685e8|" +
                "93a3967b|" +
                "8b86ba58|" +
                "ffffffff"
        },
        {
            Mailmark2DType.Type29,
            "JGB 012100123412345678AB19XY1A 0             www.xyz.com",
            16,
            48,
            "aaaaaaaaaaaa|" +
                "c1458ffebe77|" +
                "a5c6728ecf38|" +
                "a81245b46fad|" +
                "8d1a86a0a6a6|" +
                "a180fdb1e69d|" +
                "f007e2e61e80|" +
                "a684fb9b88ef|" +
                "b52bd88e2f54|" +
                "f6aeafcefdf7|" +
                "9f4fdc8885e8|" +
                "fa4861e22a99|" +
                "9cbc5c872712|" +
                "96c019b59fd5|" +
                "be2fea87d572|" +
                "ffffffffffff"
        },
        {
            Mailmark2DType.Type29,
            "JGB 012100123412345678AB19XY1A 0             ",
            16,
            48,
            "aaaaaaaaaaaa|" +
                "c1458ff643cf|" +
                "a5c672919a1c|" +
                "a812459ee835|" +
                "8d1a86a1d632|" +
                "a180fb90dec7|" +
                "f007e6e66136|" +
                "a684f9ddf9e7|" +
                "b52ad08d0414|" +
                "f6ac2bc91517|" +
                "9f489c8f790c|" +
                "fa4341d2b75d|" +
                "9c819c95c04e|" +
                "96d959d61cdf|" +
                "be3fea87f4a4|" +
                "ffffffffffff"
        },
    };

    /// <summary>
    /// Table 1 of the Mailmark barcode definition document: type 7 is 24 x 24 modules, type 9 32 x 32 and
    /// type 29 16 x 48.
    /// </summary>
    /// <param name="type">The barcode type.</param>
    /// <param name="text">The text to encode.</param>
    /// <param name="rows">The expected number of rows.</param>
    /// <param name="columns">The expected number of columns.</param>
    /// <param name="expected">The expected grid.</param>
    [Theory]
    [MemberData(nameof(Mailmark2D_MatchesReferenceGridData))]
    public void Mailmark2D_MatchesReferenceGrid(Mailmark2DType type, string text, int rows, int columns, string expected)
        => AssertGrid(new Mailmark2DSymbology(type), text, rows, columns, expected);

    /// <summary>
    /// Table 2 and Appendix A of the Mailmark barcode definition document: each Royal Mail field takes its
    /// legitimate values, padded with spaces to its fixed length.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    [Theory]
    [InlineData("JGB 012100123412345678AB19XY1A 0")]
    [InlineData("XGB 012100123412345678AB19XY1A 0             ")]
    [InlineData("JGB 012100123412345678AB19XY1A 0           X ")]
    [InlineData("JGB 0 2100123412345678AB19XY1A 0             ")]
    [InlineData("JGB 022100123412345678AB19XY1A 0             ")]
    [InlineData("JGB 01210012341234567AAB19XY1A 0             ")]
    [InlineData("JGB 01210012341234567aAB19XY1A 0             ")]
    [InlineData("JGB 012100123412345678AB1 XY1A 0             ")]
    [InlineData("JGB 012100123412345678AB19XY   0             ")]
    [InlineData("JGB 012100123412345678AB19XY1A 7             ")]
    [InlineData("JGB 012100123412345678AB19XY1A 0SW1A         ")]
    public void Mailmark2D_RejectsInvalidRoyalMailFields(string text)
        => Assert.Throws<ArgumentException>(() => Encode(new Mailmark2DSymbology(Mailmark2DType.Type9), text));

    /// <summary>
    /// Appendix A, Table 14 of the Mailmark barcode definition document: the destination post code plus
    /// DPS may be spaces, an outward code alone or the full post code with its delivery point, and the
    /// return to sender post code may be spaces or the outward and inward codes.
    /// </summary>
    /// <param name="destination">The destination post code plus DPS field.</param>
    /// <param name="returnToSender">The return to sender post code field.</param>
    [Theory]
    [InlineData("         ", "       ")]
    [InlineData("XY11     ", "       ")]
    [InlineData("N1      ", "W1A1AA ")]
    [InlineData("NE11AA9Z ", "SW1A1AA")]
    [InlineData("EC1A1BB1A", "M11AE  ")]
    public void Mailmark2D_AcceptsThePostcodeFormatsOfAppendixA(string destination, string returnToSender)
    {
        string text = "JGB 01210012341234567" + "8" + destination.PadRight(9) + "0" + returnToSender + "      ";
        Assert.Equal(24, Size(new Mailmark2DSymbology(Mailmark2DType.Type7), text).Rows);
    }

    [Fact]
    public void Mailmark2D_RejectsCustomerContentLargerThanTheType()
        => Assert.Throws<ArgumentException>(() => Encode(new Mailmark2DSymbology(Mailmark2DType.Type7), Mailmark2DFields + "ABCDEFGHIJ"));

    /// <summary>
    /// Gets the cases of <see cref="Gs1DigitalLinkDataMatrix_MatchesReferenceGrid"/>.
    /// </summary>
    public static TheoryData<string, int, int, string> Gs1DigitalLinkDataMatrix_MatchesReferenceGridData { get; } = new()
    {
        {
            "https://id.gs1.org/01/09521234543213/22/ABC%2D123?99=XYZ-987",
            32,
            32,
            "aaaaaaaa|" +
                "cb83948f|" +
                "950eb702|" +
                "816f9e47|" +
                "d41cdedc|" +
                "dab5c225|" +
                "ee5ed09e|" +
                "cebfcb2f|" +
                "f418e734|" +
                "cb43dc07|" +
                "881cd1ce|" +
                "f173b62b|" +
                "c73e922e|" +
                "b4bdf06f|" +
                "8776ffd2|" +
                "ffffffff|" +
                "aaaaaaaa|" +
                "b0e18c09|" +
                "f332c600|" +
                "d34999e9|" +
                "9a90a292|" +
                "e1b58575|" +
                "9d9afa04|" +
                "9cd99db1|" +
                "9014e296|" +
                "c9fdea7d|" +
                "fd8cd686|" +
                "91f1ea27|" +
                "e7748df2|" +
                "ff33901f|" +
                "c46c9482|" +
                "ffffffff"
        },
        {
            "https://example.com/01/09520123456788/10/ABC123/21/12345XYZ",
            32,
            32,
            "aaaaaaaa|" +
                "cb81eb09|" +
                "957ca280|" +
                "813f88c5|" +
                "d5dce35e|" +
                "dc758c25|" +
                "e940c89e|" +
                "dba5b32f|" +
                "f4da8734|" +
                "c761dc0f|" +
                "8832f0ce|" +
                "b1adb717|" +
                "c6209f9a|" +
                "b54df719|" +
                "85b6d4b0|" +
                "ffffffff|" +
                "aaaaaaaa|" +
                "b781b411|" +
                "ec90bf90|" +
                "c74bdb51|" +
                "871cd2e2|" +
                "b7bfab05|" +
                "93b68f50|" +
                "9aefe9b1|" +
                "d324b004|" +
                "fd3b83bd|" +
                "e01ab55e|" +
                "9a37fbcf|" +
                "e3408bf0|" +
                "8f159ba9|" +
                "c0649584|" +
                "ffffffff"
        },
        {
            "https://id.gs1.org/00/395123451234567895",
            22,
            22,
            "aaaaa8|" +
                "cb8254|" +
                "950ee8|" +
                "816e14|" +
                "d41c08|" +
                "dab764|" +
                "ee5c00|" +
                "ceb9dc|" +
                "f40a70|" +
                "cb403c|" +
                "887c68|" +
                "f129fc|" +
                "c51c98|" +
                "b53914|" +
                "8109a8|" +
                "b68e24|" +
                "b412a0|" +
                "dfe9a4|" +
                "b1dd88|" +
                "99786c|" +
                "d86050|" +
                "fffffc"
        },
        {
            "https://example.com/some/path/414/9520123456788/254/32a%2Fb",
            32,
            32,
            "aaaaaaaa|" +
                "cb81f689|" +
                "9578aa82|" +
                "813dfbc7|" +
                "d5c6cd5e|" +
                "dc519425|" +
                "e956809e|" +
                "db61bb2f|" +
                "f6988f34|" +
                "c5d7dc0f|" +
                "8ad891d6|" +
                "b837b6bb|" +
                "db8697ee|" +
                "9235f8bd|" +
                "d52ed5ac|" +
                "ffffffff|" +
                "aaaaaaaa|" +
                "e9c18fe9|" +
                "9a12a938|" +
                "f649c239|" +
                "f516db1c|" +
                "e9bfe53d|" +
                "8582cf72|" +
                "e2c3af87|" +
                "b37ac42c|" +
                "d515c6e1|" +
                "e1a0a272|" +
                "a635fbbf|" +
                "cbb8eba0|" +
                "b559945b|" +
                "a2629518|" +
                "ffffffff"
        },
        {
            "https://example.com/01/09520123456788?3103=000189&17=280101&linkType=gs1:pip",
            32,
            32,
            "aaaaaaaa|" +
                "cb81e20d|" +
                "957cbf60|" +
                "813fd9c5|" +
                "d5dc818e|" +
                "dc75d995|" +
                "e944afde|" +
                "dba9d7af|" +
                "f4c09934|" +
                "c763f617|" +
                "883addd2|" +
                "b1a7a393|" +
                "c62cf25e|" +
                "b5418247|" +
                "859af78c|" +
                "ffffffff|" +
                "aaaaaaaa|" +
                "b71fc681|" +
                "ef62e918|" +
                "c6a78f01|" +
                "83e4aef6|" +
                "b03980f5|" +
                "8e84a78c|" +
                "a4ade00b|" +
                "973ee2be|" +
                "b871cce5|" +
                "a23291b6|" +
                "9e17fca7|" +
                "90d0a096|" +
                "f51f80c5|" +
                "e062940a|" +
                "ffffffff"
        },
    };

    /// <summary>
    /// GS1 Digital Link URIs that follow the GS1 Digital Link Standard: URI Syntax: a primary key with its
    /// qualifiers in order, data attributes and an extension parameter in the query string, and custom path
    /// segments before the key. The symbol carries the URI as it is.
    /// </summary>
    /// <param name="text">The URI.</param>
    /// <param name="rows">The expected number of rows.</param>
    /// <param name="columns">The expected number of columns.</param>
    /// <param name="expected">The expected grid.</param>
    [Theory]
    [MemberData(nameof(Gs1DigitalLinkDataMatrix_MatchesReferenceGridData))]
    public void Gs1DigitalLinkDataMatrix_MatchesReferenceGrid(string text, int rows, int columns, string expected)
        => AssertGrid(new Gs1DigitalLinkDataMatrixSymbology(), text, rows, columns, expected);

    /// <summary>
    /// URIs the GS1 Digital Link Standard: URI Syntax does not allow: a scheme other than http or https
    /// (section 4.11), a GTIN of fewer than 14 digits (section 4.1), key qualifiers out of order (section
    /// 4.9), a key qualifier in the query string, no primary key in the path, and data an Application
    /// Identifier does not allow.
    /// </summary>
    /// <param name="text">The URI.</param>
    [Theory]
    [InlineData("ftp://example.com/01/09520123456788")]
    [InlineData("https://example.com/01/9520123456788")]
    [InlineData("https://example.com/01/09520123456788/21/ABC/10/XYZ")]
    [InlineData("https://example.com/01/09520123456788?10=ABC")]
    [InlineData("https://example.com/01/09520123456788?17=281301")]
    [InlineData("https://example.com/10/ABC")]
    [InlineData("https://example.com/01/09520123456788/")]
    [InlineData("https://example.com/01/09520123456788 ")]
    public void Gs1DigitalLinkDataMatrix_RejectsInvalidUris(string text)
        => Assert.Throws<ArgumentException>(() => Encode(new Gs1DigitalLinkDataMatrixSymbology(), text));

    /// <summary>
    /// Encodes the long run inputs and compares each grid with its reference grid. The method is static, because
    /// the feature test runner runs it in a child process.
    /// </summary>
    private static void RunLookAheadOverLongRuns()
    {
        (string Text, int Rows, int Columns, string Grid)[] cases =
        [
            (
                "ABCDEF" + Pairs(60),
                44,
                44,
                "aaaaaaaaaaa|" +
                    "a6d31f34c73|" +
                    "b2e39a98e68|" +
                    "98734f1cd37|" +
                    "88698b9a63c|" +
                    "8d31cf4c727|" +
                    "a639a38e69e|" +
                    "c734c7cd35f|" +
                    "e698e3a63de|" +
                    "d31cd6c7205|" +
                    "e39a62e689a|" +
                    "f34c76d32a9|" +
                    "e98e6a63f20|" +
                    "b1cd3673a1d|" +
                    "b9a63a6929c|" +
                    "b4c73734dd9|" +
                    "98e69a3814e|" +
                    "9cd31f39c4b|" +
                    "9a639a9f2f4|" +
                    "cc734f58525|" +
                    "8e698be7de6|" +
                    "fffffffffff|" +
                    "aaaaaaaaaaa|" +
                    "cd31ce7693f|" +
                    "a639a27c4e6|" +
                    "c734d730e7f|" +
                    "e698faa4356|" +
                    "d31c9f315c9|" +
                    "e39a12895e8|" +
                    "f34d4e03f29|" +
                    "e98fa3d80d0|" +
                    "b1c9fed6f4d|" +
                    "b9a14af88c8|" +
                    "b4d44f77d89|" +
                    "98f91a2f63a|" +
                    "9c9e7f8b791|" +
                    "9a133bbd426|" +
                    "cd4a9fad80d|" +
                    "8f8ad3efdae|" +
                    "c9ff66762b7|" +
                    "a1ff4ae1a52|" +
                    "dc06d661d47|" +
                    "f5469a39962|" +
                    "fffffffffff"),
            (
                "ABCDEF" + Pairs(30) + "1" + Pairs(30) + "12345" + Pairs(20),
                48,
                48,
                "aaaaaaaaaaaa|" +
                    "a6d31db4ca55|" +
                    "b2e39a98ebfa|" +
                    "98734d9cfd1f|" +
                    "88698e9a2512|" +
                    "8d31cdcca543|" +
                    "a639a68ebfa8|" +
                    "c734c7cfd1f7|" +
                    "e698e6a2513c|" +
                    "d31cd3ca543f|" +
                    "e39a62ebfa9e|" +
                    "f34c77fd1f2b|" +
                    "e98e6ea513c2|" +
                    "b1cd13a543c7|" +
                    "b9a652bfa864|" +
                    "b4c75fd1f559|" +
                    "98e7e8d138b0|" +
                    "9cd129c439a5|" +
                    "9a652ee69e14|" +
                    "cc75f58b59f5|" +
                    "8e7e94d3e9e6|" +
                    "cd1293d27879|" +
                    "a652f8e8660c|" +
                    "ffffffffffff|" +
                    "aaaaaaaaaaaa|" +
                    "c75f4ff53d15|" +
                    "e7e94cbe254e|" +
                    "d12961a7f3d7|" +
                    "e52faa84c3de|" +
                    "f5f4add05643|" +
                    "fe948ee182ee|" +
                    "9297c9f1bf71|" +
                    "d2faa0d4fdf0|" +
                    "df4ad5a85259|" +
                    "e948f88014d0|" +
                    "a97c9fa6ce05|" +
                    "afaa1092670c|" +
                    "f4ad4bdecbd9|" +
                    "948f8eda8582|" +
                    "97c9d1c65e77|" +
                    "faa1f2867f4e|" +
                    "cad4739b2797|" +
                    "c8f82af57b8a|" +
                    "fc9fb9de8cc3|" +
                    "aa1ab0cef132|" +
                    "adc02ddf8ae5|" +
                    "af5c68e31a24|" +
                    "ffffffffffff"),
            (
                "ABCDEF" + Pairs(50) + "ABC*DEF>GHI\rJKL*",
                44,
                44,
                "aaaaaaaaaaa|" +
                    "a6d31f34c73|" +
                    "b2e39a98e68|" +
                    "98734f1cd37|" +
                    "88698b9a63c|" +
                    "8d31cf4c727|" +
                    "a639a38e69e|" +
                    "c734c7cd35f|" +
                    "e698e3a63de|" +
                    "d31cd6c7205|" +
                    "e39a62e689a|" +
                    "f34c76d32a9|" +
                    "e98e6a63f20|" +
                    "b1cd3673a0d|" +
                    "b9a63a6929c|" +
                    "b4c73732df1|" +
                    "98e69a3915a|" +
                    "9cd31f35c69|" +
                    "9a639a9b23e|" +
                    "cc734f187a5|" +
                    "8e699bc799e|" +
                    "fffffffffff|" +
                    "aaaaaaaaaaa|" +
                    "cd31df060e7|" +
                    "a639ca7c9f6|" +
                    "c734be70e0f|" +
                    "e6983aa3fde|" +
                    "d31c2737911|" +
                    "e39bb29ea46|" +
                    "f34fbe79661|" +
                    "e99423cdf00|" +
                    "b1f53f8752d|" +
                    "b9584b944a8|" +
                    "b4e15f29b89|" +
                    "99570381a12|" +
                    "9fee4f262ff|" +
                    "9cf62a43270|" +
                    "cd65bfac75d|" +
                    "acfbf222f56|" +
                    "cc9646c5ec7|" +
                    "8a190befafe|" +
                    "bbc7d6c16d9|" +
                    "d5a6fa29b62|" +
                    "fffffffffff"),
            (
                "ABCDEF" + Pairs(50) + "éèêëàâäöüß",
                44,
                44,
                "aaaaaaaaaaa|" +
                    "c9e26e79047|" +
                    "c0faebedd44|" +
                    "8aa6c70fd8f|" +
                    "be17dac27bc|" +
                    "c757cf5c107|" +
                    "f1c11ba20bc|" +
                    "e7091e24fdd|" +
                    "adc91b7995e|" +
                    "c7cf4e7ec05|" +
                    "ce330b8d09a|" +
                    "cc6bd7222af|" +
                    "a366634f726|" +
                    "e223cf37a13|" +
                    "be4bc36729c|" +
                    "fb51e752d8d|" +
                    "9fd2ba0912e|" +
                    "b4935f15c4f|" +
                    "9a518adb284|" +
                    "80ea2f984bf|" +
                    "b5f04a47dea|" +
                    "fffffffffff|" +
                    "aaaaaaaaaaa|" +
                    "b05b370759b|" +
                    "b872ca7d76a|" +
                    "ab5f9e75bcf|" +
                    "a491faa2fe6|" +
                    "f885a7363dd|" +
                    "b4dab285018|" +
                    "8f37be1eb61|" +
                    "8f8423bcd9c|" +
                    "b1a93ec036b|" +
                    "a0fd4a46902|" +
                    "b0165efcf33|" +
                    "c5c11bcbc70|" +
                    "ede616181ad|" +
                    "aea53bc6256|" +
                    "daabdfcf19b|" +
                    "d0f41aae1e2|" +
                    "fca19f62127|" +
                    "bf153a912e0|" +
                    "c6cd371a579|" +
                    "e2f8b3220c6|" +
                    "fffffffffff"),
        ];

        foreach ((string text, int rows, int columns, string grid) in cases)
        {
            AssertGrid(new DataMatrixSymbology(), text, rows, columns, grid);
        }

        // The pairs of a small and a capital letter, the given number of times.
        static string Pairs(int count) => string.Concat(Enumerable.Repeat("aA", count));
    }

    /// <summary>
    /// Encodes the characters of the text into data codewords with no C40 prefix.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    /// <param name="capacities">The data codeword capacities of the sizes the data may take, smallest first.</param>
    /// <returns>The data codewords.</returns>
    private static int[] Codewords(string text, int[] capacities)
    {
        int[] message = new int[text.Length];
        for (int i = 0; i < text.Length; i++)
        {
            message[i] = text[i];
        }

        return Codewords(message, capacities, 0);
    }

    /// <summary>
    /// Encodes a message into data codewords.
    /// </summary>
    /// <param name="message">The message values, where -1 stands for the Function 1 Symbol Character.</param>
    /// <param name="capacities">The data codeword capacities of the sizes the data may take, smallest first.</param>
    /// <param name="c40Prefix">The number of leading values that encode in C40 encodation.</param>
    /// <returns>The data codewords.</returns>
    private static int[] Codewords(int[] message, int[] capacities, int c40Prefix)
    {
        int[] codewords = new int[DataMatrixCodewords.MaximumCodewords];
        int count = DataMatrixCodewords.Encode(message, capacities, c40Prefix, codewords);
        return codewords[..count];
    }

    private static string Digits(int count)
    {
        StringBuilder builder = new(count);
        for (int i = 0; i < count; i++)
        {
            builder.Append((char)('0' + (i % 10)));
        }

        return builder.ToString();
    }

    private static BarcodeSymbology Create(string format, string version)
    {
        if (version.Length > 0)
        {
            string[] size = version.Split('x');
            int rows = int.Parse(size[0], System.Globalization.CultureInfo.InvariantCulture);
            int columns = int.Parse(size[1], System.Globalization.CultureInfo.InvariantCulture);
            return format == "dmre" ? new DataMatrixRectangularExtensionSymbology(rows, columns) : new DataMatrixSymbology(rows, columns);
        }

        return format switch
        {
            "rectangle" => new DataMatrixRectangularSymbology(),
            "dmre" => new DataMatrixRectangularExtensionSymbology(),
            _ => new DataMatrixSymbology(),
        };
    }

    /// <summary>
    /// Encodes the text with the default options.
    /// </summary>
    /// <param name="symbology">The symbology to encode with.</param>
    /// <param name="text">The text to encode.</param>
    private static void Encode(BarcodeSymbology symbology, string text)
        => SymbolEncoder.Encode(symbology, text, new BarcodeOptions());

    /// <summary>
    /// Encodes the text with the default options and returns the size of the symbol.
    /// </summary>
    /// <param name="symbology">The symbology to encode with.</param>
    /// <param name="text">The text to encode.</param>
    /// <returns>The number of module rows and module columns.</returns>
    private static (int Rows, int Columns) Size(BarcodeSymbology symbology, string text)
        => SymbolEncoder.Read(symbology, text, new BarcodeOptions(), static (ref symbol) => (symbol.MatrixRows, symbol.Columns));

    /// <summary>
    /// Encodes the text with the default options and compares the size and the modules of the symbol
    /// with the reference.
    /// </summary>
    /// <param name="symbology">The symbology to encode with.</param>
    /// <param name="text">The text to encode.</param>
    /// <param name="rows">The expected number of module rows.</param>
    /// <param name="columns">The expected number of module columns.</param>
    /// <param name="expected">The expected grid, as <see cref="Grid"/> writes it.</param>
    private static void AssertGrid(BarcodeSymbology symbology, string text, int rows, int columns, string expected)
        => SymbolEncoder.Encode(symbology, text, new BarcodeOptions(), (ref symbol) =>
        {
            Assert.Equal((rows, columns), (symbol.MatrixRows, symbol.Columns));
            Assert.Equal(expected, Grid(ref symbol));
        });

    /// <summary>
    /// Writes the modules of a symbol as rows of hexadecimal digits separated by a vertical bar.
    /// </summary>
    /// <param name="symbol">The encoded symbol.</param>
    /// <returns>The grid.</returns>
    private static string Grid(ref BarcodeSymbol symbol)
    {
        StringBuilder builder = new();
        for (int r = 0; r < symbol.MatrixRows; r++)
        {
            if (r > 0)
            {
                builder.Append('|');
            }

            // Four modules make one hexadecimal digit, the leftmost module the most significant bit. A
            // row whose width is not a multiple of four pads its last digit with light modules, the format
            // the reference vectors use.
            for (int c = 0; c < symbol.Columns; c += 4)
            {
                int digit = 0;
                for (int b = 0; b < 4; b++)
                {
                    digit <<= 1;
                    if (c + b < symbol.Columns && symbol.Modules[(r * symbol.Columns) + c + b])
                    {
                        digit |= 1;
                    }
                }

                builder.Append("0123456789abcdef"[digit]);
            }
        }

        return builder.ToString();
    }
}
