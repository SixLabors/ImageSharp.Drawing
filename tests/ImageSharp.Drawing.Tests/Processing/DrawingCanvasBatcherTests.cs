// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Diagnostics.CodeAnalysis;
using SixLabors.Fonts;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Processing.Backends;
using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Drawing.Tests.Processing;

public class DrawingCanvasBatcherTests
{
    [Fact]
    public void Flush_SamePathDifferentBrushes_UsesSingleCoverageDefinition()
    {
        Configuration configuration = new();
        CapturingBackend backend = new();
        configuration.SetDrawingBackend(backend);
        using Image<Rgba32> image = new(40, 40);
        Buffer2DRegion<Rgba32> region = new(image.Frames.RootFrame.PixelBuffer, image.Bounds);

        IPath path = new RectangularPolygon(4, 6, 18, 12);
        DrawingOptions options = new();
        using DrawingCanvas<Rgba32> canvas = new(configuration, region, options);
        Brush brushA = Brushes.Solid(Color.Red);
        Brush brushB = Brushes.Solid(Color.Blue);

        canvas.Fill(brushA, path);
        canvas.Fill(brushB, path);
        canvas.Flush();

        Assert.True(backend.HasBatch);
        Assert.NotNull(backend.LastBatch.Definition.Path);
        Assert.Equal(2, backend.LastBatch.Commands.Count);
        Assert.Same(brushA, backend.LastBatch.Commands[0].Brush);
        Assert.Same(brushB, backend.LastBatch.Commands[1].Brush);
    }

    [Fact]
    public void Flush_SamePathDifferentBrushes_Stroke_UsesSingleBatch()
    {
        Configuration configuration = new();
        CapturingBackend backend = new();
        configuration.SetDrawingBackend(backend);
        using Image<Rgba32> image = new(40, 40);
        Buffer2DRegion<Rgba32> region = new(image.Frames.RootFrame.PixelBuffer, image.Bounds);

        IPath path = new RectangularPolygon(4, 6, 18, 12);
        DrawingOptions options = new();
        using DrawingCanvas<Rgba32> canvas = new(configuration, region, options);
        Pen penA = Pens.Solid(Color.Red, 2F);
        Pen penB = Pens.Solid(Color.Blue, 2F);

        canvas.Draw(penA, path);
        canvas.Draw(penB, path);
        canvas.Flush();

        Assert.Single(backend.Batches);
        Assert.True(backend.LastBatch.Definition.IsStroke);
        Assert.Equal(2, backend.LastBatch.Commands.Count);
    }

    [Fact]
    public void Flush_SamePathReusedMultipleTimes_BatchesCommands()
    {
        Configuration configuration = new();
        CapturingBackend backend = new();
        configuration.SetDrawingBackend(backend);
        using Image<Rgba32> image = new(100, 100);
        Buffer2DRegion<Rgba32> region = new(image.Frames.RootFrame.PixelBuffer, image.Bounds);

        // Use the same path reference 10 times with different brushes.
        IPath path = new RectangularPolygon(10, 10, 40, 40);
        DrawingOptions options = new();
        using DrawingCanvas<Rgba32> canvas = new(configuration, region, options);

        for (int i = 0; i < 10; i++)
        {
            canvas.Fill(Brushes.Solid(Color.FromPixel(new Rgba32((byte)i, 0, 0, 255))), path);
        }

        canvas.Flush();

        // All 10 commands share the same path reference → single batch.
        Assert.Single(backend.Batches);
        Assert.Equal(10, backend.Batches[0].Commands.Count);
    }

    [Fact]
    public void Flush_RepeatedGlyphs_ReusesCoverageDefinitions()
    {
        Configuration configuration = new();
        CapturingBackend backend = new();
        configuration.SetDrawingBackend(backend);
        using Image<Rgba32> image = new(420, 220);
        Buffer2DRegion<Rgba32> region = new(image.Frames.RootFrame.PixelBuffer, image.Bounds);

        Font font = TestFontUtilities.GetFont(TestFonts.OpenSans, 48);
        RichTextOptions textOptions = new(font)
        {
            Origin = new PointF(8, 8),
            WrappingLength = 400
        };

        DrawingOptions drawingOptions = new()
        {
            GraphicsOptions = new GraphicsOptions { Antialias = true }
        };

        string text = new('A', 200);
        Brush brush = Brushes.Solid(Color.Black);

        using DrawingCanvas<Rgba32> canvas = new(configuration, region, drawingOptions);
        canvas.DrawText(textOptions, text, brush, pen: null);
        canvas.Flush();

        int totalCommands = backend.Batches.Sum(b => b.Commands.Count);
        Assert.True(totalCommands > 0);

        // The glyph renderer caches paths within 1/8th pixel sub-pixel offset,
        // so 200 identical glyphs reuse coverage definitions across sub-pixel variants.
        Assert.True(
            backend.Batches.Count < 200,
            $"Expected coverage reuse but got {backend.Batches.Count} batches for 200 glyphs.");
    }

    private sealed class CapturingBackend : IDrawingBackend
    {
        public List<CompositionBatch> Batches { get; } = [];

        public bool HasBatch { get; private set; }

        public CompositionBatch LastBatch { get; private set; } = new(
            new CompositionCoverageDefinition(
                0,
                EmptyPath.ClosedPath,
                new RasterizerOptions(
                    Rectangle.Empty,
                    IntersectionRule.NonZero,
                    RasterizationMode.Aliased,
                    RasterizerSamplingOrigin.PixelBoundary,
                    0.5f)),
            []);

        public void FlushCompositions<TPixel>(
            Configuration configuration,
            ICanvasFrame<TPixel> target,
            CompositionScene compositionScene)
            where TPixel : unmanaged, IPixel<TPixel>
        {
            List<CompositionBatch> batches = CompositionScenePlanner.CreatePreparedBatches(
                compositionScene.Commands,
                target.Bounds);
            if (batches.Count == 0)
            {
                return;
            }

            this.LastBatch = batches[^1];
            this.HasBatch = true;
            this.Batches.AddRange(batches);
        }

        public bool TryReadRegion<TPixel>(
            Configuration configuration,
            ICanvasFrame<TPixel> target,
            Rectangle sourceRectangle,
            [NotNullWhen(true)] out Image<TPixel> image)
            where TPixel : unmanaged, IPixel<TPixel>
        {
            image = null;
            return false;
        }

        public void ComposeLayer<TPixel>(
            Configuration configuration,
            ICanvasFrame<TPixel> source,
            ICanvasFrame<TPixel> destination,
            Point destinationOffset,
            GraphicsOptions options)
            where TPixel : unmanaged, IPixel<TPixel>
        {
        }

        public ICanvasFrame<TPixel> CreateLayerFrame<TPixel>(
            Configuration configuration,
            ICanvasFrame<TPixel> parentTarget,
            int width,
            int height)
            where TPixel : unmanaged, IPixel<TPixel>
            => DefaultDrawingBackend.Instance.CreateLayerFrame(configuration, parentTarget, width, height);

        public void ReleaseFrameResources<TPixel>(
            Configuration configuration,
            ICanvasFrame<TPixel> target)
            where TPixel : unmanaged, IPixel<TPixel>
        {
        }
    }
}
