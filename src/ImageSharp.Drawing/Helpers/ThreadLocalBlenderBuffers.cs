// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Buffers;
using SixLabors.ImageSharp.Memory;

namespace SixLabors.ImageSharp.Drawing.Helpers;

/// <summary>
/// Provides per-thread scratch buffers used by brush applicators during blending.
/// </summary>
/// <typeparam name="TPixel">The target pixel type.</typeparam>
/// <remarks>
/// <para>
/// Each participating thread gets its own pair of scanline-sized buffers:
/// one for blend amounts (<see cref="float"/> values) and, optionally, one for overlay pixels.
/// </para>
/// <para>
/// This avoids per-row allocations while preventing cross-thread contention on shared buffers.
/// </para>
/// <para>
/// Instances must be disposed to release all thread-local allocations.
/// </para>
/// </remarks>
internal class ThreadLocalBlenderBuffers<TPixel> : IDisposable
    where TPixel : unmanaged, IPixel<TPixel>
{
    private readonly ThreadLocal<BufferOwner> data;

    /// <summary>
    /// Initializes a new instance of the <see cref="ThreadLocalBlenderBuffers{TPixel}"/> class.
    /// </summary>
    /// <param name="allocator">The allocator used to create per-thread buffers.</param>
    /// <param name="scanlineWidth">The required buffer length, in pixels.</param>
    /// <param name="amountBufferOnly">
    /// <see langword="true"/> to allocate only the amount buffer.
    /// Use this when blending does not require an intermediate overlay color buffer.
    /// </param>
    public ThreadLocalBlenderBuffers(MemoryAllocator allocator, int scanlineWidth, bool amountBufferOnly = false)
        => this.data = new ThreadLocal<BufferOwner>(() => new BufferOwner(allocator, scanlineWidth, amountBufferOnly), true);

    /// <summary>
    /// Gets the current thread's amount buffer.
    /// </summary>
    /// <remarks>
    /// The span length is equal to the configured scanline width.
    /// The returned span is thread-local and should only be used on the calling thread.
    /// </remarks>
    public Span<float> AmountSpan => this.data.Value!.AmountSpan;

    /// <summary>
    /// Gets the current thread's overlay color buffer.
    /// </summary>
    /// <remarks>
    /// When the instance was created with <c>amountBufferOnly=true</c>,
    /// this property returns an empty span.
    /// </remarks>
    public Span<TPixel> OverlaySpan => this.data.Value!.OverlaySpan;

    /// <inheritdoc/>
    public void Dispose()
    {
        foreach (BufferOwner d in this.data.Values)
        {
            d.Dispose();
        }

        this.data.Dispose();
    }

    /// <summary>
    /// Owns the actual memory buffers for a single thread.
    /// </summary>
    private sealed class BufferOwner : IDisposable
    {
        private readonly IMemoryOwner<float> amountBuffer;
        private readonly IMemoryOwner<TPixel>? overlayBuffer;

        /// <summary>
        /// Initializes a new instance of the <see cref="BufferOwner"/> class.
        /// </summary>
        /// <param name="allocator">The allocator used for memory ownership.</param>
        /// <param name="scanlineLength">The required buffer length, in pixels.</param>
        /// <param name="amountBufferOnly">
        /// <see langword="true"/> to omit overlay buffer allocation.
        /// </param>
        public BufferOwner(MemoryAllocator allocator, int scanlineLength, bool amountBufferOnly)
        {
            this.amountBuffer = allocator.Allocate<float>(scanlineLength);
            this.overlayBuffer = amountBufferOnly ? null : allocator.Allocate<TPixel>(scanlineLength);
        }

        /// <summary>
        /// Gets the per-thread amount buffer.
        /// </summary>
        public Span<float> AmountSpan => this.amountBuffer.Memory.Span;

        /// <summary>
        /// Gets the per-thread overlay buffer.
        /// </summary>
        /// <remarks>
        /// Returns an empty span when overlay storage was intentionally not allocated.
        /// </remarks>
        public Span<TPixel> OverlaySpan
        {
            get
            {
                if (this.overlayBuffer != null)
                {
                    return this.overlayBuffer.Memory.Span;
                }

                return [];
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            this.amountBuffer.Dispose();
            this.overlayBuffer?.Dispose();
        }
    }
}
