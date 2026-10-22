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
    /// that fits a smaller MicroPDF417 takes the smallest size of its column count: 17 rows of 2 columns, 15 rows of 3 and 10 rows of 4.
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
            SymbolEncoder.Read(
                new Gs1CompositeComponentSymbology(Gs1CompositeComponent.CcB, columns),
                "(21)1",
                new BarcodeOptions(),
                static (ref s) => s.Rows.Length));

    /// <summary>
    /// Clause 10 of ISO/IEC 24723: "the total number of CC-C columns shall be less than four times the number of rows in the component". A row of c data
    /// columns is 17c + 69 modules wide: the 17 module start pattern, the two row indicators, the data columns and the 18 module stop pattern. Short data in
    /// many columns breaks the rule, so the symbol takes fewer columns.
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

    /// <summary>
    /// The note of section 4.13 of the GS1 General Specifications: "GTINs encoded in an EAN/UPC and ITF-14 symbols are to be regarded as element strings
    /// prefixed by an inferred AI (01)". The GTIN of the EAN-13 symbol 9521234543213 counts as (01)09521234543213 with the 2D component: another GTIN in the
    /// 2D component breaks the pair rules of section 4.13.1, the same GTIN repeats it, and the (8200) URL of section 3.9.27 has its (01) in the symbol.
    /// </summary>
    [Fact]
    public void Ean13CompositeInfersTheGtinOfItsLinearComponent()
    {
        Ean13CompositeSymbology symbology = new();

        Assert.Throws<ArgumentException>(() => Measure(symbology, "9521234543213", "(01)09506000134352"));
        Assert.Null(Record.Exception(() => Measure(symbology, "9521234543213", "(01)09521234543213")));
        Assert.Null(Record.Exception(() => Measure(symbology, "9521234543213", "(8200)HTTP://EXAMPLE.COM")));
    }

    /// <summary>
    /// Section 3.9.27 of the GS1 General Specifications ties the (8200) URL to a GTIN "encoded in one symbol". A GS1 Composite symbol is one symbol, so a
    /// GTIN in its 2D component serves an (8200) URL in its linear component.
    /// </summary>
    [Fact]
    public void Gs1128CompositeValidatesItsComponentsAsOneSymbol()
        => Assert.Null(Record.Exception(() => Measure(new Gs1128CompositeSymbology(), "(8200)HTTP://EXAMPLE.COM", "(01)09521234543213")));

    /// <summary>
    /// Measures a composite symbol with the default options, which encodes it.
    /// </summary>
    /// <param name="symbology">The composite symbology.</param>
    /// <param name="first">The text of the linear component.</param>
    /// <param name="second">The element strings of the 2D component.</param>
    private static void Measure(CompositeBarcodeSymbology symbology, string first, string second)
        => BarcodeMeasurer.Measure(symbology, new BarcodeOptions(), PointF.Empty, first, second);
}
