// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Specifies the encoding table of the customer information field of an Australia Post 4-state barcode.
/// The Customer Barcoding Technical Specifications state that the field can be coded by either of the
/// two tables.
/// </summary>
public enum AustraliaPostEncodingTable
{
    /// <summary>
    /// The C Encoding Table: three bars per character, for capital letters, small letters, digits, the
    /// space and the number sign.
    /// </summary>
    Character,

    /// <summary>
    /// The N Encoding Table: two bars per digit.
    /// </summary>
    Numeric,
}
