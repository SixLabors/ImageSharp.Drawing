// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.Fonts;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The house fonts for barcode text. They are built clean-room from the dimensioned character drawings of
/// the OCR standards. No symbology selects them. To use one, set it on
/// <see cref="BarcodeOptions.Font"/> or <see cref="BarcodeOptions.CaptionFont"/> at the size you need.
/// </summary>
public static class BarcodeFonts
{
    private static readonly Lazy<FontFamily> OcrAFamily = new(() => Load(OcrAFontData.Bytes));

    private static readonly Lazy<FontFamily> OcrBFamily = new(() => Load(OcrBFontData.Bytes));

    /// <summary>
    /// Gets the SixLabors OCR-A font family, built from the character drawings of FIPS PUB 32 (1974).
    /// </summary>
    public static FontFamily OcrA => OcrAFamily.Value;

    /// <summary>
    /// Gets the SixLabors OCR-B font family. It holds the barcode repertoire of the constant strokewidth
    /// OCR-B design: the digits, the capitals, the Code 39 symbols and the EAN quiet zone indicators.
    /// </summary>
    public static FontFamily OcrB => OcrBFamily.Value;

    private static FontFamily Load(ReadOnlySpan<byte> data)
    {
        FontCollection collection = new();
        using MemoryStream stream = new(data.ToArray());
        return collection.Add(stream);
    }
}
