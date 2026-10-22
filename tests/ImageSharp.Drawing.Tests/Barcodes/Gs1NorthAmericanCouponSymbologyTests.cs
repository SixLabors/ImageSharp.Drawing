// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Barcodes;

namespace SixLabors.ImageSharp.Drawing.Tests.Barcodes;

/// <summary>
/// Encoder tests for <see cref="Gs1NorthAmericanCouponSymbology"/>. The tests check the human readable interpretation, the row and separator heights, the
/// coupon code rules of Table C and the segment counts of the North American Coupon Application Guideline.
/// </summary>
public class Gs1NorthAmericanCouponSymbologyTests
{
    /// <summary>
    /// Section 5.5 of the guideline: "The only human readable digits shown from the GS1 DataBar barcode will be the GS1 Company Prefix and Offer Code separated
    /// by a dash." Figure 1 prints them above the symbol, centred, as 0614141-654321. Example 2 gives the offer code 1234 as the six digits 001234.
    /// </summary>
    /// <param name="text">The element string to encode.</param>
    /// <param name="expected">The printed line.</param>
    [Theory]
    [InlineData("(8110)1061414165432131501101201211014092110256100126663101231", "0614141-654321")]
    [InlineData("(8110)106141410012342501106501013085093101231", "0614141-001234")]
    public void Gs1NorthAmericanCoupon_TextPrintsThePrefixAndOfferCodeAbove(string text, string expected)
    {
        BarcodeOptions options = new() { Font = BarcodeFonts.OcrB.CreateFont(12F) };

        SymbolEncoder.Encode(new Gs1NorthAmericanCouponSymbology(), text, options, (ref symbol) =>
        {
            BarcodeTextPlacement placement = Assert.Single(symbol.Text.ToArray());
            Assert.Equal(expected, symbol.TextOf(placement).ToString());
            Assert.Equal(BarcodeTextSide.AboveBars, placement.Side);
            Assert.Equal(-BarcodeTextPlacement.Clearance, placement.TextEdge);
            Assert.Equal(0F, placement.Left);
            Assert.Equal(symbol.WidthInModules, placement.Right);
        });
    }

    /// <summary>
    /// Section 5.4 of the guideline: "Each row shall be 34X high, and the separator pattern shall be 3X (minimum) high", with a nominal X dimension of 0.33
    /// millimetre, 33 pixels at 100 pixels a millimetre. Figure 1 has eight segments in a row, 200 modules, which is the default.
    /// </summary>
    [Fact]
    public void Gs1NorthAmericanCoupon_HasEightSegmentRowsOfThirtyFourModules()
    {
        Gs1NorthAmericanCouponSymbology symbology = new();

        Assert.Equal(8, symbology.Segments);

        BarcodeOptions options = new() { Dpi = SymbolEncoder.MillimetreDpi };

        SymbolEncoder.Encode(symbology, "(8110)1061414165432131501101201211014092110256100126663101231", options, static (ref symbol) =>
        {
            Assert.Equal(0.33, symbol.XDimension);
            Assert.Equal(200F, symbol.WidthInModules);
            Assert.Equal(34F, symbol.Rows[0].Height);
            Assert.Equal([1F, 1F, 1F], SymbolEncoder.RowHeights(ref symbol)[1..4]);
        });
    }

    /// <summary>
    /// Table C of the guideline: a leap year allows 29 February, the start date of data field 4 may equal the expiration date of data field 3, and data field 9
    /// carries its four miscellaneous elements. The guideline sets no order for the optional data fields, so data field 1 can follow data field 3.
    /// </summary>
    /// <param name="text">The element string to encode.</param>
    [Theory]
    [InlineData("(8110)106141410012342501106503120229")]
    [InlineData("(8110)106141410012342501106503101231410123190010")]
    [InlineData("(8110)106141410012342501106503101231101308509")]
    public void Gs1NorthAmericanCoupon_AcceptsValidOptionalFields(string text)
        => Assert.Null(Record.Exception(() => SymbolEncoder.Encode(new Gs1NorthAmericanCouponSymbology(), text, new BarcodeOptions())));

    /// <summary>
    /// The symbol carries one element string, Application Identifier (8110), whose coupon code follows Table C of the guideline: digits only, at most 70 of
    /// them, every required element present, every indicator and code in its range, valid dates with the start date not after the expiration date, and only
    /// the optional data fields 1, 2, 3, 4, 5, 6 and 9.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    [Theory]
    [InlineData("(01)09521234543213")]
    [InlineData("(8110)106141416543213500110000310123196000(21)1")]
    [InlineData("(8110)7061414165432131501101200")]
    [InlineData("(8110)0012345123456101")]
    [InlineData("(8110)10614141001234650110650")]
    [InlineData("(8110)10614141001234250117650")]
    [InlineData("(8110)106141410012342501106503101331")]
    [InlineData("(8110)106141410012342501106503110229")]
    [InlineData("(8110)1061414100123425011065031012314110101")]
    [InlineData("(8110)1061414100123425011065041101013101231")]
    [InlineData("(8110)106141410012342501106509300")]
    [InlineData("(8110)10614141001234250110650A")]
    [InlineData("(8110)1061414100123425011065051234567890123456789012345678901234567890123")]
    public void Gs1NorthAmericanCoupon_RejectsInvalidInput(string text)
        => Assert.Throws<ArgumentException>(() => SymbolEncoder.Encode(new Gs1NorthAmericanCouponSymbology(), text, new BarcodeOptions()));

    /// <summary>
    /// The guideline allows "a minimum of 2 segments per row", and a row holds whole segment pairs up to the 20 segments of section 5.5.2.3.2 of the GS1
    /// General Specifications.
    /// </summary>
    /// <param name="segments">The number of segments in a row.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(22)]
    public void Gs1NorthAmericanCoupon_RejectsInvalidSegments(int segments)
        => Assert.ThrowsAny<ArgumentException>(() => new Gs1NorthAmericanCouponSymbology(segments));
}
