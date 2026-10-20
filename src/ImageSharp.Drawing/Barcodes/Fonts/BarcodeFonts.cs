// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.Fonts;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The house fonts for barcode text. They are built clean-room from the dimensioned character drawings of the OCR standards. No symbology selects them. To use
/// one, set it on <see cref="BarcodeOptions.Font"/> or <see cref="BarcodeOptions.CaptionFont"/> at the size you need.
/// </summary>
public static class BarcodeFonts
{
    /// <summary>
    /// Gets the SixLabors OCR-A font family, built from the character drawings of FIPS PUB 32 (1974).
    /// </summary>
    public static FontFamily OcrA => OcrAFamily.Value;

    /// <summary>
    /// Gets the SixLabors OCR-B font family. It holds the barcode repertoire of the constant strokewidth OCR-B design: the digits, the capitals, the Code 39
    /// symbols and the EAN quiet zone indicators.
    /// </summary>
    public static FontFamily OcrB => OcrBFamily.Value;

    /// <summary>
    /// Loads a font family from the bytes of a font file.
    /// </summary>
    /// <param name="data">The bytes of the font file.</param>
    /// <returns>The font family.</returns>
    private static FontFamily Load(ReadOnlySpan<byte> data)
    {
        FontCollection collection = new();
        using MemoryStream stream = new(data.ToArray());
        return collection.Add(stream);
    }

    /// <summary>
    /// Holds the OCR-A family. Each family has its own holder type, so only code that reads <see cref="OcrA"/> references the OCR-A font data, and the
    /// trimmer can remove the data of a family that the application does not use.
    /// </summary>
    private static class OcrAFamily
    {
        /// <summary>
        /// The OCR-A family, loaded on first use.
        /// </summary>
        private static readonly Lazy<FontFamily> Family = new(() => Load(OcrAFontData.Bytes));

        /// <summary>
        /// Gets the OCR-A family.
        /// </summary>
        public static FontFamily Value => Family.Value;
    }

    /// <summary>
    /// Holds the OCR-B family. Each family has its own holder type, so only code that reads <see cref="OcrB"/> references the OCR-B font data, and the
    /// trimmer can remove the data of a family that the application does not use.
    /// </summary>
    private static class OcrBFamily
    {
        /// <summary>
        /// The OCR-B family, loaded on first use.
        /// </summary>
        private static readonly Lazy<FontFamily> Family = new(() => Load(OcrBFontData.Bytes));

        /// <summary>
        /// Gets the OCR-B family.
        /// </summary>
        public static FontFamily Value => Family.Value;
    }
}
