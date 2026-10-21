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
    /// The default X dimension in millimetres of a linear or stacked symbol: 0.330. When the specification of a symbology gives no X dimension, the symbology
    /// uses this value. GS1 General Specifications release 26.0 gives this target X dimension to linear symbols in Table 5-44.
    /// </summary>
    public const double DefaultXDimension = 0.330;

    /// <summary>
    /// The default X dimension in millimetres of a 2D matrix symbol: 0.495. The specifications of Aztec Code, Code One, Data Matrix, DotCode, Han Xin Code,
    /// QR Code and Ultracode give no X dimension. Thus these symbologies use this value. GS1 General Specifications release 26.0 gives this target X
    /// dimension to 2D symbols in Table 5-45 and Table 5-46. The notes to those tables give the reason. Because of optical effects in image capture, the X
    /// dimension of a 2D symbol must be 1.5 times the X dimension of a linear symbol.
    /// </summary>
    public const double DefaultMatrixXDimension = 0.495;

    /// <summary>
    /// Initializes a new instance of the <see cref="BarcodeSymbology"/> class.
    /// </summary>
    private protected BarcodeSymbology()
    {
    }

    /// <summary>
    /// Validates the given text and encodes it into a device-independent symbol measured in modules. An implementation checks the text length against the
    /// maximum of the symbology before it sizes a buffer from the text.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    /// <param name="options">The options that control layout choices made during encoding.</param>
    /// <param name="symbol">The symbol to write. It is empty on entry.</param>
    /// <exception cref="ArgumentException">The text is not valid for the symbology.</exception>
    internal abstract void Encode(ReadOnlySpan<char> text, BarcodeOptions options, ref BarcodeSymbol symbol);

    /// <summary>
    /// Validates that the text is the ASCII digits 0 to 9 alone. The vectorized search finds the first other character. Only then does the method decode the
    /// code point there for the message.
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

    /// <summary>
    /// Calculates the modulo 11 check value over the digits. The weights start at 2 on the rightmost digit and go up by one for each digit to the left. After
    /// the maximum weight, the weight starts at 2 again. The check value lifts the weighted sum to the next multiple of eleven.
    /// </summary>
    /// <param name="digits">The digits the check value covers.</param>
    /// <param name="maximumWeight">The highest weight: 7 for the IBM MSI calculation, 9 for the NCR MSI calculation, 10 for ISBN-10 and 8 for ISSN.</param>
    /// <returns>The check value, 0 to 10.</returns>
    internal static int Modulo11(ReadOnlySpan<char> digits, int maximumWeight)
    {
        int sum = 0;
        int weight = 2;
        for (int i = digits.Length - 1; i >= 0; i--)
        {
            sum += weight * (digits[i] - '0');
            weight = weight == maximumWeight ? 2 : weight + 1;
        }

        return (11 - (sum % 11)) % 11;
    }
}
