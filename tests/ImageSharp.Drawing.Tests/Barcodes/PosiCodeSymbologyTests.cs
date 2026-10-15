// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Barcodes;

namespace SixLabors.ImageSharp.Drawing.Tests.Barcodes;

/// <summary>
/// Encoder tests for <see cref="PosiCodeSymbology"/>. Each expected run string is the alternating bar
/// and space module widths, starting with a bar, from the reference implementation's raw encoding API,
/// joined with commas because a space can be wider than nine modules. The reference ends on the last bar
/// of the stop character, so the strings compare directly.
/// </summary>
public class PosiCodeSymbologyTests
{
    /// <summary>
    /// Gets the cases of <see cref="MatchesReferenceRuns"/>.
    /// </summary>
    public static TheoryData<PosiCodeVersion, string, string> MatchesReferenceRunsData { get; } = new()
    {
        {
            PosiCodeVersion.A,
            "ABC123",
            "1,12,1,1,1,1,1,2,1,8,1,1,1,1,1,7,1,2,1,1,1,6,1,3,1,1,1,3,1,2,1,2,1,2,1,3,1,2,1,1,1,4,1,2,1,1,1,1,1,5,1,1,1,5,1,1,1,1,1,1,1,1,1,1,1,11,1"
        },
        {
            PosiCodeVersion.B,
            "ABC123",
            "1,12,1,2,1,3,1,2,1,9,1,2,1,2,1,8,1,3,1,2,1,7,1,4,1,2,1,4,1,3,1,3,1,3,1,4,1,3,1,2,1,5,1,3,1,2,1,2,1,6,1,2,1,6,1,2,1,2,1,2,1,2,1,2,1,12,1"
        },
        { PosiCodeVersion.LimitedA, "ABC123", "1,5,1,1,1,1,1,7,1,1,1,1,1,6,1,2,1,1,1,5,1,3,1,1,1,1,1,3,1,2,1,1,1,2,1,3,1,1,1,1,1,4,1,1,1,5,1,1,1,2,1,4,1,1,1" },
        { PosiCodeVersion.LimitedB, "ABC123", "1,4,1,2,1,2,1,8,1,2,1,2,1,7,1,3,1,2,1,6,1,4,1,2,1,2,1,4,1,3,1,2,1,3,1,4,1,2,1,2,1,5,1,2,1,6,1,2,1,3,1,5,1,2,1" },
        { PosiCodeVersion.A, "A", "1,12,1,1,1,1,1,2,1,8,1,1,1,1,1,2,1,5,1,1,1,2,1,3,1,1,1,1,1,1,1,1,1,1,1,11,1" },
        { PosiCodeVersion.A, "abc", "1,12,1,1,1,1,1,2,1,2,1,1,1,7,1,8,1,1,1,1,1,7,1,2,1,1,1,6,1,3,1,1,1,1,1,3,1,5,1,3,1,1,1,1,1,1,1,1,1,1,1,1,1,11,1" },
        { PosiCodeVersion.A, "aB1", "1,12,1,1,1,1,1,2,1,1,1,2,1,7,1,8,1,1,1,1,1,7,1,2,1,1,1,3,1,2,1,2,1,2,1,4,1,3,1,2,1,2,1,1,1,1,1,1,1,1,1,1,1,11,1" },
        {
            PosiCodeVersion.A,
            "%$/+ -.",
            "1,12,1,1,1,1,1,2,1,1,1,3,1,6,1,1,1,4,1,5,1,3,1,1,1,6,1,2,1,2,1,6,1,2,1,3,1,5,1,4,1,1,1,5,1,3,1,2,1,5,1,2,1,6,1,1,1,2,1,2,1,1,1,1,1,1,1,1,1,1" +
                ",1,11,1"
        },
        {
            PosiCodeVersion.A,
            "\x01\x1B*,:",
            "1,12,1,1,1,1,1,2,1,1,1,1,1,8,1,8,1,1,1,1,1,1,1,1,1,8,1,3,1,2,1,2,1,1,1,1,1,8,1,1,1,4,1,5,1,1,1,1,1,8,1,3,1,1,1,6,1,1,1,1,1,8,1,2,1,2,1,6,1,1" +
                ",1,2,1,1,1,1,1,7,1,2,1,1,1,1,1,1,1,1,1,11,1"
        },
        {
            PosiCodeVersion.A,
            "AÄB",
            "1,12,1,1,1,1,1,2,1,8,1,1,1,1,1,1,1,1,1,8,1,1,1,1,1,8,1,5,1,4,1,1,1,7,1,2,1,1,1,8,1,1,1,1,1,2,1,1,1,1,1,1,1,1,1,1,1,1,1,11,1"
        },
        {
            PosiCodeVersion.A,
            "ÄÖÜßäö",
            "1,12,1,1,1,1,1,2,1,1,1,1,1,8,1,1,1,1,1,8,1,1,1,1,1,8,1,1,1,1,1,8,1,5,1,4,1,1,1,5,1,1,1,4,1,2,1,1,1,7,1,1,1,2,1,4,1,4,1,1,1,5,1,5,1,4,1,1,1,5" +
                ",1,1,1,4,1,1,1,1,1,1,1,7,1,1,1,3,1,1,1,1,1,1,1,1,1,11,1"
        },
        {
            PosiCodeVersion.A,
            "ABÄÖÜ",
            "1,12,1,1,1,1,1,2,1,8,1,1,1,1,1,7,1,2,1,1,1,1,1,1,1,8,1,1,1,1,1,8,1,1,1,1,1,8,1,1,1,1,1,8,1,5,1,4,1,1,1,5,1,1,1,4,1,2,1,1,1,7,1,1,1,2,1,4,1,4" +
                ",1,3,1,1,1,1,1,2,1,3,1,1,1,1,1,1,1,1,1,11,1"
        },
        {
            PosiCodeVersion.LimitedA,
            "0123456789",
            "1,5,1,1,1,1,1,1,1,4,1,1,1,1,1,3,1,2,1,1,1,2,1,3,1,1,1,1,1,4,1,2,1,3,1,1,1,2,1,2,1,2,1,2,1,1,1,3,1,4,1,1,1,1,1,3,1,2,1,1,1,3,1,1,1,2,1,2,1,5," +
                "1,2,1,1,1,3,1,1,1"
        },
        { PosiCodeVersion.LimitedB, "A-.9", "1,4,1,2,1,2,1,8,1,2,1,2,1,2,1,3,1,7,1,2,1,2,1,8,1,4,1,2,1,3,1,3,1,4,1,6,1,3,1,2,1,2,1" },
    };

