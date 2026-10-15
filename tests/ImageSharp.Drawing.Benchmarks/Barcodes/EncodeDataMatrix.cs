// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using BenchmarkDotNet.Attributes;
using SixLabors.ImageSharp.Drawing.Barcodes;

namespace SixLabors.ImageSharp.Drawing.Benchmarks.Barcodes;

/// <summary>
/// Measures the encodation of Data Matrix symbols through the public measuring API, which encodes the full
/// symbol: the data codewords, the error correction and the module placement.
/// </summary>
public class EncodeDataMatrix
{
    private readonly DataMatrixSymbology symbology = new();
    private readonly BarcodeOptions options = new();

    /// <summary>
    /// Gets or sets the text to encode.
    /// </summary>
    [ParamsSource(nameof(Texts))]
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// Gets the texts to encode.
    /// </summary>
    /// <returns>
    /// A short URL, 3116 digits (the numeric capacity of the largest symbol), long lowercase text, and
    /// capital letters followed by pairs of a small and a capital letter. C40 and ASCII encodation cost the
    /// same for each pair, so the look-ahead test in C40 encodation reads to the end of the data at each
    /// group.
    /// </returns>
    public static IEnumerable<string> Texts()
    {
        yield return "https://sixlabors.com/products/imagesharp-drawing/";
        yield return new string('7', 3116);
        yield return string.Concat(Enumerable.Repeat("the quick brown fox jumps over the lazy dog ", 34));
        yield return "ABCDEF" + string.Concat(Enumerable.Repeat("aA", 760));
    }

    /// <summary>
    /// Encodes and measures the symbol.
    /// </summary>
    /// <returns>The renderable bounds.</returns>
    [Benchmark]
    public RectangleF Encode() => BarcodeMeasurer.MeasureRenderableBounds(this.symbology, this.Text, this.options, PointF.Empty);
}
