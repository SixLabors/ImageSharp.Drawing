// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// GS1 QR Code, section 5.7 of the GS1 General Specifications: a QR Code symbol that carries GS1 element
/// strings. The symbol is in FNC1 first position mode, and an FNC1 character separates an element string of
/// variable length from the next one.
/// <para>
/// The input is the element string syntax the standard prints: an Application Identifier in parentheses,
/// then its data, repeated. For example, <c>(01)09521234543213(17)120508(10)ABCD1234</c>. The symbol does
/// not carry the parentheses.
/// </para>
/// <para>
/// When a font is set, the element strings print below the symbol, one on each line, with the Application
/// Identifiers in parentheses. An element string of Application Identifier (8200) does not print.
/// </para>
/// </summary>
public sealed class Gs1QrCodeSymbology : BarcodeSymbology
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Gs1QrCodeSymbology"/> class at error correction level M.
    /// The symbol takes the smallest version that holds the data.
    /// </summary>
    public Gs1QrCodeSymbology()
        : this(QrErrorCorrectionLevel.M)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Gs1QrCodeSymbology"/> class. The symbol takes the
    /// smallest version that holds the data.
    /// </summary>
    /// <param name="errorCorrectionLevel">The error correction level.</param>
    public Gs1QrCodeSymbology(QrErrorCorrectionLevel errorCorrectionLevel)
    {
        Guard.MustBeBetweenOrEqualTo((int)errorCorrectionLevel, (int)QrErrorCorrectionLevel.L, (int)QrErrorCorrectionLevel.H, nameof(errorCorrectionLevel));
        this.ErrorCorrectionLevel = errorCorrectionLevel;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Gs1QrCodeSymbology"/> class with a fixed version.
    /// </summary>
    /// <param name="errorCorrectionLevel">The error correction level.</param>
    /// <param name="version">The version, 1 to 40.</param>
    public Gs1QrCodeSymbology(QrErrorCorrectionLevel errorCorrectionLevel, int version)
        : this(errorCorrectionLevel)
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

    /// <inheritdoc/>
    internal override void Encode(string text, BarcodeOptions options, ref BarcodeSymbol symbol)
    {
        Guard.NotNull(text, nameof(text));

        InlineArray64<char> bufferStorage = default;
        int capacity = Gs1Data.PreparedCapacity(text, QrEncoder.MaximumLength);
        ValueStringBuilder encoded = capacity <= Gs1Data.InlineBufferLength ? new(bufferStorage) : new(capacity);
        try
        {
            Gs1Data.Prepare(text, ref encoded);
            QrEncoder.BuildSymbol(
                encoded.AsSpan(),
                true,
                QrFormat.Full,
                this.ErrorCorrectionLevel,
                this.Version ?? 0,
                options.XDimension ?? PointXDimension,
                ref symbol);

            if (options.Font is not null)
            {
                Gs1Data.AddMatrixReadable(text, options.Font, ref symbol);
            }
        }
        finally
        {
            encoded.Dispose();
        }
    }
}
