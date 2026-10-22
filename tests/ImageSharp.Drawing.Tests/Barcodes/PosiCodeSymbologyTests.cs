// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Barcodes;

namespace SixLabors.ImageSharp.Drawing.Tests.Barcodes;

/// <summary>
/// Encoder tests for <see cref="PosiCodeSymbology"/>.
/// </summary>
public class PosiCodeSymbologyTests
{
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

    /// <summary>
    /// The human readable line is the text as given.
    /// </summary>
    [Fact]
    public void PrintsTheTextAsGiven()
    {
        BarcodeOptions options = new() { Font = BarcodeFonts.OcrB.CreateFont(12F) };
        string[] texts = SymbolEncoder.Read(new PosiCodeSymbology(), "ABC123", options, SymbolEncoder.Texts);

        Assert.Equal("ABC123", Assert.Single(texts));
    }

    /// <summary>
    /// Versions A and B carry the 256 values of ISO/IEC 8859-1. The Limited versions carry the digits, the capital letters, the hyphen and the full stop.
    /// The symbology rejects empty input and characters outside the set of the version.
    /// </summary>
    /// <param name="version">The version.</param>
    /// <param name="text">The text to encode.</param>
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
