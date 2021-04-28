// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Buffers;
using System.Threading;
using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Drawing.Processing
{
    /// <summary>
    /// Provides an implementation of a solid brush for painting solid color areas.
    /// </summary>
    public sealed class SolidBrush : IBrush
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SolidBrush"/> class.
        /// </summary>
        /// <param name="color">The color.</param>
        public SolidBrush(Color color) => this.Color = color;

        /// <summary>
        /// Gets the color.
        /// </summary>
        public Color Color { get; }

        /// <inheritdoc />
        public BrushApplicator<TPixel> CreateApplicator<TPixel>(
            Configuration configuration,
            GraphicsOptions options,
            ImageFrame<TPixel> source,
            RectangleF region)
            where TPixel : unmanaged, IPixel<TPixel>
            => new SolidBrushApplicator<TPixel>(configuration, options, source, this.Color.ToPixel<TPixel>());

        /// <summary>
        /// The solid brush applicator.
        /// </summary>
        /// <typeparam name="TPixel">The pixel format.</typeparam>
        private sealed class SolidBrushApplicator<TPixel> : BrushApplicator<TPixel>
            where TPixel : unmanaged, IPixel<TPixel>
        {
            private readonly IMemoryOwner<TPixel> colors;
            private readonly MemoryAllocator allocator;
            private readonly int scalineWidth;
            private readonly ThreadLocal<ThreadContextData> threadContextData;
            private bool isDisposed;

            /// <summary>
            /// Initializes a new instance of the <see cref="SolidBrushApplicator{TPixel}"/> class.
            /// </summary>
            /// <param name="configuration">The configuration instance to use when performing operations.</param>
            /// <param name="options">The graphics options.</param>
            /// <param name="source">The source image.</param>
            /// <param name="color">The color.</param>
            public SolidBrushApplicator(
                Configuration configuration,
                GraphicsOptions options,
                ImageFrame<TPixel> source,
                TPixel color)
                : base(configuration, options, source)
            {
                this.colors = configuration.MemoryAllocator.Allocate<TPixel>(source.Width);
                this.colors.Memory.Span.Fill(color);
                this.scalineWidth = source.Width;
                this.allocator = configuration.MemoryAllocator;

                // The threadlocal value is lazily invoked so there is no need to optionally create the type.
                this.threadContextData = new ThreadLocal<ThreadContextData>(
                    () => new ThreadContextData(this.allocator, this.scalineWidth),
                    true);
            }

            /// <inheritdoc />
            public override void Apply(Span<float> scanline, int x, int y)
            {
                Span<TPixel> destinationRow = this.Target.GetPixelRowSpan(y).Slice(x);

                // Constrain the spans to each other
                if (destinationRow.Length > scanline.Length)
                {
                    destinationRow = destinationRow.Slice(0, scanline.Length);
                }
                else
                {
                    scanline = scanline.Slice(0, destinationRow.Length);
                }

                Configuration configuration = this.Configuration;
                if (this.Options.BlendPercentage == 1F)
                {
                    // TODO: refactor the BlendPercentage == 1 logic to a separate, simpler BrushApplicator class.
                    this.Blender.Blend(configuration, destinationRow, destinationRow, this.colors.Memory.Span, scanline);
                }
                else
                {
                    ThreadContextData contextData = this.threadContextData.Value;
                    Span<float> amounts = contextData.AmountSpan.Slice(0, scanline.Length);

                    for (int i = 0; i < scanline.Length; i++)
                    {
                        amounts[i] = scanline[i] * this.Options.BlendPercentage;
                    }

                    this.Blender.Blend(
                        configuration,
                        destinationRow,
                        destinationRow,
                        this.colors.Memory.Span,
                        amounts);
                }
            }

            /// <inheritdoc />
            protected override void Dispose(bool disposing)
            {
                if (this.isDisposed)
                {
                    return;
                }

                if (disposing)
                {
                    this.colors.Dispose();
                    foreach (ThreadContextData data in this.threadContextData.Values)
                    {
                        data.Dispose();
                    }

                    this.threadContextData.Dispose();
                }

                this.isDisposed = true;
            }

            private sealed class ThreadContextData : IDisposable
            {
                private bool isDisposed;
                private readonly IMemoryOwner<float> amountBuffer;

                public ThreadContextData(MemoryAllocator allocator, int scanlineLength)
                    => this.amountBuffer = allocator.Allocate<float>(scanlineLength);

                public Span<float> AmountSpan => this.amountBuffer.Memory.Span;

                public void Dispose()
                {
                    if (!this.isDisposed)
                    {
                        this.isDisposed = true;
                        this.amountBuffer.Dispose();
                    }
                }
            }
        }
    }
}
