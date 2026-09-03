// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Barcodes;

namespace SixLabors.ImageSharp.Drawing.Tests.Barcodes;

/// <summary>
/// Encoder tests for <see cref="FlattermarkenSymbology"/>. Each reference string is the alternating bar
/// and space module widths, starting with a bar, from the reference implementation's raw encoding API,
/// joined with commas. The reference writes every window as a bar, a space, a bar and a space, some of
/// zero width. The symbol keeps a zero width bar only at its ends, so the test merges the spaces around
/// every other zero width bar before it compares.
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
        => Assert.Equal(Merge(reference), string.Join(",", Encode(text).RunWidths));

    /// <summary>
    /// Every digit takes a window of nine modules, so the symbol is nine modules per digit wide whatever
    /// the digits, with no quiet zone.
    /// </summary>
    /// <param name="text">The digits to encode.</param>
    [Theory]
    [InlineData("1")]
    [InlineData("0")]
    [InlineData("11099")]
    [InlineData("0123456789")]
    public void SpansNineModulesPerDigit(string text)
    {
        LinearBarcodeSymbol symbol = Encode(text);
        int widthInModules = 0;
        foreach (int run in symbol.RunWidths)
        {
            widthInModules += run;
        }

        Assert.Equal(9 * text.Length, widthInModules);
        Assert.Equal(0, symbol.LeadingQuietZone);
        Assert.Equal(0, symbol.TrailingQuietZone);
    }

    [Fact]
    public void PrintsTheDigitsAsGiven()
    {
        BarcodeOptions options = new() { Font = BarcodeFonts.OcrB.CreateFont(12F) };
        LinearBarcodeSymbol symbol = (LinearBarcodeSymbol)new FlattermarkenSymbology().Encode("11099", options);
        Assert.Equal("11099", Assert.Single(symbol.Text).Text);
    }

    [Theory]
    [InlineData("")]
    [InlineData("12A")]
    [InlineData("1 2")]
    [InlineData("１２")]
    public void RejectsMalformedInput(string text)
        => Assert.ThrowsAny<ArgumentException>(() => Encode(text));

    private static LinearBarcodeSymbol Encode(string text)
        => (LinearBarcodeSymbol)new FlattermarkenSymbology().Encode(text, new BarcodeOptions());

    private static string Merge(string reference)
    {
        int[] runs = Array.ConvertAll(reference.Split(','), int.Parse);

        // A leading zero width bar with no space after it is nothing at all, and the symbol starts with
        // the next bar. One followed by a space starts the symbol blank, which the zero width bar keeps.
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

        // The reference ends every window with a space. A symbol ends on its last bar when that space
        // is empty, and on a zero width bar after the space otherwise.
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
