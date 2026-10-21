// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The MaxiCode symbology, ISO/IEC 16023. A symbol is 33 offset rows of hexagonal modules, alternately 30 and 29 wide, around a finder of three dark rings.
/// The symbol has a quiet zone of one module on every side. It carries the characters U+0000 to U+00FF with Reed-Solomon error correction. The X dimension is
/// 0.88 mm. Section 4.11.1 permits 24.00/29 to 27.00/29 mm. When an X dimension is outside that range, it draws at the nearest limit.
/// <para>
/// In modes 2 and 3 the text starts with the structured carrier message: the postal code, the 3-digit country code and the 3-digit class of service, each
/// followed by a group separator (U+001D). A message that starts with "[)&gt;", RS, "01", GS and two digits keeps that header in the secondary message. Without
/// a mode the symbol takes mode 5 when the data fits and mode 4 otherwise.
/// </para>
/// </summary>
public sealed class MaxiCodeSymbology : BarcodeSymbology
{
    /// <summary>
    /// The nominal X dimension in millimetres: W = X = L/29 = 0.88 mm at the nominal L of 25.50 mm, Table 7 of ISO/IEC 16023. Section 4.11.1 accepts L from
    /// 24.00 mm to 27.00 mm.
    /// </summary>
    private const double NominalXDimension = 0.88;

    /// <summary>
    /// The smallest X dimension in millimetres: L/29 at the smallest L of 24.00 mm of section 4.11.1. Section 4.11.6 gives the same limit for the 32X wide
    /// symbol with its quiet zones: "Each MaxiCode symbol shall be in the range of 26.48mm wide by 25.32mm high to 29.79mm wide by 28.49mm high."
    /// </summary>
    private const double MinimumXDimension = 24.00 / 29;

    /// <summary>
    /// The largest X dimension in millimetres: L/29 at the largest L of 27.00 mm of section 4.11.1.
    /// </summary>
    private const double MaximumXDimension = 27.00 / 29;

    /// <summary>
    /// Initializes a new instance of the <see cref="MaxiCodeSymbology"/> class that takes mode 5 when the data fits and mode 4 otherwise.
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
    /// Gets the mode, or <see langword="null"/> when the symbol takes mode 5 when the data fits and mode 4 otherwise.
    /// </summary>
    public MaxiCodeMode? Mode { get; }

    /// <inheritdoc/>
    internal override void Encode(ReadOnlySpan<char> text, BarcodeOptions options, ref BarcodeSymbol symbol)
    {
        // The hexagonal modules do not stand on whole pixels, so the code clamps the X dimension in millimetres, and the symbol draws at it exactly.
        double xDimension = Math.Clamp(options.XDimension ?? NominalXDimension, MinimumXDimension, MaximumXDimension);
        MaxiCodeEncoder.BuildSymbol(text, this.Mode, xDimension, ref symbol);
    }
}
