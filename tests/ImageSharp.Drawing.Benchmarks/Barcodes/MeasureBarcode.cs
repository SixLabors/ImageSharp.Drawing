// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using BenchmarkDotNet.Attributes;
using SixLabors.ImageSharp.Drawing.Barcodes;

namespace SixLabors.ImageSharp.Drawing.Benchmarks.Barcodes;

/// <summary>
/// Measures the encodation of symbols through the public measuring API, which encodes the full symbol and lays out its modules and text.
/// </summary>
public class MeasureBarcode
{
    private BarcodeOptions options = new();
    private BarcodeSymbology symbology = new Code128Symbology();
    private string text = string.Empty;

    /// <summary>
    /// Gets or sets the name of the case to encode. A case that ends in "Text" sets a font, so the measure also lays out the human readable interpretation.
    /// </summary>
    [Params("Code128", "Ean13", "Ean13Text", "Gs1128", "Pdf417", "Code16K", "QrCode", "DotCode", "Aztec", "Gs1DataBarExpandedStacked")]
    public string Case { get; set; } = string.Empty;

    /// <summary>
    /// Creates the symbology, the options and the text of the case.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        (this.symbology, this.text) = this.Case switch
        {
            "Ean13" or "Ean13Text" => ((BarcodeSymbology)new Ean13Symbology(), "5901234123457"),
            "Gs1128" => (new Gs1128Symbology(), "(01)09521234543213(3103)000123(10)ABC123"),
            "Pdf417" => (new Pdf417Symbology(), "PDF417 rows and columns hold text, digits 0123456789 and bytes."),
            "Code16K" => (new Code16KSymbology(), "Abcd-1234567890-wxyZ"),
            "QrCode" => (new QrCodeSymbology(), "https://sixlabors.com/products/imagesharp-drawing/ QR Code holds text, digits 0123456789 and bytes."),
            "DotCode" => (new DotCodeSymbology(), "DotCode holds text, digits 0123456789 and bytes."),
            "Aztec" => (new AztecCodeSymbology(), "https://sixlabors.com/products/imagesharp-drawing/ Aztec Code holds text, digits 0123456789 and bytes."),
            "Gs1DataBarExpandedStacked" => (new Gs1DataBarExpandedStackedSymbology(), "(01)09521234543213(3103)000123(15)991231(10)ABC123"),
            _ => (new Code128Symbology(), "https://sixlabors.com/products/imagesharp-drawing/"),
        };

        this.options = this.Case.EndsWith("Text", StringComparison.Ordinal) ? new BarcodeOptions { Font = BarcodeFonts.OcrB.CreateFont(10F) } : new BarcodeOptions();
    }

    /// <summary>
    /// Encodes and measures the symbol.
    /// </summary>
    /// <returns>The renderable bounds.</returns>
    [Benchmark]
    public RectangleF Measure() => BarcodeMeasurer.MeasureRenderableBounds(this.symbology, this.options, PointF.Empty, this.text);
}
