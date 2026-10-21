// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// A GS1 Digital Link URI in a QR Code symbol, section 5.10 of the GS1 General Specifications. The symbol carries the URI as plain data. The URI follows the
/// GS1 Digital Link Standard: URI Syntax: a primary identification key and its key qualifiers in the path, data attributes in the query string, and every
/// Application Identifier with valid data.
/// <para>
/// When a font is set, the element strings that the URI carries print below the symbol, one on each line, with the Application Identifiers in parentheses, in
/// the order of the URI. The URI does not print.
/// </para>
/// <para>
/// Table 5-46, symbol specification table 1 addendum 2, gives an X dimension of 0.396 to 0.990 millimetres with a target of 0.495. An X dimension outside
/// that range draws at the nearest limit.
/// </para>
/// </summary>
public sealed class Gs1DigitalLinkQrCodeSymbology : BarcodeSymbology
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Gs1DigitalLinkQrCodeSymbology"/> class at error correction level M. The symbol takes the smallest version
    /// that holds the URI.
    /// </summary>
    public Gs1DigitalLinkQrCodeSymbology()
        : this(QrErrorCorrectionLevel.M)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Gs1DigitalLinkQrCodeSymbology"/> class. The symbol takes the smallest version that holds the URI.
    /// </summary>
    /// <param name="errorCorrectionLevel">The error correction level.</param>
    public Gs1DigitalLinkQrCodeSymbology(QrErrorCorrectionLevel errorCorrectionLevel)
    {
        Guard.MustBeBetweenOrEqualTo((int)errorCorrectionLevel, (int)QrErrorCorrectionLevel.L, (int)QrErrorCorrectionLevel.H, nameof(errorCorrectionLevel));
        this.ErrorCorrectionLevel = errorCorrectionLevel;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Gs1DigitalLinkQrCodeSymbology"/> class with a fixed version.
    /// </summary>
    /// <param name="errorCorrectionLevel">The error correction level.</param>
    /// <param name="version">The version, 1 to 40.</param>
    public Gs1DigitalLinkQrCodeSymbology(QrErrorCorrectionLevel errorCorrectionLevel, int version)
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
    /// Gets the version, or <see langword="null"/> when the version follows from the URI.
    /// </summary>
    public int? Version { get; }

    /// <inheritdoc/>
    internal override void Encode(ReadOnlySpan<char> text, BarcodeOptions options, ref BarcodeSymbol symbol)
    {
        // The URI encodes as plain data, so no URI longer than the largest symbol holds is parsed.
        Guard.MustBeLessThanOrEqualTo(text.Length, QrEncoder.MaximumLength, nameof(text));
        int version = this.Version ?? 0;
        double xDimension = options.ResolveXDimension(
            Gs1Data.DigitalLinkMinimumXDimension,
            Gs1Data.DigitalLinkXDimension,
            Gs1Data.DigitalLinkMaximumXDimension);

        if (options.Font is null)
        {
            Gs1DigitalLink.Validate(text);
            QrEncoder.BuildSymbol(
                text,
                false,
                QrFormat.Full,
                this.ErrorCorrectionLevel,
                version,
                xDimension,
                ref symbol);

            return;
        }

        // The element strings print as GS1 QR Code prints them, from the values the validation decodes. The URI is bounded above, so the buffer is too, and a
        // longer URI rents it once.
        InlineArray64<char> readableStorage = default;
        PooledBuffer<char> rented = default;
        int capacity = Gs1DigitalLink.ReadableCapacity(text.Length);
        Span<char> readable = capacity <= Gs1Data.InlineBufferLength
            ? ((Span<char>)readableStorage)[..capacity]
            : (rented = new PooledBuffer<char>(capacity)).Span;
        try
        {
            int written = Gs1DigitalLink.Validate(text, readable);
            QrEncoder.BuildSymbol(
                text,
                false,
                QrFormat.Full,
                this.ErrorCorrectionLevel,
                version,
                xDimension,
                ref symbol);

            symbol.AddMatrixText(readable[..written], BarcodeTextAlignment.Left, Gs1Data.TextScale(options.Font));
        }
        finally
        {
            rented.Dispose();
        }
    }
}
