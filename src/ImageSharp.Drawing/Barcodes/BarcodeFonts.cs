// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.Fonts;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The house fonts shipped for barcode text, built clean-room from the dimensioned character drawings of
/// the OCR standards. Nothing selects these automatically: assign them to
/// <see cref="BarcodeOptions.Font"/> or <see cref="BarcodeOptions.CaptionFont"/> at the required size.
/// </summary>
public static class BarcodeFonts
{
    private static readonly Lazy<FontFamily> OcrAFamily = new(() => Load(OcrAFontData.Bytes));

    /// <summary>
    /// Gets the SixLabors OCR-A font family, built from the character drawings of FIPS PUB 32 (1974).
    /// </summary>
    public static FontFamily OcrA => OcrAFamily.Value;

    private static FontFamily Load(ReadOnlySpan<byte> data)
    {
        FontCollection collection = new();
        using MemoryStream stream = new(data.ToArray());
        return collection.Add(stream);
    }
}
