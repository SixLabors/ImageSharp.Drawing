// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The MaxiCode symbology, ISO/IEC 16023: 33 offset rows of hexagonal modules, alternately 30 and 29 wide,
/// around a finder of three dark rings, with a quiet zone of one module on every side. It carries the
/// characters U+0000 to U+00FF with Reed-Solomon error correction.
/// <para>
/// In modes 2 and 3 the text starts with the structured carrier message: the postal code, the 3-digit
/// country code and the 3-digit class of service, each followed by a group separator (U+001D). A message that
/// starts with "[)&gt;", RS, "01", GS and two digits keeps that header in the secondary message. Without a mode
/// the symbol takes mode 5 when the data fits and mode 4 otherwise.
/// </para>
/// </summary>
public sealed class MaxiCodeSymbology : BarcodeSymbology
{
    /// <summary>
    /// The nominal X dimension in millimetres: W = X = L/29 = 0.88 mm at the nominal L of 25.50 mm, Table 7 of
    /// ISO/IEC 16023. Section 4.11.1 accepts L from 24.00 mm to 27.00 mm.
    /// </summary>
    private const double NominalXDimension = 0.88;

    /// <summary>
    /// Initializes a new instance of the <see cref="MaxiCodeSymbology"/> class that takes mode 5 when the data
    /// fits and mode 4 otherwise.
    /// </summary>
    public MaxiCodeSymbology()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="MaxiCodeSymbology"/> class.
    /// </summary>
    /// <param name="mode">The mode.</param>
    public MaxiCodeSymbology(MaxiCodeMode mode)
    {
        Guard.MustBeBetweenOrEqualTo((int)mode, (int)MaxiCodeMode.StructuredCarrierNumeric, (int)MaxiCodeMode.ReaderProgramming, nameof(mode));
        this.Mode = mode;
    }

    /// <summary>
    /// Gets the mode, or <see langword="null"/> when the symbol takes mode 5 when the data fits and mode 4
    /// otherwise.
    /// </summary>
    public MaxiCodeMode? Mode { get; }

    /// <inheritdoc/>
    internal override void Encode(string text, BarcodeOptions options, ref BarcodeSymbol symbol)
    {
        Guard.NotNull(text, nameof(text));
        MaxiCodeEncoder.BuildSymbol(text, (int?)this.Mode ?? 0, options.XDimension ?? NominalXDimension, ref symbol);
    }
}
