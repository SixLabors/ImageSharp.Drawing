// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The Japan Post customer barcode, which the Japan Post zip code and barcode manual defines. The text
/// is the postal code and the address number as digits, capital letters and hyphens, and the symbol
/// carries twenty codes, a check code, and a start and a stop code. A letter takes two codes, and CC4
/// fills the codes the text leaves empty. The printed line shows the text as given.
/// </summary>
public sealed class JapanPostSymbology : BarcodeSymbology
{
    /// <inheritdoc/>
    public override float NominalXDimension => JapanPostEncoder.Metrics.XDimension;

    /// <inheritdoc/>
    internal override BarcodeSymbol Encode(string text, BarcodeOptions options)
    {
        Guard.NotNull(text, nameof(text));
        if (text.Length == 0)
        {
            throw new ArgumentException("The Japan Post barcode carries at least one character.", nameof(text));
        }

        Span<byte> codes = stackalloc byte[JapanPostEncoder.CodeCount];
        JapanPostEncoder.Codes(text, codes);
        Span<FourState> states = stackalloc FourState[JapanPostEncoder.BarCount];
        JapanPostEncoder.Bars(codes, states);
        string readable = options.Font is null ? string.Empty : text;
        return FourStateEncoder.BuildSymbol(states, JapanPostEncoder.Metrics, readable, options);
    }
}
