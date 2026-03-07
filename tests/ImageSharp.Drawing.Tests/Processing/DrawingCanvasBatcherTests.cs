// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Diagnostics.CodeAnalysis;
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

        canvas.Fill(path, brushA);
        canvas.Fill(path, brushB);
        canvas.Flush();

        Assert.True(backend.HasBatch);
        Assert.NotNull(backend.LastBatch.Definition.Path);
        Assert.Equal(2, backend.LastBatch.Commands.Count);
        Assert.Same(brushA, backend.LastBatch.Commands[0].Brush);
        Assert.Same(brushB, backend.LastBatch.Commands[1].Brush);
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
    }
}
