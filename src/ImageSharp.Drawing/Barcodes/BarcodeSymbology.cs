// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// A barcode symbology: one machine readable encoding scheme. A symbology validates the input and applies the check character rules of its specification. It
/// then encodes the input into a symbol. The canvas draws that symbol through
/// <see cref="Processing.DrawingCanvas.DrawBarcode(BarcodeSymbology, BarcodeOptions, PointF, ReadOnlySpan{char})"/>.
/// </summary>
public abstract class BarcodeSymbology
{
    /// <summary>
    /// The X dimension in millimetres of a symbology whose specification gives none: 2 pixels at the default 96 dots per inch, 2 x 25.4 / 96.
    /// </summary>
    public const double DefaultXDimension = 2 * 25.4 / 96;

    /// <summary>
    /// The X dimension in millimetres of an Aztec Code, Code One, Data Matrix, DotCode, Han Xin Code, QR Code or Ultracode symbol whose specification gives
    /// none: twice <see cref="DefaultXDimension"/>, 4 pixels at the default 96 dots per inch.
    /// </summary>
    public const double DefaultMatrixXDimension = 2 * DefaultXDimension;

    /// <summary>
    /// Initializes a new instance of the <see cref="BarcodeSymbology"/> class.
    /// </summary>
    private protected BarcodeSymbology()
    {
    }

    /// <summary>
    /// Validates the given text and encodes it into a device-independent symbol measured in modules. The text length is checked against the maximum of the
    /// symbology before any buffer is sized from it.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    /// <param name="options">The options that control layout choices made during encoding.</param>
    /// <param name="symbol">The symbol to write. It is empty on entry.</param>
    /// <exception cref="ArgumentException">The text is not valid for the symbology.</exception>
    internal abstract void Encode(ReadOnlySpan<char> text, BarcodeOptions options, ref BarcodeSymbol symbol);

    /// <summary>
    /// Validates that the text is the ASCII digits 0 to 9 alone. The vectorized search finds the first other character, and only then is the code point there
    /// decoded for the message.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <exception cref="ArgumentException">The text has a character that is not a digit.</exception>
    internal static void ValidateDigits(ReadOnlySpan<char> text)
    {
        int index = text.IndexOfAnyExceptInRange('0', '9');
        if (index >= 0)
        {
            throw new ArgumentException($"Only the digits 0 to 9 are accepted; got {text.CodePointAt(index).ToDisplayString()}.", nameof(text));
        }
    }
}
