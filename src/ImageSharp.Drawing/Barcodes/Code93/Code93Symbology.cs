// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The Code 93 symbology, which ANSI/AIM BC5-1995 defines. It carries the digits, the capital letters,
/// the space and the special characters <c>$ % + - . /</c>. Table 2 gives every character six elements,
/// three bars and three spaces. A symbol carries a start character, the data, two check characters and a
/// stop character. Each character takes nine modules, and the stop character takes ten.
/// <para>
/// The two check characters are modulo 47 and Section 2.6 counts them in the symbol length, so every
/// symbol carries them. The human readable interpretation shows the data characters.
/// </para>
/// </summary>
public sealed class Code93Symbology : BarcodeSymbology
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Code93Symbology"/> class.
    /// </summary>
    public Code93Symbology()
    {
    }

    /// <inheritdoc/>
    internal override BarcodeSymbol Encode(string text, BarcodeOptions options)
    {
        Guard.NotNull(text, nameof(text));

        Code93Encoder.Validate(text);

        return Code93Encoder.BuildSymbol(Code93Encoder.Encode(text), options.Font is null ? string.Empty : text, options);
    }
}
