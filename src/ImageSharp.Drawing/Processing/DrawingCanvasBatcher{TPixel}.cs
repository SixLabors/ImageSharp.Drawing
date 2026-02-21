// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Processing.Backends;

namespace SixLabors.ImageSharp.Drawing.Processing;

internal sealed class DrawingCanvasBatcher<TPixel>
    where TPixel : unmanaged, IPixel<TPixel>
{
    internal DrawingCanvasBatcher(
        Configuration configuration,
        IDrawingBackend backend,
        ICanvasFrame<TPixel> targetFrame)
    {
        Guard.NotNull(configuration, nameof(configuration));
        Guard.NotNull(backend, nameof(backend));
        Guard.NotNull(targetFrame, nameof(targetFrame));
    }

    public void AddComposition(in CompositionCommand composition)
    {
        _ = composition;
        // Stub: implementation is added after backend contracts are wired.
    }

    public void FlushCompositions()
    {
        // Stub: implementation is added after backend contracts are wired.
    }
}
