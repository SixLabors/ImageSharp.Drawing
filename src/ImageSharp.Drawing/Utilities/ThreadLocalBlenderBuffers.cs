// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Buffers;
using System.Threading;
using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Drawing.Utilities
{
    internal class ThreadLocalBlenderBuffers<TPixel> : IDisposable
        where TPixel : unmanaged, IPixel<TPixel>
    {
        private ThreadLocal<BufferOwner> data;

        // amountBufferOnly:true is for SolidBrush, which doesn't need the overlay buffer (it will be dummy)
        public ThreadLocalBlenderBuffers(MemoryAllocator allocator, int scanlineWidth, bool amountBufferOnly = false)
        {
            this.data = new ThreadLocal<BufferOwner>(() => new BufferOwner(allocator, scanlineWidth, amountBufferOnly), true);
        }

        public Span<float> AmountSpan => this.data.Value.AmountSpan;

        public Span<TPixel> OverlaySpan => this.data.Value.OverlaySpan;

        /// <inheritdoc />
        public void Dispose()
        {
            if (this.data == null)
            {
                return;
            }

            foreach (BufferOwner d in this.data.Values)
            {
                d.Dispose();
            }

            this.data.Dispose();
            this.data = null;
        }

        private sealed class BufferOwner : IDisposable
        {
            private readonly IMemoryOwner<float> amountBuffer;
            private readonly IMemoryOwner<TPixel> overlayBuffer;

            public BufferOwner(MemoryAllocator allocator, int scanlineLength, bool amountBufferOnly)
            {
                this.amountBuffer = allocator.Allocate<float>(scanlineLength);
                this.overlayBuffer = amountBufferOnly ? null : allocator.Allocate<TPixel>(scanlineLength);
            }

            public Span<float> AmountSpan => this.amountBuffer.Memory.Span;

            public Span<TPixel> OverlaySpan => this.overlayBuffer.Memory.Span;

            public void Dispose()
            {
                this.amountBuffer.Dispose();
                this.overlayBuffer?.Dispose();
            }
        }
    }
}
