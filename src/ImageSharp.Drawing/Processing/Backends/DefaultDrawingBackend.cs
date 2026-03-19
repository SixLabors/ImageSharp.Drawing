// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Buffers;
using SixLabors.ImageSharp.Memory;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// CPU backend that executes path coverage rasterization and brush composition directly against a CPU region.
/// </summary>
public sealed class DefaultDrawingBackend : IDrawingBackend
{
    /// <summary>
    /// Gets the default backend instance.
    /// </summary>
    public static DefaultDrawingBackend Instance { get; } = new();

    /// <inheritdoc />
    public void FlushCompositions<TPixel>(
        Configuration configuration,
        ICanvasFrame<TPixel> target,
        CompositionScene compositionScene)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        if (compositionScene.Commands.Count == 0)
        {
            return;
        }

        if (!target.TryGetCpuRegion(out Buffer2DRegion<TPixel> destinationFrame))
        {
            throw new NotSupportedException($"{nameof(DefaultDrawingBackend)} requires CPU-accessible frame targets.");
        }

        using FlushScene scene = FlushScene.Create(
            compositionScene.Commands,
            target.Bounds,
            configuration.MemoryAllocator);

        if (scene.ItemCount == 0)
        {
            return;
        }

        scene.Execute(configuration, destinationFrame);
    }

    /// <inheritdoc />
    public void ComposeLayer<TPixel>(
        Configuration configuration,
        ICanvasFrame<TPixel> source,
        ICanvasFrame<TPixel> destination,
        Point destinationOffset,
        GraphicsOptions options)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        Guard.NotNull(configuration, nameof(configuration));

        if (!source.TryGetCpuRegion(out Buffer2DRegion<TPixel> sourceRegion))
        {
            throw new NotSupportedException($"{nameof(DefaultDrawingBackend)} requires CPU-accessible source frames.");
        }

        if (!destination.TryGetCpuRegion(out Buffer2DRegion<TPixel> destinationRegion))
        {
            throw new NotSupportedException($"{nameof(DefaultDrawingBackend)} requires CPU-accessible destination frames.");
        }

        PixelBlender<TPixel> blender = PixelOperations<TPixel>.Instance.GetPixelBlender(options);
        float blendPercentage = options.BlendPercentage;

        int srcWidth = sourceRegion.Width;
        int srcHeight = sourceRegion.Height;
        int dstWidth = destinationRegion.Width;
        int dstHeight = destinationRegion.Height;

        // Clamp the compositing region to both source and destination bounds.
        int startX = Math.Max(0, -destinationOffset.X);
        int startY = Math.Max(0, -destinationOffset.Y);
        int endX = Math.Min(srcWidth, dstWidth - destinationOffset.X);
        int endY = Math.Min(srcHeight, dstHeight - destinationOffset.Y);

        if (endX <= startX || endY <= startY)
        {
            return;
        }

        int width = endX - startX;

        // Allocate a reusable per-row amount buffer from the memory pool.
        using IMemoryOwner<float> amountsOwner = configuration.MemoryAllocator.Allocate<float>(width);
        Span<float> amounts = amountsOwner.Memory.Span;
        amounts.Fill(blendPercentage);

        for (int y = startY; y < endY; y++)
        {
            Span<TPixel> srcRow = sourceRegion.DangerousGetRowSpan(y).Slice(startX, width);
            int dstX = destinationOffset.X + startX;
            int dstY = destinationOffset.Y + y;
            Span<TPixel> dstRow = destinationRegion.DangerousGetRowSpan(dstY).Slice(dstX, width);

            blender.Blend(configuration, dstRow, dstRow, srcRow, amounts);
        }
    }

    /// <inheritdoc />
    public ICanvasFrame<TPixel> CreateLayerFrame<TPixel>(
        Configuration configuration,
        ICanvasFrame<TPixel> parentTarget,
        int width,
        int height)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        Buffer2D<TPixel> buffer = configuration.MemoryAllocator.Allocate2D<TPixel>(width, height, AllocationOptions.Clean);
        return new MemoryCanvasFrame<TPixel>(new Buffer2DRegion<TPixel>(buffer));
    }

    /// <inheritdoc />
    public void ReleaseFrameResources<TPixel>(
        Configuration configuration,
        ICanvasFrame<TPixel> target)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        if (target.TryGetCpuRegion(out Buffer2DRegion<TPixel> region))
        {
            region.Buffer.Dispose();
        }
    }

    /// <inheritdoc />
    public bool TryReadRegion<TPixel>(
        Configuration configuration,
        ICanvasFrame<TPixel> target,
        Rectangle sourceRectangle,
        Buffer2DRegion<TPixel> destination)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        Guard.NotNull(configuration, nameof(configuration));
        Guard.NotNull(destination.Buffer, nameof(destination));

        // CPU backend readback is available only when the target exposes CPU pixels.
        if (!target.TryGetCpuRegion(out Buffer2DRegion<TPixel> sourceRegion))
        {
            return false;
        }

        // Clamp the request to the target region to avoid out-of-range row slicing.
        Rectangle clipped = Rectangle.Intersect(
            new Rectangle(0, 0, sourceRegion.Width, sourceRegion.Height),
            sourceRectangle);

        if (clipped.Width <= 0 || clipped.Height <= 0)
        {
            return false;
        }

        int copyWidth = Math.Min(clipped.Width, destination.Width);
        int copyHeight = Math.Min(clipped.Height, destination.Height);
        for (int y = 0; y < copyHeight; y++)
        {
            sourceRegion.DangerousGetRowSpan(clipped.Y + y)
                .Slice(clipped.X, copyWidth)
                .CopyTo(destination.DangerousGetRowSpan(y));
        }

        return true;
    }
}
