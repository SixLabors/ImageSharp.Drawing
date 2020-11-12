// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Buffers;
using SixLabors.ImageSharp.Drawing.Shapes.Rasterization;
using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing.Processors;

namespace SixLabors.ImageSharp.Drawing.Processing.Processors.Drawing
{
    /// <summary>
    /// Using a brush and a shape fills shape with contents of brush the
    /// </summary>
    /// <typeparam name="TPixel">The type of the color.</typeparam>
    /// <seealso cref="ImageProcessor{TPixel}" />
    internal class FillRegionProcessor<TPixel> : ImageProcessor<TPixel>
        where TPixel : unmanaged, IPixel<TPixel>
    {
        private readonly FillRegionProcessor definition;

        public FillRegionProcessor(Configuration configuration, FillRegionProcessor definition, Image<TPixel> source, Rectangle sourceRectangle)
            : base(configuration, source, sourceRectangle)
        {
            this.definition = definition;
        }

        /// <inheritdoc/>
        protected override void OnFrameApply(ImageFrame<TPixel> source)
        {
            Configuration configuration = this.Configuration;
            ShapeOptions shapeOptions = this.definition.Options.ShapeOptions;
            GraphicsOptions graphicsOptions = this.definition.Options.GraphicsOptions;
            IBrush brush = this.definition.Brush;
            Region region = this.definition.Region;
            Rectangle rect = region.Bounds;

            bool isSolidBrushWithoutBlending = IsSolidBrushWithoutBlending(graphicsOptions, this.definition.Brush, out SolidBrush solidBrush);
            TPixel solidBrushColor = isSolidBrushWithoutBlending ? solidBrush.Color.ToPixel<TPixel>() : default;

            // Align start/end positions.
            int minX = Math.Max(0, rect.Left);
            int maxX = Math.Min(source.Width, rect.Right);
            int minY = Math.Max(0, rect.Top);
            int maxY = Math.Min(source.Height, rect.Bottom);
            if (minX >= maxX)
            {
                return; // no effect inside image;
            }

            if (minY >= maxY)
            {
                return; // no effect inside image;
            }

            int subpixelCount = FillRegionProcessor.MinimumSubpixelCount;

            // we need to offset the pixel grid to account for when we outline a path.
            // basically if the line is [1,2] => [3,2] then when outlining at 1 we end up with a region of [0.5,1.5],[1.5, 1.5],[3.5,2.5],[2.5,2.5]
            // and this can cause missed fills when not using antialiasing.so we offset the pixel grid by 0.5 in the x & y direction thus causing the#
            // region to align with the pixel grid.
            if (graphicsOptions.Antialias)
            {
                subpixelCount = Math.Max(subpixelCount, graphicsOptions.AntialiasSubpixelDepth);
            }

            using BrushApplicator<TPixel> applicator = brush.CreateApplicator(configuration, graphicsOptions, source, rect);
            int scanlineWidth = maxX - minX;
            MemoryAllocator allocator = this.Configuration.MemoryAllocator;
            bool scanlineDirty = true;

            var scanner = PolygonScanner.Create(
                region,
                minY,
                maxY,
                subpixelCount,
                shapeOptions.IntersectionRule,
                configuration,
                shapeOptions.OrientationHandling);

            try
            {
                using IMemoryOwner<float> bScanline = allocator.Allocate<float>(scanlineWidth);
                Span<float> scanline = bScanline.Memory.Span;

                while (scanner.MoveToNextPixelLine())
                {
                    if (scanlineDirty)
                    {
                        scanline.Clear();
                    }

                    scanlineDirty = scanner.ScanCurrentPixelLineInto(minX, 0, scanline);

                    if (scanlineDirty)
                    {
                        int y = scanner.PixelLineY;
                        if (!graphicsOptions.Antialias)
                        {
                            bool hasOnes = false;
                            bool hasZeros = false;
                            for (int x = 0; x < scanline.Length; x++)
                            {
                                if (scanline[x] >= 0.5)
                                {
                                    scanline[x] = 1;
                                    hasOnes = true;
                                }
                                else
                                {
                                    scanline[x] = 0;
                                    hasZeros = true;
                                }
                            }

                            if (isSolidBrushWithoutBlending && hasOnes != hasZeros)
                            {
                                if (hasOnes)
                                {
                                    source.GetPixelRowSpan(y).Slice(minX, scanlineWidth).Fill(solidBrushColor);
                                }

                                continue;
                            }
                        }

                        applicator.Apply(scanline, minX, y);
                    }
                }
            }
            finally
            {
                // ref structs can't implement interfaces so technically PolygonScanner is not IDisposable
                scanner.Dispose();
            }
        }

        private static bool IsSolidBrushWithoutBlending(GraphicsOptions options, IBrush inputBrush, out SolidBrush solidBrush)
        {
            solidBrush = inputBrush as SolidBrush;

            if (solidBrush == null)
            {
                return false;
            }

            return options.IsOpaqueColorWithoutBlending(solidBrush.Color);
        }
    }
}
