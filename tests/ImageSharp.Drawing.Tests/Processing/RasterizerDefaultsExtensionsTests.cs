// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Diagnostics.CodeAnalysis;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Processing.Backends;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Drawing.Tests.Processing;

public class RasterizerDefaultsExtensionsTests
{
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

    private sealed class RecordingDrawingBackend : IDrawingBackend
    {
        public void FlushCompositions<TPixel>(
            Configuration configuration,
            ICanvasFrame<TPixel> target,
            CompositionScene compositionScene)
            where TPixel : unmanaged, IPixel<TPixel>
        {
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

        public void ReleaseFrameResources<TPixel>(
            Configuration configuration,
            ICanvasFrame<TPixel> target)
            where TPixel : unmanaged, IPixel<TPixel>
        {
        }
    }
}
