// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Barcodes;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Drawing.Tests.Barcodes;

/// <summary>
/// Golden image tests for <see cref="DrawingCanvas.DrawBarcode(BarcodeSymbology, string, BarcodeOptions, PointF)"/>.
/// The encoder tests already pin the module sequences, so these tests cover the rendering layer: module
/// scaling, quiet zones, guard bar extension, background painting, brushes and the human readable text.
/// </summary>
[GroupOutput("Barcodes")]
public class DrawBarcodeTests
{
    [Theory]
    [WithBlankImage(260, 140, PixelTypes.Rgba32)]
    public void Ean13<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new Ean13Symbology(), "5901234123457", CreateOptions());

    [Theory]
    [WithBlankImage(260, 160, PixelTypes.Rgba32)]
    public void Ean13_WithText<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new Ean13Symbology(), "5901234123457", CreateTextOptions());

    [Theory]
    [WithBlankImage(200, 140, PixelTypes.Rgba32)]
    public void Ean8<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new Ean8Symbology(), "96385074", CreateOptions());

    [Theory]
    [WithBlankImage(260, 160, PixelTypes.Rgba32)]
    public void UpcA_WithText<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new UpcASymbology(), "036000291452", CreateTextOptions());

    [Theory]
    [WithBlankImage(180, 160, PixelTypes.Rgba32)]
    public void UpcE_WithText<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new UpcESymbology(), "01234565", CreateTextOptions());

    [Theory]
    [WithBlankImage(160, 160, PixelTypes.Rgba32)]
    public void Ean5_WithText<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new Ean5Symbology(), "52495", CreateTextOptions());

    [Theory]
    [WithBlankImage(110, 140, PixelTypes.Rgba32)]
    public void Ean2<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new Ean2Symbology(), "05", CreateOptions());

    [Theory]
    [WithBlankImage(280, 180, PixelTypes.Rgba32)]
    public void Isbn_WithText<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new IsbnSymbology(), "978-0-306-40615-7", CreateTextOptions());

    [Theory]
    [WithBlankImage(280, 180, PixelTypes.Rgba32)]
    public void Ismn_WithText<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new IsmnSymbology(), "M-2306-7118", CreateTextOptions());

    [Theory]
    [WithBlankImage(280, 180, PixelTypes.Rgba32)]
    public void Issn_WithText<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new IssnSymbology(), "0317-8471", CreateTextOptions());

    [Theory]
    [WithBlankImage(210, 160, PixelTypes.Rgba32)]
    public void Mands_WithText<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => RunTest(provider, new MandsSymbology(), "0642118", CreateTextOptions());

    /// <summary>
    /// Disabling the quiet zones must shift the first bar to the draw origin, and a non-solid brush must
    /// flow through to the bar fill unchanged.
    /// </summary>
    [Theory]
    [WithBlankImage(220, 140, PixelTypes.Rgba32)]
    public void Ean13_NoQuietZones_GradientBars<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        BarcodeOptions options = CreateOptions();
        options.IncludeQuietZones = false;
        options.BarBrush = new LinearGradientBrush(
            new PointF(0, 0),
            new PointF(220, 0),
            GradientRepetitionMode.None,
            new ColorStop(0, Color.DarkBlue),
            new ColorStop(1, Color.DarkRed));

        RunTest(provider, new Ean13Symbology(), "5901234123457", options);
    }

    private static void RunTest<TPixel>(TestImageProvider<TPixel> provider, BarcodeSymbology symbology, string text, BarcodeOptions options)
        where TPixel : unmanaged, IPixel<TPixel>
        => provider.RunValidatingProcessorTest(
            x => x.Paint(canvas => canvas.DrawBarcode(symbology, text, options, new PointF(10, 10))),
            appendPixelTypeToFileName: false,
            appendSourceFileOrDescription: false);

    private static BarcodeOptions CreateOptions()
        => new()
        {
            ModuleWidth = 2F,
            BarHeight = 100F,
            Background = Brushes.Solid(Color.White),
        };

    private static BarcodeOptions CreateTextOptions()
    {
        BarcodeOptions options = CreateOptions();
        // GS1 prints the interpretation 2.75mm high at the 0.33mm X-dimension; at ModuleWidth 2 that is
        // 16.7px of ink, and the font's ink spans 0.72 em.
        options.Font = BarcodeFonts.OcrA.CreateFont(23.15F);
        return options;
    }
}
