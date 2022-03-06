// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using SixLabors.ImageSharp.Drawing.Utilities;
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

        public bool Equals(IBrush other)
        {
            if (other is SolidBrush sb)
            {
                return sb.Color.Equals(this.Color);
            }

            return false;
        }

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
            private readonly ThreadLocalBlenderBuffers<TPixel> blenderBuffers;
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
                this.blenderBuffers = new ThreadLocalBlenderBuffers<TPixel>(configuration.MemoryAllocator, source.Width, true);
            }

            /// <inheritdoc />
            public override void Apply(Span<float> scanline, int x, int y)
            {
                Span<TPixel> destinationRow = this.Target.PixelBuffer.DangerousGetRowSpan(y).Slice(x);

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
                    Span<float> amounts = this.blenderBuffers.AmountSpan.Slice(0, scanline.Length);

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
                    this.blenderBuffers.Dispose();
                }

                this.isDisposed = true;
            }
        }
    }
}
