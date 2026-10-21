// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The Japan Post customer barcode, which the Japan Post zip code and barcode manual defines. The text is the postal code and the address number as digits,
/// capital letters and hyphens, and the symbol carries twenty codes, a check code, and a start and a stop code. A letter takes two codes, and CC4 fills the
/// codes the text leaves empty. The symbol prints no text line: page 13 of the manual prints the barcode alone.
/// </summary>
public sealed class JapanPostSymbology : BarcodeSymbology
{
    /// <inheritdoc/>
    internal override void Encode(ReadOnlySpan<char> text, BarcodeOptions options, ref BarcodeSymbol symbol)
    {
        if (text.Length == 0)
        {
            throw new ArgumentException("The Japan Post barcode carries at least one character.", nameof(text));
        }

        InlineArray24<byte> codeStorage = default;
        Span<byte> codes = ((Span<byte>)codeStorage)[..JapanPostEncoder.CodeCount];
        JapanPostEncoder.Codes(text, codes);
        InlineArray128<FourState> stateStorage = default;
        Span<FourState> states = ((Span<FourState>)stateStorage)[..JapanPostEncoder.BarCount];
        JapanPostEncoder.Bars(codes, states);
        FourStateEncoder.BuildSymbol(states, JapanPostEncoder.Metrics, [], options, ref symbol);
    }
}
