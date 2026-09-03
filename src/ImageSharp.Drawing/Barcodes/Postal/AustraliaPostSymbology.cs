// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The Australia Post 4-state customer barcode, which the Australia Post Customer Barcoding Technical
/// Specifications define. The text is a 2-digit format control code, an 8-digit sorting code and the
/// customer information. Format control codes 11, 45, 87 and 92 give the 37-bar Standard Customer
/// Barcode, which has no customer information field, 59 gives the 52-bar Customer Barcode 2 with a field
/// of 16 bars and 62 the 67-bar Customer Barcode 3 with a field of 31 bars. Format control code 00 is the
/// Null Customer Barcode, "only valid if DPID is 00000000". The C Encoding Table encodes
/// a character in three bars and the N Encoding Table a digit in two, and filler bars complete the field.
/// The printed line is the format control code, the sorting code, the customer information and the four
/// error correction symbols as decimal values.
/// </summary>
public sealed class AustraliaPostSymbology : BarcodeSymbology
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AustraliaPostSymbology"/> class that encodes the
    /// customer information with the C Encoding Table.
    /// </summary>
    public AustraliaPostSymbology()
        : this(AustraliaPostEncodingTable.Character)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AustraliaPostSymbology"/> class.
    /// </summary>
    /// <param name="customerInformationTable">The encoding table of the customer information field.</param>
    public AustraliaPostSymbology(AustraliaPostEncodingTable customerInformationTable)
        => this.CustomerInformationTable = customerInformationTable;

    /// <summary>
    /// Gets the encoding table of the customer information field.
    /// </summary>
    public AustraliaPostEncodingTable CustomerInformationTable { get; }

    /// <inheritdoc/>
    public override float NominalXDimension => AustraliaPostEncoder.Metrics.XDimension;

    /// <inheritdoc/>
    internal override BarcodeSymbol Encode(string text, BarcodeOptions options)
    {
        Guard.NotNull(text, nameof(text));
        return AustraliaPostEncoder.Encode(text, this.CustomerInformationTable, options);
    }
}