    /// <summary>
    /// One vector per version for the same data, then the paths through the character sets of the
    /// standard versions: a latch to the small letters, a shift for one small letter, the symbols of
    /// set 0, shifts to the control characters of set 2, and the function 4 switching of characters at
    /// or above 128, both as single shifts and as a mode change for a run of five.
    /// </summary>
    /// <param name="version">The version.</param>
    /// <param name="text">The text to encode.</param>
    /// <param name="reference">The reference run widths, comma separated.</param>
    [Theory]
    [MemberData(nameof(MatchesReferenceRunsData))]
    public void MatchesReferenceRuns(PosiCodeVersion version, string text, string reference)
        => Assert.Equal(
            reference,
            SymbolEncoder.Read(new PosiCodeSymbology(version), text, new BarcodeOptions(), static (ref symbol) => string.Join(",", symbol.Runs.ToArray())));

    /// <summary>
    /// The quiet zone is 12G on either side, and 13G for Limited PosiCode B.
    /// </summary>
    /// <param name="version">The version.</param>
    /// <param name="expected">The quiet zone in modules.</param>
    [Theory]
    [InlineData(PosiCodeVersion.A, 12)]
    [InlineData(PosiCodeVersion.B, 12)]
    [InlineData(PosiCodeVersion.LimitedA, 12)]
    [InlineData(PosiCodeVersion.LimitedB, 13)]
    public void KeepsTheQuietZoneOfTheVersion(PosiCodeVersion version, int expected)
        => SymbolEncoder.Encode(new PosiCodeSymbology(version), "ABC123", new BarcodeOptions(), (ref symbol) =>
        {
            Assert.Equal(expected, symbol.LeadingQuietZone);
            Assert.Equal(expected, symbol.TrailingQuietZone);
        });

    [Fact]
    public void PrintsTheTextAsGiven()
    {
        BarcodeOptions options = new() { Font = BarcodeFonts.OcrB.CreateFont(12F) };
        string[] texts = SymbolEncoder.Read(new PosiCodeSymbology(), "ABC123", options, SymbolEncoder.Texts);
        Assert.Equal("ABC123", Assert.Single(texts));
    }

    [Theory]
    [InlineData(PosiCodeVersion.A, "")]
    [InlineData(PosiCodeVersion.A, "Ā")]
    [InlineData(PosiCodeVersion.A, "１２")]
    [InlineData(PosiCodeVersion.LimitedA, "ab")]
    [InlineData(PosiCodeVersion.LimitedA, "A B")]
    [InlineData(PosiCodeVersion.LimitedB, "A$")]
    public void RejectsMalformedInput(PosiCodeVersion version, string text)
        => Assert.ThrowsAny<ArgumentException>(() => SymbolEncoder.Encode(new PosiCodeSymbology(version), text, new BarcodeOptions()));
}
