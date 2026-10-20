// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Barcodes;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace SixLabors.ImageSharp.Drawing.Tests.Barcodes;

/// <summary>
/// Encoder tests for <see cref="Itf14Symbology"/>. Each expected run string is the alternating bar and space module widths, starting with a bar, taken verbatim
/// from an independent reference implementation through its raw encoding API. That implementation draws a wide element two modules wide and emits a narrow
/// space after the stop pattern. This library draws the 2.5:1 target ratio of the symbol height table of section 5.12.3.2 of the GS1 General Specifications
/// in runs of one pixel. At 2 pixels a module a run is half a module, so the test turns every narrow element into 2 run units and every wide element into 5,
/// and drops the trailing space, before it compares. The target X of 0.495 mm of Table 5-47 is 1.87 pixels at the default 96 dots per inch and draws at 2
/// pixels.
/// </summary>
public class Itf14SymbologyTests
{
    /// <summary>
    /// The reference runs of 15400141288763, the number of Figure 5-32.
    /// </summary>
    private const string Figure532 = "111122111211211111221221121121211212112111221221111221211111221212222111112111";

    /// <summary>
    /// The target X of Table 5-47 in millimetres, which draws at exactly 495 pixels at <see cref="SymbolEncoder.MicrometreDpi"/>.
    /// </summary>
    private const double TargetX = 0.495;

    /// <summary>
    /// The bearer bar thickness in modules at the target X: the 4.83 millimetres of section 5.3.2.4.
    /// </summary>
    private const float BearerBarThickness = (float)(4.83 / TargetX);

    /// <summary>
    /// The bar height in modules at the target X: the 31.75 millimetres of the symbol height table of section 5.12.3.2.
    /// </summary>
    private const float BarHeight = (float)(31.75 / TargetX);

    /// <summary>
    /// Figure 5-32 of the GS1 General Specifications shows the number 15400141288763, whose check digit section 7.9 calculates as 3. Thirteen digits take the
    /// calculated check digit, fourteen carry their own, and spaces in the input stay out of the symbol, as section 4.14 rule 2.a.iv requires.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    /// <param name="reference">The reference run string.</param>
    [Theory]
    [InlineData("1540014128876", Figure532)]
    [InlineData("15400141288763", Figure532)]
    [InlineData("1 54 00141 28876 3", Figure532)]
    [InlineData("0123456789012", "111112112121121222111121121122112111212112122112112211121121211212211112212111")]
    public void MatchesReferenceRuns(string text, string reference)
    {
        Assert.Equal(Widen(reference), SymbolEncoder.Read(new Itf14Symbology(), text, new BarcodeOptions(), SymbolEncoder.RunString));
    }

    /// <summary>
    /// A supplied check digit that is not the section 7.9 check digit of the data throws.
    /// </summary>
    [Fact]
    public void RejectsAWrongCheckDigit()
        => Assert.Throws<ArgumentException>(() => Encode("15400141288764"));

    /// <summary>
    /// Figure 5-32 prints the number both as "1 54 00141 28876 3" and as "15400141288763". Section 4.14 rule 2.a.v permits the spaces, so the printed line
    /// keeps the spaces of the input, and a calculated check digit follows a space only when the input has spaces. Section 5.3.2.2: "A minimum space of 1.02
    /// millimetre (0.040 inch) between the bottom line of the bearer bar and the top of the human readable characters is required", which is 1.02 / 0.495
    /// modules at the target X.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    /// <param name="expected">The printed line.</param>
    [Theory]
    [InlineData("1540014128876", "15400141288763")]
    [InlineData("15400141288763", "15400141288763")]
    [InlineData("1 54 00141 28876", "1 54 00141 28876 3")]
    [InlineData("1 54 00141 28876 3", "1 54 00141 28876 3")]
    public void PrintsTheFourteenDigits(string text, string expected)
    {
        BarcodeOptions options = new() { Dpi = SymbolEncoder.MicrometreDpi, Font = BarcodeFonts.OcrB.CreateFont(12F) };

        SymbolEncoder.Encode(new Itf14Symbology(), text, options, (ref symbol) =>
        {
            BarcodeTextPlacement placement = Assert.Single(symbol.Text.ToArray());

            Assert.Equal(expected, symbol.TextOf(placement).ToString());
            Assert.Equal(symbol.HeightInModules + (float)(1.02 / TargetX), placement.TextEdge, new ApproximateFloatComparer(1e-4F));
        });
    }

