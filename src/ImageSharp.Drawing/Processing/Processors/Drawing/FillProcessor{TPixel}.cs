// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Buffers;
using System.Runtime.CompilerServices;
using SixLabors.ImageSharp.Advanced;
using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing.Processors;

namespace SixLabors.ImageSharp.Drawing.Processing.Processors.Drawing
{
    /// <summary>
    /// Using the brush as a source of pixels colors blends the brush color with source.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    internal class FillProcessor<TPixel> : ImageProcessor<TPixel>
        where TPixel : unmanaged, IPixel<TPixel>
    {
        private readonly FillProcessor definition;

        public FillProcessor(Configuration configuration, FillProcessor definition, Image<TPixel> source, Rectangle sourceRectangle)
            : base(configuration, source, sourceRectangle)
            => this.definition = definition;

        /// <inheritdoc/>
        protected override void OnFrameApply(ImageFrame<TPixel> source)
        {
            var interest = Rectangle.Intersect(this.SourceRectangle, source.Bounds());
            if (interest.Width == 0 || interest.Height == 0)
            {
                return;
            }

            Configuration configuration = this.Configuration;
            IBrush brush = this.definition.Brush;
            GraphicsOptions options = this.definition.Options.GraphicsOptions;

            // If there's no reason for blending, then avoid it.
            if (this.IsSolidBrushWithoutBlending(out SolidBrush solidBrush))
            {
                ParallelExecutionSettings parallelSettings = ParallelExecutionSettings.FromConfiguration(configuration)
                    .MultiplyMinimumPixelsPerTask(4);

                TPixel colorPixel = solidBrush.Color.ToPixel<TPixel>();

                var solidOperation = new SolidBrushRowIntervalOperation(interest, source, colorPixel);
                ParallelRowIterator.IterateRowIntervals(
                    interest,
                    parallelSettings,
                    in solidOperation);

                return;
            }

            using IMemoryOwner<float> amount = configuration.MemoryAllocator.Allocate<float>(interest.Width);
            using BrushApplicator<TPixel> applicator = brush.CreateApplicator(
                configuration,
                options,
                source,
                interest);

            amount.Memory.Span.Fill(1F);

            var operation = new RowIntervalOperation(interest, applicator, amount.Memory);
            ParallelRowIterator.IterateRowIntervals(
                configuration,
                interest,
                in operation);
        }

        private bool IsSolidBrushWithoutBlending(out SolidBrush solidBrush)
        {
            solidBrush = this.definition.Brush as SolidBrush;

            if (solidBrush is null)
            {
                return false;
            }

            return this.definition.Options.GraphicsOptions.IsOpaqueColorWithoutBlending(solidBrush.Color);
        }

        private readonly struct SolidBrushRowIntervalOperation : IRowIntervalOperation
        {
            private readonly Rectangle bounds;
            private readonly ImageFrame<TPixel> source;
            private readonly TPixel color;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public SolidBrushRowIntervalOperation(Rectangle bounds, ImageFrame<TPixel> source, TPixel color)
            {
                this.bounds = bounds;
                this.source = source;
                this.color = color;
            }

            /// <inheritdoc/>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Invoke(in RowInterval rows)
            {
                for (int y = rows.Min; y < rows.Max; y++)
                {
                    this.source.PixelBuffer.DangerousGetRowSpan(y).Slice(this.bounds.X, this.bounds.Width).Fill(this.color);
                }
            }
        }

        private readonly struct RowIntervalOperation : IRowIntervalOperation
        {
            private readonly Memory<float> amount;
            private readonly Rectangle bounds;
            private readonly BrushApplicator<TPixel> applicator;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public RowIntervalOperation(Rectangle bounds, BrushApplicator<TPixel> applicator, Memory<float> amount)
            {
                this.bounds = bounds;
                this.applicator = applicator;
                this.amount = amount;
            }

            /// <inheritdoc/>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Invoke(in RowInterval rows)
            {
                Span<float> amountSpan = this.amount.Span;
                int x = this.bounds.X;
                for (int y = rows.Min; y < rows.Max; y++)
                {
                    this.applicator.Apply(amountSpan, x, y);
                }
            }
        }
    }
}
