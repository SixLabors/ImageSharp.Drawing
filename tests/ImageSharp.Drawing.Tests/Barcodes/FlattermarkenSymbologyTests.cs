// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Barcodes;

namespace SixLabors.ImageSharp.Drawing.Tests.Barcodes;

/// <summary>
/// Encoder tests for <see cref="FlattermarkenSymbology"/>. Each reference string is the alternating bar and space module widths, starting with a bar, from the
/// reference implementation's raw encoding API, joined with commas. The reference writes every window as a bar, a space, a bar and a space, some of zero width.
/// The symbol keeps a zero width bar only at its ends, so the test merges the spaces around every other zero width bar before it compares.
/// </summary>
public class FlattermarkenSymbologyTests
{
    [Theory]
    [InlineData("1", "0,0,1,8")]
    [InlineData("9", "0,8,1,0")]
    [InlineData("0", "0,9,0,0")]
    [InlineData("10", "0,0,1,8,0,9,0,0")]
    [InlineData("123", "0,0,1,8,0,1,1,7,0,2,1,6")]
    [InlineData("11099", "0,0,1,8,0,0,1,8,0,9,0,0,0,8,1,0,0,8,1,0")]
    public void MatchesReferenceRuns(string text, string reference)
        => Assert.Equal(
            Merge(reference),
            SymbolEncoder.Read(new FlattermarkenSymbology(), text, new BarcodeOptions(), static (ref symbol) => string.Join(",", symbol.Runs.ToArray())));

    /// <summary>
    /// Every digit takes a window of nine modules, so the symbol is nine modules per digit wide whatever the digits, with no quiet zone.
    /// </summary>
    /// <param name="text">The digits to encode.</param>
    [Theory]
    [InlineData("1")]
    [InlineData("0")]
    [InlineData("11099")]
    [InlineData("0123456789")]
    public void SpansNineModulesPerDigit(string text)
        => SymbolEncoder.Encode(new FlattermarkenSymbology(), text, new BarcodeOptions(), (ref symbol) =>
        {
            Assert.Equal(9 * text.Length, SymbolEncoder.RunSum(ref symbol));
            Assert.Equal(0, symbol.LeadingQuietZone);
            Assert.Equal(0, symbol.TrailingQuietZone);
        });

    /// <summary>
    /// The TEC-IT Barcode Reference gives a "Symbol height between 5 and 10mm" and a module width of 2 to 3 mm. The bars are 10 mm high at any module width:
    /// 5 modules at 2 mm and 10 / 3 modules at 3 mm.
    /// </summary>
    /// <param name="xDimension">The X dimension in millimetres.</param>
    [Theory]
    [InlineData(2.0)]
    [InlineData(3.0)]
    public void DrawsBarsTenMillimetresHigh(double xDimension)
    {
        BarcodeOptions options = new() { Dpi = SymbolEncoder.MillimetreDpi, XDimension = xDimension };
        SymbolEncoder.Encode(new FlattermarkenSymbology(), "11099", options, (ref symbol) =>
            Assert.All(symbol.BarHeights.ToArray(), height => Assert.Equal((float)(10 / xDimension), height, 1e-4F)));
    }

    [Fact]
    public void PrintsTheDigitsAsGiven()
    {
        BarcodeOptions options = new() { Font = BarcodeFonts.OcrB.CreateFont(12F) };
        string[] texts = SymbolEncoder.Read(new FlattermarkenSymbology(), "11099", options, SymbolEncoder.Texts);
        Assert.Equal("11099", Assert.Single(texts));
    }

    [Theory]
    [InlineData("")]
    [InlineData("12A")]
    [InlineData("1 2")]
    [InlineData("１２")]
    public void RejectsMalformedInput(string text)
        => Assert.ThrowsAny<ArgumentException>(() => SymbolEncoder.Encode(new FlattermarkenSymbology(), text, new BarcodeOptions()));

    private static string Merge(string reference)
    {
        int[] runs = Array.ConvertAll(reference.Split(','), int.Parse);

        // A leading zero width bar with no space after it is nothing at all, and the symbol starts with the next bar. One followed by a space starts the symbol
        // blank, which the zero width bar keeps.
        List<int> merged = runs[0] == 0 && runs[1] == 0 ? [] : [runs[0], runs[1]];
        for (int i = 2; i < runs.Length; i += 2)
        {
            int bar = runs[i];
            int space = i + 1 < runs.Length ? runs[i + 1] : 0;
            if (bar == 0 && i + 1 < runs.Length)
            {
                merged[^1] += space;
                continue;
            }

            merged.Add(bar);
            merged.Add(space);
        }

        // The reference ends every window with a space. A symbol ends on its last bar when that space is empty, and on a zero width bar after the space
        // otherwise.
        if (merged[^1] == 0)
        {
            merged.RemoveAt(merged.Count - 1);
        }
        else
        {
            merged.Add(0);
        }

        return string.Join(",", merged);
    }
}
