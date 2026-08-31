// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Barcodes;

namespace SixLabors.ImageSharp.Drawing.Tests.Barcodes;

/// <summary>
/// The input contract every symbology shares. The per-symbology test classes cover the format rules of
/// their own standard; these cover what all of them owe a caller, so no symbology reports bad input by
/// crashing or by drawing a symbol that carries nothing.
/// </summary>
public class BarcodeInputValidationTests
{
    /// <summary>
    /// Null text is a caller error, and every symbology reports it the same way rather than dereferencing
    /// it and throwing <see cref="NullReferenceException"/> from somewhere inside the encoder.
    /// </summary>
    [Theory]
    [InlineData(typeof(Ean13Symbology))]
    [InlineData(typeof(Ean8Symbology))]
    [InlineData(typeof(Ean5Symbology))]
    [InlineData(typeof(Ean2Symbology))]
    [InlineData(typeof(UpcASymbology))]
    [InlineData(typeof(UpcESymbology))]
    [InlineData(typeof(IsbnSymbology))]
    [InlineData(typeof(IsmnSymbology))]
    [InlineData(typeof(IssnSymbology))]
    [InlineData(typeof(MandsSymbology))]
    [InlineData(typeof(Code128Symbology))]
    [InlineData(typeof(Gs1128Symbology))]
    [InlineData(typeof(Ean14Symbology))]
    [InlineData(typeof(Sscc18Symbology))]
    [InlineData(typeof(HibcCode128Symbology))]
    [InlineData(typeof(Code39Symbology))]
    [InlineData(typeof(Code39ExtendedSymbology))]
    [InlineData(typeof(HibcCode39Symbology))]
    public void RejectsNullText(Type symbologyType)
    {
        BarcodeSymbology symbology = (BarcodeSymbology)Activator.CreateInstance(symbologyType)!;
        Assert.Throws<ArgumentNullException>(() => symbology.Encode(null!, new BarcodeOptions()));
    }

    /// <summary>
    /// Empty text carries no data, and every symbology whose standard fixes a length or requires data
    /// rejects it. Code 128 is left out: its standard sets no minimum, and the reference implementation
    /// encodes an empty symbol as a start character, a check character and a stop character.
    /// </summary>
    [Theory]
    [InlineData(typeof(Ean13Symbology))]
    [InlineData(typeof(Ean8Symbology))]
    [InlineData(typeof(Ean5Symbology))]
    [InlineData(typeof(Ean2Symbology))]
    [InlineData(typeof(UpcASymbology))]
    [InlineData(typeof(UpcESymbology))]
    [InlineData(typeof(IsbnSymbology))]
    [InlineData(typeof(IsmnSymbology))]
    [InlineData(typeof(IssnSymbology))]
    [InlineData(typeof(MandsSymbology))]
    [InlineData(typeof(Gs1128Symbology))]
    [InlineData(typeof(Ean14Symbology))]
    [InlineData(typeof(Sscc18Symbology))]
    [InlineData(typeof(HibcCode128Symbology))]
    [InlineData(typeof(Code39Symbology))]
    [InlineData(typeof(Code39ExtendedSymbology))]
    [InlineData(typeof(HibcCode39Symbology))]
    public void RejectsEmptyText(Type symbologyType)
    {
        BarcodeSymbology symbology = (BarcodeSymbology)Activator.CreateInstance(symbologyType)!;
        Assert.ThrowsAny<ArgumentException>(() => symbology.Encode(string.Empty, new BarcodeOptions()));
    }

    /// <summary>
    /// A Code 128 symbol carries at most 500 characters, so longer input is rejected rather than encoded
    /// into a symbol no scanner will read.
    /// </summary>
    [Fact]
    public void Code128_RejectsDataBeyondTheMaximumLength()
        => Assert.ThrowsAny<ArgumentException>(
            () => new Code128Symbology().Encode(new string('A', 501), new BarcodeOptions()));
}
