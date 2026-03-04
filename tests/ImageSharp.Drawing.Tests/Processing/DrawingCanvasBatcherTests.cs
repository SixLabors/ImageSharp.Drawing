// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Diagnostics.CodeAnalysis;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Processing.Backends;
using SixLabors.ImageSharp.Drawing.Shapes.Rasterization;
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

        canvas.Fill(path, brushA);
        canvas.Fill(path, brushB);
        canvas.Flush();

        Assert.True(backend.HasBatch);
        Assert.NotNull(backend.LastBatch.Definition.Path);
        Assert.Equal(2, backend.LastBatch.Commands.Count);
        Assert.Same(brushA, backend.LastBatch.Commands[0].Brush);
        Assert.Same(brushB, backend.LastBatch.Commands[1].Brush);
    }

    [Fact]
    public void Flush_WhenAnyBrushUnsupported_DisablesSharedFlushId()
    {
        Configuration configuration = new();
        CapturingBackend backend = new()
        {
            IsBrushSupported = static brush => brush is SolidBrush
        };
        configuration.SetDrawingBackend(backend);

        using Image<Rgba32> image = new(40, 40);
        Buffer2DRegion<Rgba32> region = new(image.Frames.RootFrame.PixelBuffer, image.Bounds);

        IPath pathA = new RectangularPolygon(2, 2, 12, 12);
        IPath pathB = new RectangularPolygon(18, 18, 12, 12);
        DrawingOptions options = new();
        using DrawingCanvas<Rgba32> canvas = new(configuration, region, options);

        canvas.Fill(pathA, Brushes.Solid(Color.Red));
        canvas.Fill(pathB, Brushes.Horizontal(Color.Blue));
        canvas.Flush();

        Assert.NotEmpty(backend.Batches);
        Assert.All(backend.Batches, static batch => Assert.Equal(0, batch.FlushId));
    }

    private sealed class CapturingBackend : IDrawingBackend
    {
        public Func<Brush, bool> IsBrushSupported { get; init; } = static _ => true;

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
                    RasterizerSamplingOrigin.PixelBoundary)),
            Array.Empty<PreparedCompositionCommand>());

        public void FillPath<TPixel>(
            ICanvasFrame<TPixel> target,
            IPath path,
            Brush brush,
            GraphicsOptions graphicsOptions,
            in RasterizerOptions rasterizerOptions,
            DrawingCanvasBatcher<TPixel> batcher)
            where TPixel : unmanaged, IPixel<TPixel>
            => batcher.AddComposition(
                CompositionCommand.Create(path, brush, graphicsOptions, rasterizerOptions, target.Bounds.Location));

        public bool IsCompositionBrushSupported<TPixel>(Brush brush)
            where TPixel : unmanaged, IPixel<TPixel>
            => this.IsBrushSupported(brush);

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

            this.LastBatch = batches[batches.Count - 1];
            this.HasBatch = true;
            this.Batches.AddRange(batches);
        }

        public bool TryReadRegion<TPixel>(
            Configuration configuration,
            ICanvasFrame<TPixel> target,
            Rectangle sourceRectangle,
            [NotNullWhen(true)] out Image<TPixel>? image)
            where TPixel : unmanaged, IPixel<TPixel>
        {
            image = null;
            return false;
        }
    }
}
