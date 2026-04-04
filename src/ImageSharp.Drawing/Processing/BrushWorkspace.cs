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

    internal BrushWorkspace(MemoryAllocator allocator, int rowWidth)
    {
        int capacity = Math.Max(1, rowWidth);
        this.amountsOwner = allocator.Allocate<float>(capacity);
        this.overlaysOwner = allocator.Allocate<TPixel>(capacity);
        this.blendScratchOwner = allocator.Allocate<Vector4>(capacity * 3);
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
        this.amountsOwner.Dispose();
        this.overlaysOwner.Dispose();
        this.blendScratchOwner.Dispose();
    }
}
