// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// HIBC QR Code, a Health Industry Bar Code carried by a QR Code symbol. The symbol carries the flag character, the data and the modulo 43 check character.
/// <para>
/// When a font is set, that same string prints below the symbol between delimiters, and a check character that is a space prints as an underscore. Primary and
/// secondary data that the symbol joins with "/" print on two lines, broken after the "/".
/// </para>
/// </summary>
public sealed class HibcQrCodeSymbology : BarcodeSymbology
{
    /// <summary>
    /// Initializes a new instance of the <see cref="HibcQrCodeSymbology"/> class at error correction level M, calculating the check character from the data.
    /// The symbol takes the smallest version that holds the data.
    /// </summary>
    public HibcQrCodeSymbology()
        : this(QrErrorCorrectionLevel.M, false)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="HibcQrCodeSymbology"/> class at error correction level M. The symbol takes the smallest version that holds
    /// the data.
    /// </summary>
    /// <param name="validateCheckCharacter"> Whether the last character of the input is the check character, which the encoder validates against the data,
    /// rather than data the encoder appends a check character to. </param>
    public HibcQrCodeSymbology(bool validateCheckCharacter)
        : this(QrErrorCorrectionLevel.M, validateCheckCharacter)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="HibcQrCodeSymbology"/> class. The symbol takes the smallest version that holds the data.
    /// </summary>
    /// <param name="errorCorrectionLevel">The error correction level.</param>
    /// <param name="validateCheckCharacter"> Whether the last character of the input is the check character, which the encoder validates against the data,
    /// rather than data the encoder appends a check character to. </param>
    public HibcQrCodeSymbology(QrErrorCorrectionLevel errorCorrectionLevel, bool validateCheckCharacter)
    {
        Guard.MustBeBetweenOrEqualTo((int)errorCorrectionLevel, (int)QrErrorCorrectionLevel.L, (int)QrErrorCorrectionLevel.H, nameof(errorCorrectionLevel));
        this.ErrorCorrectionLevel = errorCorrectionLevel;
        this.ValidateCheckCharacter = validateCheckCharacter;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="HibcQrCodeSymbology"/> class with a fixed version.
    /// </summary>
    /// <param name="errorCorrectionLevel">The error correction level.</param>
    /// <param name="version">The version, 1 to 40.</param>
    /// <param name="validateCheckCharacter"> Whether the last character of the input is the check character, which the encoder validates against the data,
    /// rather than data the encoder appends a check character to. </param>
    public HibcQrCodeSymbology(QrErrorCorrectionLevel errorCorrectionLevel, int version, bool validateCheckCharacter)
        : this(errorCorrectionLevel, validateCheckCharacter)
    {
        Guard.MustBeBetweenOrEqualTo(version, 1, 40, nameof(version));
        this.Version = version;
    }

    /// <summary>
    /// Gets the error correction level.
    /// </summary>
    public QrErrorCorrectionLevel ErrorCorrectionLevel { get; }

    /// <summary>
    /// Gets the version, or <see langword="null"/> when the version follows from the data.
    /// </summary>
    public int? Version { get; }

    /// <summary>
    /// Gets a value indicating whether the last character of the input is the check character.
    /// </summary>
    public bool ValidateCheckCharacter { get; }

    /// <inheritdoc/>
    internal override void Encode(ReadOnlySpan<char> text, BarcodeOptions options, ref BarcodeSymbol symbol)
    {
        InlineArray64<char> bufferStorage = default;
        int capacity = HibcData.PreparedCapacity(text.Length);
        ValueStringBuilder encoded = capacity <= HibcData.InlineBufferLength ? new(bufferStorage) : new(capacity);
        try
        {
            HibcData.Prepare(text, this.ValidateCheckCharacter, ref encoded);
            QrEncoder.BuildSymbol(
                encoded.AsSpan(),
                false,
                QrFormat.Full,
                this.ErrorCorrectionLevel,
                this.Version ?? 0,
                options.XDimension ?? PointXDimension,
                ref symbol);

            if (options.Font is not null)
            {
                HibcData.AddMatrixReadable(encoded.AsSpan(), ref symbol);
            }
        }
        finally
        {
            encoded.Dispose();
        }
    }
}
