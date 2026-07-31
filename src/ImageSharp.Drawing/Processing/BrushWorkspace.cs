// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Buffers;
using System.Numerics;
using SixLabors.ImageSharp.Memory;

namespace SixLabors.ImageSharp.Drawing.Processing;

/// <summary>
/// Worker-local scratch workspace used by prepared brushes during row composition.
/// </summary>
/// <typeparam name="TPixel">The target pixel format.</typeparam>
public sealed class BrushWorkspace<TPixel> : IDisposable
    where TPixel : unmanaged, IPixel<TPixel>
{
    private readonly IMemoryOwner<float> amountsOwner;
    private readonly IMemoryOwner<TPixel> overlaysOwner;
    private readonly IMemoryOwner<Vector4> blendScratchOwner;
    private bool isDisposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="BrushWorkspace{TPixel}"/> class.
    /// </summary>
    /// <param name="allocator">The memory allocator used to rent the pooled buffers.</param>
    /// <param name="rowWidth">The maximum row width, in pixels, the workspace must be able to service.</param>
    internal BrushWorkspace(MemoryAllocator allocator, int rowWidth)
    {
        int capacity = Math.Max(1, rowWidth);
        IMemoryOwner<float>? amounts = null;
        IMemoryOwner<TPixel>? overlays = null;
        try
        {
            amounts = allocator.Allocate<float>(capacity);
            overlays = allocator.Allocate<TPixel>(capacity);

            // Callers request at most three vector rows per pixel (see GetBlendScratch),
            // so reserve the worst case once instead of reallocating per request.
            this.blendScratchOwner = allocator.Allocate<Vector4>(capacity * 3);
            this.amountsOwner = amounts;
            this.overlaysOwner = overlays;
        }
        catch
        {
            // A later allocation throwing strands the earlier rentals: the instance never
            // escapes, so nothing else can return them.
            amounts?.Dispose();
            overlays?.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Gets the shared amount buffer for the requested length.
    /// </summary>
    /// <param name="length">The number of elements required.</param>
    /// <returns>A slice of the worker-local pooled amount buffer.</returns>
    public Span<float> GetAmounts(int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        return this.amountsOwner.Memory.Span[..length];
    }

    /// <summary>
    /// Gets the shared overlay buffer for the requested length.
    /// </summary>
    /// <param name="length">The number of elements required.</param>
    /// <returns>A slice of the worker-local pooled overlay buffer.</returns>
    public Span<TPixel> GetOverlays(int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        return this.overlaysOwner.Memory.Span[..length];
    }

    /// <summary>
    /// Gets the shared vector scratch for the requested row length and vector row count.
    /// </summary>
    /// <param name="length">The number of pixels in the row.</param>
    /// <param name="vectorRows">The number of temporary vector rows required.</param>
    /// <returns>A slice of the worker-local pooled vector scratch buffer.</returns>
    public Span<Vector4> GetBlendScratch(int length, int vectorRows)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        ArgumentOutOfRangeException.ThrowIfLessThan(vectorRows, 1);
        return this.blendScratchOwner.Memory.Span[..(length * vectorRows)];
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (this.isDisposed)
        {
            return;
        }

        this.isDisposed = true;
        this.amountsOwner.Dispose();
        this.overlaysOwner.Dispose();
        this.blendScratchOwner.Dispose();
    }
}
