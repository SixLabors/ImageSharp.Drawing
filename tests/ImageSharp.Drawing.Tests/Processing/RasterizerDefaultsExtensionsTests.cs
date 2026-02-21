// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Processing.Backends;
using SixLabors.ImageSharp.Drawing.Shapes.Rasterization;
using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Drawing.Tests.Processing;

public class RasterizerDefaultsExtensionsTests
{
    [Fact]
    public void GetDefaultRasterizerFromConfiguration_AlwaysReturnsDefaultInstance()
    {
        Configuration configuration = new();

        IRasterizer first = configuration.GetRasterizer();
        IRasterizer second = configuration.GetRasterizer();

        Assert.Same(first, second);
        Assert.Same(DefaultRasterizer.Instance, first);
    }

    [Fact]
    public void GetDefaultDrawingBackendFromConfiguration_AlwaysReturnsDefaultInstance()
    {
        Configuration configuration = new();

        IDrawingBackend first = configuration.GetDrawingBackend();
        IDrawingBackend second = configuration.GetDrawingBackend();

        Assert.Same(first, second);
        Assert.Same(DefaultDrawingBackend.Instance, first);
    }

    [Fact]
    public void SetRasterizerOnConfiguration_RoundTrips()
    {
        Configuration configuration = new();
        RecordingRasterizer rasterizer = new();

        configuration.SetRasterizer(rasterizer);

        Assert.Same(rasterizer, configuration.GetRasterizer());
        Assert.IsType<DefaultDrawingBackend>(configuration.GetDrawingBackend());
    }

    [Fact]
    public void SetRasterizerOnProcessingContext_RoundTrips()
    {
        Configuration configuration = new();
        FakeImageOperationsProvider.FakeImageOperations<Rgba32> context = new(configuration, null, true);
        RecordingRasterizer rasterizer = new();

        context.SetRasterizer(rasterizer);

        Assert.Same(rasterizer, context.GetRasterizer());
        Assert.IsType<DefaultDrawingBackend>(context.GetDrawingBackend());
    }

    [Fact]
    public void GetRasterizerFromProcessingContext_FallsBackToConfiguration()
    {
        Configuration configuration = new();
        RecordingRasterizer rasterizer = new();
        configuration.SetRasterizer(rasterizer);
        FakeImageOperationsProvider.FakeImageOperations<Rgba32> context = new(configuration, null, true);

        Assert.Same(rasterizer, context.GetRasterizer());
    }

    [Fact]
    public void SetDrawingBackendOnConfiguration_RoundTrips()
    {
        Configuration configuration = new();
        RecordingDrawingBackend backend = new();

        configuration.SetDrawingBackend(backend);

        Assert.Same(backend, configuration.GetDrawingBackend());
    }

    [Fact]
    public void SetDrawingBackendOnProcessingContext_RoundTrips()
    {
        Configuration configuration = new();
        FakeImageOperationsProvider.FakeImageOperations<Rgba32> context = new(configuration, null, true);
        RecordingDrawingBackend backend = new();

        context.SetDrawingBackend(backend);

        Assert.Same(backend, context.GetDrawingBackend());
    }

    private sealed class RecordingRasterizer : IRasterizer
    {
        public void Rasterize<TState>(
            IPath path,
            in RasterizerOptions options,
            MemoryAllocator allocator,
            ref TState state,
            RasterizerScanlineHandler<TState> scanlineHandler)
            where TState : struct
        {
        }
    }

    private sealed class RecordingDrawingBackend : IDrawingBackend
    {
        public void BeginCompositeSession<TPixel>(Configuration configuration, ICanvasFrame<TPixel> target)
            where TPixel : unmanaged, IPixel<TPixel>
        {
        }

        public void EndCompositeSession<TPixel>(Configuration configuration, ICanvasFrame<TPixel> target)
            where TPixel : unmanaged, IPixel<TPixel>
        {
        }

        public void FillPath<TPixel>(
            Configuration configuration,
            ICanvasFrame<TPixel> target,
            IPath path,
            Brush brush,
            GraphicsOptions graphicsOptions,
            in RasterizerOptions rasterizerOptions)
            where TPixel : unmanaged, IPixel<TPixel>
        {
        }

        public void FillRegion<TPixel>(
            Configuration configuration,
            ICanvasFrame<TPixel> target,
            Brush brush,
            GraphicsOptions graphicsOptions,
            Rectangle region)
            where TPixel : unmanaged, IPixel<TPixel>
        {
        }

        public bool SupportsCoverageComposition<TPixel>(Brush brush, in GraphicsOptions graphicsOptions)
            where TPixel : unmanaged, IPixel<TPixel>
        {
            _ = brush;
            _ = graphicsOptions;
            return true;
        }

        public DrawingCoverageHandle PrepareCoverage(
            IPath path,
            in RasterizerOptions rasterizerOptions,
            MemoryAllocator allocator,
            CoveragePreparationMode preparationMode)
        {
            _ = preparationMode;
            return default;
        }

        public void CompositeCoverage<TPixel>(
            Configuration configuration,
            ICanvasFrame<TPixel> target,
            DrawingCoverageHandle coverageHandle,
            Point sourceOffset,
            Brush brush,
            in GraphicsOptions graphicsOptions,
            Rectangle brushBounds)
            where TPixel : unmanaged, IPixel<TPixel>
        {
        }

        public void ReleaseCoverage(DrawingCoverageHandle coverageHandle)
        {
        }
    }
}