    /// <summary>
    /// Section 5.3.2.4 of the GS1 General Specifications gives the bearer bar a thickness of 4.83 millimetres, which is 4.83 / 0.495 modules at the target X of
    /// Table 5-47, and butts it against the top and bottom of the bars. The bars therefore start one thickness below the symbol top, and the symbol height is
    /// the 31.75 millimetre bar height of section 5.12.3.2 plus two thicknesses.
    /// </summary>
    [Fact]
    public void FramesTheBarsWithTheBearerBarOfSectionFiveThreeTwoFour()
        => SymbolEncoder.Encode(new Itf14Symbology(), "15400141288763", new BarcodeOptions { Dpi = SymbolEncoder.MicrometreDpi }, static (ref symbol) =>
        {
            ApproximateFloatComparer comparer = new(1e-3F);

            Assert.Equal(BearerBarThickness, symbol.FrameThickness, comparer);
            Assert.All(symbol.BarTops.ToArray(), top => Assert.Equal(BearerBarThickness, top, comparer));
            Assert.Equal(BarHeight + BearerBarThickness + BearerBarThickness, symbol.HeightInModules, comparer);
        });

    /// <summary>
    /// The symbol height table of section 5.12.3.2 gives a bar height of "always 31.75 millimetres", section 5.3.2.2 a space of 1.02 millimetres before the
    /// text, and section 5.3.2.4 a bearer bar of 4.83 millimetres. All three are in millimetres, so at the largest X of Table 5-47, 1.016 millimetres, they
    /// take fewer modules than at the target X and keep their millimetre sizes.
    /// </summary>
    [Fact]
    public void KeepsTheMillimetreDimensionsAtAnotherXDimension()
    {
        BarcodeOptions options = new() { Dpi = SymbolEncoder.MicrometreDpi, XDimension = 1.016, Font = BarcodeFonts.OcrB.CreateFont(12F) };

        SymbolEncoder.Encode(new Itf14Symbology(), "15400141288763", options, static (ref symbol) =>
        {
            ApproximateFloatComparer comparer = new(1e-3F);
            float bearerBar = (float)(4.83 / 1.016);

            Assert.Equal(bearerBar, symbol.FrameThickness, comparer);
            Assert.Equal((float)(31.75 / 1.016) + bearerBar + bearerBar, symbol.HeightInModules, comparer);
            Assert.Equal(symbol.HeightInModules + (float)(1.02 / 1.016), Assert.Single(symbol.Text.ToArray()).TextEdge, comparer);
        });
    }

    /// <summary>
    /// Section 5.3.2.2 of the GS1 General Specifications measures a symbol as <c>(P(4N+6)+N+6)X</c> before its quiet zones, where P is the seven character
    /// pairs and N the wide to narrow ratio, the 2.5 target of the symbol height table of section 5.12.3.2, at 2 pixels a module.
    /// </summary>
    [Fact]
    public void SpansTheWidthOfSectionFiveThreeTwoTwo()
        => SymbolEncoder.Encode(
            new Itf14Symbology(),
            "15400141288763",
            new BarcodeOptions(),
            static (ref symbol) => Assert.Equal((7 * ((4 * 2.5F) + 6)) + 2.5F + 6, symbol.WidthInModules));

    /// <summary>
    /// Every element draws at a whole number of pixels. At 3 pixels a module the wide element of 2.5 modules rounds half up to 8 pixels, a ratio of 8:3,
    /// inside the range of 2.25:1 to 3.0:1 of section 5.3.2.2, so the symbol is (P(4N+6)+N+6)X with N = 8 / 3. At 75 dots per inch an X of 0.04 inch,
    /// 1.016 mm, the largest X of Table 5-47, is 3 pixels.
    /// </summary>
    [Fact]
    public void RoundsTheWideElementHalfUpToAWholePixel()
        => SymbolEncoder.Encode(new Itf14Symbology(), "15400141288763", new BarcodeOptions { Dpi = 75F, XDimension = 1.016 }, static (ref symbol) =>
        {
            Assert.Equal(1F / 3F, symbol.RunUnit);
            Assert.All(symbol.Runs.ToArray(), run => Assert.True(run is 3 or 8, $"A run of {run} pixels."));
            Assert.Equal((7 * ((4 * (8F / 3F)) + 6)) + (8F / 3F) + 6, symbol.WidthInModules, 1e-4F);
        });

