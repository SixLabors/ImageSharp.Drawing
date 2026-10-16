// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using BenchmarkDotNet.Attributes;
using SixLabors.ImageSharp.Drawing.Barcodes;

namespace SixLabors.ImageSharp.Drawing.Benchmarks.Barcodes;

/// <summary>
/// Measures the encodation of linear and stacked symbols through the public measuring API, which encodes the full symbol and lays out its bars and text.
/// </summary>
public class MeasureBarcode
{
    private readonly BarcodeOptions options = new();
    private BarcodeSymbology symbology = new Code128Symbology();
    private string text = string.Empty;

    /// <summary>
    /// Gets or sets the name of the case to encode.
    /// </summary>
    [Params("Code128", "Ean13", "Gs1128", "Pdf417", "Code16K")]
    public string Case { get; set; } = string.Empty;

    /// <summary>
    /// Creates the symbology and the text of the case.
    /// </summary>
    [GlobalSetup]
    public void Setup()
        => (this.symbology, this.text) = this.Case switch
        {
            "Ean13" => ((BarcodeSymbology)new Ean13Symbology(), "5901234123457"),
            "Gs1128" => (new Gs1128Symbology(), "(01)09521234543213(3103)000123(10)ABC123"),
            "Pdf417" => (new Pdf417Symbology(), "PDF417 rows and columns hold text, digits 0123456789 and bytes."),
            "Code16K" => (new Code16KSymbology(), "Abcd-1234567890-wxyZ"),
            _ => (new Code128Symbology(), "https://sixlabors.com/products/imagesharp-drawing/"),
        };

    /// <summary>
    /// Encodes and measures the symbol.
    /// </summary>
    /// <returns>The renderable bounds.</returns>
    [Benchmark]
    public RectangleF Measure() => BarcodeMeasurer.MeasureRenderableBounds(this.symbology, this.text, this.options, PointF.Empty);
}
