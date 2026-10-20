// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Barcodes;

namespace SixLabors.ImageSharp.Drawing.Tests.Barcodes;

/// <summary>
/// Encoder tests for the 2D component of the GS1 Composite symbologies of ISO/IEC 24723.
/// </summary>
public class Gs1CompositeSymbologyTests
{
    /// <summary>
    /// CC-B takes only the sizes of Table 5-41 of the GS1 General Specifications, which "lists all the possible column and row combinations for CC-B". Data
    /// that a smaller MicroPDF417 would hold takes the smallest size of its column count: 17 rows of 2 columns, 15 rows of 3 and 10 rows of 4.
    /// </summary>
    /// <param name="columns">The data columns.</param>
    /// <param name="rows">The rows of the smallest size of Table 5-41 of that column count.</param>
    [Theory]
    [InlineData(2, 17)]
    [InlineData(3, 15)]
    [InlineData(4, 10)]
    public void TakesTheSmallestCcBSizeOfTable541(int columns, int rows)
        => Assert.Equal(
            rows,
            SymbolEncoder.Read(new Gs1CompositeComponentSymbology(Gs1CompositeComponent.CcB, columns), "(21)1", new BarcodeOptions(), static (ref s) => s.Rows.Length));

    /// <summary>
    /// Clause 10 of ISO/IEC 24723: "the total number of CC-C columns shall be less than four times the number of rows in the component". A row of c data columns
    /// is 17c + 69 modules wide: the 17 module start pattern, the two row indicators, the data columns and the 18 module stop pattern. Short data in many
    /// columns would break the rule, so the symbol takes fewer columns.
    /// </summary>
    /// <param name="columns">The data columns asked for.</param>
    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(12)]
    [InlineData(30)]
    public void KeepsCcCColumnsUnderFourTimesTheRows(int columns)
    {
        (float width, int rows) = SymbolEncoder.Read(
            new Gs1CompositeComponentSymbology(Gs1CompositeComponent.CcC, columns),
            "(21)1",
            new BarcodeOptions(),
            static (ref s) => (s.WidthInModules, s.Rows.Length));

        int totalColumns = (((int)width - 69) / 17) + 4;
        Assert.True(totalColumns < 4 * rows, $"{totalColumns} columns in {rows} rows.");
    }
}
