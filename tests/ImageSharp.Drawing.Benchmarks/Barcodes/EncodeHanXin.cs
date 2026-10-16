// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using BenchmarkDotNet.Attributes;
using SixLabors.ImageSharp.Drawing.Barcodes;

namespace SixLabors.ImageSharp.Drawing.Benchmarks.Barcodes;

/// <summary>
/// Measures the encodation of Han Xin Code symbols through the public measuring API, which encodes the full
/// symbol: the segments, the error correction, the module placement and the mask choice.
/// </summary>
public class EncodeHanXin
{
    private readonly HanXinSymbology symbology = new();
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
    /// A short URL, 6500 digits and 2684 characters of lowercase text. At the default level L2, the digits and
    /// the text both need one of the largest versions, where the mask choice scores the most modules.
    /// </returns>
    public static IEnumerable<string> Texts()
    {
        yield return "https://sixlabors.com/products/imagesharp-drawing/";
        yield return new string('7', 6500);
        yield return string.Concat(Enumerable.Repeat("the quick brown fox jumps over the lazy dog ", 61));
    }

    /// <summary>
    /// Encodes and measures the symbol.
    /// </summary>
    /// <returns>The renderable bounds.</returns>
    [Benchmark]
    public RectangleF Encode() => BarcodeMeasurer.MeasureRenderableBounds(this.symbology, this.Text, this.options, PointF.Empty);
}
