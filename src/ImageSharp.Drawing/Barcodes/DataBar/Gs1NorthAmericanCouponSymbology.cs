// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The GS1 North American Coupon, a GS1 DataBar Expanded Stacked symbol that carries a single element string, the coupon code of GS1 Application Identifier
/// (8110). The North American Coupon Application Guideline of GS1 US: "Data shall be encoded in a GS1 DataBar Expanded Symbol. This barcode is constructed in
/// segments and is capable of being stacked (as shown) with a minimum of 2 segments per row." Each row is 34X high, a 3X separator stands between rows, and no
/// quiet zone is required. Section 5.4 of the guideline gives an X dimension of 0.254 to 0.33 millimetres, and an X dimension outside that range is clamped
/// to it.
/// <para>
/// The input is <c>(8110)</c> and a coupon code of up to 70 digits, whose elements Table C of the guideline defines: the GS1 Company Prefix with its length
/// indicator, the offer code, the save value and the primary purchase requirement, and optional data fields 1, 2, 3, 4, 5, 6 and 9 in increasing order. Section
/// 5.5 of the guideline: "The only human readable digits shown from the GS1 DataBar barcode will be the GS1 Company Prefix and Offer Code separated by a dash."
/// They print above the symbol, as Figure 1 shows.
/// </para>
/// </summary>
public sealed class Gs1NorthAmericanCouponSymbology : BarcodeSymbology
{
    /// <summary>
    /// The most digits the coupon code carries. Section 5.2 of the guideline: "until all desired data is encoded (or the limit of 70 digits is reached)".
    /// </summary>
    private const int MaximumDigits = 70;

    /// <summary>
    /// The smallest X dimension in millimetres. Section 5.4 of the guideline: "If a specific application requires, the X-dimension may be as small as 0.0100
    /// inch. (0.254 mm) or as large as 0.013 in. (0.33 mm)."
    /// </summary>
    private const double MinimumXDimension = 0.0100 * 25.4;

    /// <summary>
    /// The largest X dimension in millimetres, 0.013 inch, which section 5.4 also gives as the nominal X dimension.
    /// </summary>
    private const double MaximumXDimension = 0.33;

    /// <summary>
    /// Initializes a new instance of the <see cref="Gs1NorthAmericanCouponSymbology"/> class with eight segments in a row, as Figure 1 of the guideline prints
    /// the symbol.
    /// </summary>
    public Gs1NorthAmericanCouponSymbology()
        : this(8)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Gs1NorthAmericanCouponSymbology"/> class.
    /// </summary>
    /// <param name="segments">The number of segments in a full row, an even number from 2 to 20. A segment is one symbol character.</param>
    public Gs1NorthAmericanCouponSymbology(int segments)
    {
        Guard.MustBeBetweenOrEqualTo(segments, DataBarExpandedEncoder.MinimumSegments, DataBarExpandedEncoder.MaximumStackedSegments, nameof(segments));
        if ((segments & 1) == 1)
        {
            throw new ArgumentOutOfRangeException(nameof(segments), segments, "The number of segments in a row is even.");
        }

        this.Segments = segments;
    }

    /// <summary>
    /// Gets the number of segments in a full row. The last row can hold fewer.
    /// </summary>
    public int Segments { get; }

    /// <inheritdoc/>
    internal override void Encode(ReadOnlySpan<char> text, BarcodeOptions options, ref BarcodeSymbol symbol)
    {
        if (!text.StartsWith("(8110)", StringComparison.Ordinal))
        {
            throw new ArgumentException("A GS1 North American Coupon carries a single element string, Application Identifier (8110).", nameof(text));
        }

        InlineArray64<char> bufferStorage = default;
        int capacity = Gs1Data.PreparedCapacity(text, DataBarExpandedEncoder.MaximumDataCharacters);
        ValueStringBuilder encoded = capacity <= Gs1Data.InlineBufferLength ? new(bufferStorage) : new(capacity);
        try
        {
            Gs1Data.Prepare(text, ref encoded);

            // Application Identifier (8110) has no predefined length, so a second element string after it stands behind a separator.
            ReadOnlySpan<char> data = encoded.AsSpan()[4..];
            if (data.Contains(Gs1Data.Separator))
            {
                throw new ArgumentException("A GS1 North American Coupon carries a single element string, Application Identifier (8110).", nameof(text));
            }

            if (data.Length > MaximumDigits)
            {
                throw new ArgumentException($"The coupon code carries at most {MaximumDigits} digits; got {data.Length}.", nameof(text));
            }

            int prefixLength = NorthAmericanCouponCode.Validate(data);

            // The caption is the GS1 Company Prefix and the offer code, joined by a hyphen.
            DataBarExpandedEncoder.BuildStacked(
                encoded.AsSpan(),
                this.Segments,
                false,
                data.Slice(1, prefixLength),
                "-",
                data.Slice(1 + prefixLength, NorthAmericanCouponCode.OfferCodeLength),
                BarcodeTextSide.AboveBars,
                MinimumXDimension,
                MaximumXDimension,
                options,
                ref symbol);
        }
        finally
        {
            encoded.Dispose();
        }
    }
}