    /// <summary>
    /// Section 5.3.2.2 of the GS1 General Specifications: "The minimum width of each Quiet Zone is 10X", and section 5.3.2.4 runs the bearer bar around the
    /// quiet zones. Along a pixel row through the bars, the light run between the bearer bar and the first bar and the one between the last bar and the bearer
    /// bar are each at least the 10 modules of the module in pixels. The outer edges of the bearer bar snap outward to fill the measured area, so a quiet zone
    /// can hold one more pixel. At 144.6 dots per inch the 4.83 mm bearer bar is 27.5 pixels and the target X of 0.495 mm is 2.82 pixels, which draws at 3.
    /// </summary>
    /// <param name="dpi">The resolution.</param>
    /// <param name="originX">The horizontal position of the symbol.</param>
    [Theory]
    [InlineData(144F, 0F)]
    [InlineData(144.6F, 0F)]
    [InlineData(144.6F, 0.3F)]
    [InlineData(150F, 0.5F)]
    [InlineData(216F, 0.7F)]
    public void KeepsTenModulesOfQuietZoneInsideTheBearerBar(float dpi, float originX)
    {
        BarcodeOptions options = new() { Dpi = dpi };
        Itf14Symbology symbology = new();
        RectangleF bounds = BarcodeMeasurer.MeasureRenderableBounds(symbology, options, new PointF(originX, 0F), "15400141288763");

        using Image<L8> image = new((int)MathF.Ceiling(bounds.Right) + 2, (int)MathF.Ceiling(bounds.Bottom) + 2, new L8(255));
        image.Mutate(x => x.Paint(canvas => canvas.DrawBarcode(symbology, options, new PointF(originX, 0F), "15400141288763")));

        // A row a quarter of the way down the symbol crosses the vertical sections of the bearer bar, the quiet zones and the bars. Without the light border
        // on each side the runs are the bearer bar, the leading quiet zone, the bars, the trailing quiet zone and the bearer bar.
        int y = (int)(bounds.Top + (bounds.Height * 0.25F));
        List<(bool Dark, int Width)> runs = [];

        for (int x = 0; x < image.Width; x++)
        {
            bool dark = image[x, y].PackedValue < 128;

            if (runs.Count > 0 && runs[^1].Dark == dark)
            {
                runs[^1] = (dark, runs[^1].Width + 1);
            }
            else
            {
                runs.Add((dark, 1));
            }
        }

        if (!runs[0].Dark)
        {
            runs.RemoveAt(0);
        }

        if (!runs[^1].Dark)
        {
            runs.RemoveAt(runs.Count - 1);
        }

        float module = MathF.Round((float)TargetX / 25.4F * dpi);

        Assert.InRange((float)runs[1].Width, 10F * module, (10F * module) + 1F);
        Assert.InRange((float)runs[^2].Width, 10F * module, (10F * module) + 1F);
    }

    /// <summary>
    /// ITF-14 carries thirteen digits and an optional check digit. The symbology rejects other lengths, a letter and full width digits.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    [Theory]
    [InlineData("")]
    [InlineData("12345")]
    [InlineData("123456789012345")]
    [InlineData("154001412887A")]
    [InlineData("１５４００１４１２８８７６")]
    public void RejectsMalformedInput(string text)
        => Assert.ThrowsAny<ArgumentException>(() => Encode(text));

    /// <summary>
    /// Encodes the text with the default options.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    private static void Encode(string text)
        => SymbolEncoder.Encode(new Itf14Symbology(), text, new BarcodeOptions());

    /// <summary>
    /// Returns the reference runs in run units of half a module: a narrow element of 1 module is 2 units and a wide element of 2 modules becomes the 5 units of
    /// the 2.5:1 ratio. The method drops the trailing space of the reference.
    /// </summary>
    /// <param name="reference">The reference run string.</param>
    /// <returns>The runs in run units.</returns>
    private static string Widen(string reference)
        => reference[..^1].Replace('2', '5').Replace('1', '2');
}
