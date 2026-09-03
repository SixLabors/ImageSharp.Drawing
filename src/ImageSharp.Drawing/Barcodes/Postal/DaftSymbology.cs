// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.Fonts.Unicode;
using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// A custom height modulated symbology whose input is the bar states themselves: D for a descender, A
/// for an ascender, F for a full bar and T for a tracker. The bars have the dimensions of Table 11 of
/// the Royal Mail Mailmark barcode definition document. The symbol has no quiet zone and prints no line.
/// </summary>
public sealed class DaftSymbology : BarcodeSymbology
{
    /// <summary>
    /// The largest number of bars a symbol carries.
    /// </summary>
    public const int MaximumLength = 2500;

    /// <summary>
    /// The dimensions: those of the Royal Mail symbologies, without their clear zone.
    /// </summary>
    private static readonly FourStateMetrics Metrics = new(
        FourStateEncoder.RoyalMail.XDimension,
        FourStateEncoder.RoyalMail.RunUnit,
        FourStateEncoder.RoyalMail.BarUnits,
        FourStateEncoder.RoyalMail.SpaceUnits,
        FourStateEncoder.RoyalMail.Ascender,
        FourStateEncoder.RoyalMail.Tracker,
        FourStateEncoder.RoyalMail.Descender,
        0F);

    /// <inheritdoc/>
    public override float NominalXDimension => Metrics.XDimension;

    /// <inheritdoc/>
    internal override BarcodeSymbol Encode(string text, BarcodeOptions options)
    {
        Guard.NotNull(text, nameof(text));
        Guard.MustBeGreaterThan(text.Length, 0, nameof(text));
        Guard.MustBeLessThanOrEqualTo(text.Length, MaximumLength, nameof(text));

        FourState[] states = new FourState[text.Length];
        int index = 0;
        SpanCodePointEnumerator codePoints = text.AsSpan().EnumerateCodePoints();
        while (codePoints.MoveNext())
        {
            CodePoint current = codePoints.Current;
            states[index++] = current.Value switch
            {
                'D' => FourState.Descender,
                'A' => FourState.Ascender,
                'F' => FourState.Full,
                'T' => FourState.Tracker,
                _ => throw new ArgumentException($"A DAFT symbol carries only the letters D, A, F and T; got {current.ToDisplayString()}.", nameof(text)),
            };
        }

        return FourStateEncoder.BuildSymbol(states, Metrics, string.Empty, options);
    }
}
