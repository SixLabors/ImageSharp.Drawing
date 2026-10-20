// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Barcodes;

namespace SixLabors.ImageSharp.Drawing.Tests.Barcodes;

/// <summary>
/// Encoder tests for <see cref="MicroPdf417Symbology"/>. The tests check the row width of each column count, the quiet zones, the row height, the data capacity
/// and the input that the symbology rejects.
/// </summary>
public class MicroPdf417SymbologyTests
{
    /// <summary>
    /// A row is the left row address pattern, the symbol characters, the right row address pattern and a single module bar, with a centre row address pattern
    /// in a symbol of three or four columns: 38, 55, 82 and 99 modules for one to four columns. The quiet zone is one module on all four sides and the row
    /// height two modules.
    /// </summary>
    /// <param name="columns">The number of data columns.</param>
    /// <param name="width">The row width in modules.</param>
    [Theory]
    [InlineData(1, 38)]
    [InlineData(2, 55)]
    [InlineData(3, 82)]
    [InlineData(4, 99)]
    public void MicroPdf417_RowWidthFollowsTheColumns(int columns, int width)
        => SymbolEncoder.Encode(new MicroPdf417Symbology(columns), "MicroPDF417", new BarcodeOptions(), (ref symbol) =>
        {
            foreach (BarcodeRow row in symbol.Rows)
            {
                int[] runs = SymbolEncoder.RowRuns(ref symbol, row);

                Assert.Equal(width, runs.Sum());
                Assert.Equal(1, runs[^1]);
                Assert.All(SymbolEncoder.RowBarHeights(ref symbol, row), height => Assert.Equal(2F, height));
            }

            Assert.Equal(1F, symbol.LeadingQuietZone);
            Assert.Equal(1F, symbol.TrailingQuietZone);
            Assert.Equal(1F, symbol.TopQuietZone);
            Assert.Equal(1F, symbol.BottomQuietZone);
        });

    /// <summary>
    /// The largest symbol, four columns of 44 rows with 50 error correction codewords, holds 126 data codewords, and one column of 28 rows holds 20. Data
    /// that no size holds throws.
    /// </summary>
    [Fact]
    public void MicroPdf417_RejectsDataThatNoSizeHolds()
    {
        Assert.Throws<ArgumentException>(() => Encode(new MicroPdf417Symbology(), new string('\u00E9', 160)));
        Assert.Throws<ArgumentException>(() => Encode(new MicroPdf417Symbology(1), new string('A', 60)));
    }

    /// <summary>
    /// MicroPDF417 carries the byte values U+0000 to U+00FF. A character above U+00FF throws.
    /// </summary>
    [Fact]
    public void MicroPdf417_RejectsCharactersAboveTheByteRange()
        => Assert.Throws<ArgumentException>(() => Encode(new MicroPdf417Symbology(), "Micro\u0100"));

    /// <summary>
    /// The constructor rejects a column count outside 1 to 4.
    /// </summary>
    [Fact]
    public void MicroPdf417_RejectsColumnsOutsideTheirRange()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new MicroPdf417Symbology(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MicroPdf417Symbology(5));
    }

    /// <summary>
    /// Encodes the text with the default options.
    /// </summary>
    /// <param name="symbology">The symbology to encode with.</param>
    /// <param name="text">The text to encode.</param>
    private static void Encode(BarcodeSymbology symbology, string text)
        => SymbolEncoder.Encode(symbology, text, new BarcodeOptions());
}
