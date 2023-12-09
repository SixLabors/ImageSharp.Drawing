// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using SixLabors.ImageSharp.Drawing.Shapes.Rasterization;
using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.Processing.Processors;

namespace SixLabors.ImageSharp.Drawing.Processing.Processors.Drawing;

/// <summary>
/// Uses a brush and a shape to fill the shape with contents of the brush.
/// </summary>
/// <typeparam name="TPixel">The type of the color.</typeparam>
/// <seealso cref="ImageProcessor{TPixel}" />
internal class FillPathProcessor<TPixel> : ImageProcessor<TPixel>
    where TPixel : unmanaged, IPixel<TPixel>
{
    private readonly FillPathProcessor definition;
    private readonly IPath path;
    private readonly Rectangle bounds;

    public FillPathProcessor(
        Configuration configuration,
        FillPathProcessor definition,
        Image<TPixel> source,
        Rectangle sourceRectangle)
        : base(configuration, source, sourceRectangle)
    {
        IPath path = definition.Region;
        int left = (int)MathF.Floor(path.Bounds.Left);
        int top = (int)MathF.Floor(path.Bounds.Top);
        int right = (int)MathF.Ceiling(path.Bounds.Right);
        int bottom = (int)MathF.Ceiling(path.Bounds.Bottom);

        this.bounds = Rectangle.FromLTRB(left, top, right, bottom);
        this.path = path.AsClosedPath();
        this.definition = definition;
    }

    /// <inheritdoc/>
    protected override void OnFrameApply(ImageFrame<TPixel> source)
    {
        Configuration configuration = this.Configuration;
        ShapeOptions shapeOptions = this.definition.Options.ShapeOptions;
        GraphicsOptions graphicsOptions = this.definition.Options.GraphicsOptions;
        Brush brush = this.definition.Brush;

        TPixel solidBrushColor = default;
        bool isSolidBrushWithoutBlending = false;
        if (IsSolidBrushWithoutBlending(graphicsOptions, brush, out SolidBrush? solidBrush))
        {
            isSolidBrushWithoutBlending = true;
            solidBrushColor = solidBrush.Color.ToPixel<TPixel>();
        }

        // Align start/end positions.
        Rectangle interest = Rectangle.Intersect(this.bounds, source.Bounds());
        if (interest.Equals(Rectangle.Empty))
        {
            return; // No effect inside image;
        }

        int minX = interest.Left;
        int subpixelCount = FillPathProcessor.MinimumSubpixelCount;

        // We need to offset the pixel grid to account for when we outline a path.
        // basically if the line is [1,2] => [3,2] then when outlining at 1 we end up with a region of [0.5,1.5],[1.5, 1.5],[3.5,2.5],[2.5,2.5]
        // and this can cause missed fills when not using antialiasing.so we offset the pixel grid by 0.5 in the x & y direction thus causing the#
        // region to align with the pixel grid.
        if (graphicsOptions.Antialias)
        {
            subpixelCount = Math.Max(subpixelCount, graphicsOptions.AntialiasSubpixelDepth);
        }

        using BrushApplicator<TPixel> applicator = brush.CreateApplicator(configuration, graphicsOptions, source, this.bounds);
        int scanlineWidth = interest.Width;
        MemoryAllocator allocator = this.Configuration.MemoryAllocator;
        bool scanlineDirty = true;

        PolygonScanner scanner = PolygonScanner.Create(
            this.path,
            interest.Top,
            interest.Bottom,
            subpixelCount,
            shapeOptions.IntersectionRule,
            configuration.MemoryAllocator);

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

                scanlineDirty = scanner.ScanCurrentPixelLineInto(minX, 0F, scanline);

                if (scanlineDirty)
                {
                    int y = scanner.PixelLineY;
                    if (!graphicsOptions.Antialias)
                    {
                        bool hasOnes = false;
                        bool hasZeros = false;
                        for (int x = 0; x < scanline.Length; x++)
                        {
                            if (scanline[x] >= 0.5F)
                            {
                                scanline[x] = 1F;
                                hasOnes = true;
                            }
                            else
                            {
                                scanline[x] = 0F;
                                hasZeros = true;
                            }
                        }

                        if (isSolidBrushWithoutBlending && hasOnes != hasZeros)
                        {
                            if (hasOnes)
                            {
                                source.PixelBuffer.DangerousGetRowSpan(y).Slice(minX, scanlineWidth).Fill(solidBrushColor);
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
            scanner.Dispose();
        }
    }

    private static bool IsSolidBrushWithoutBlending(GraphicsOptions options, Brush inputBrush, [NotNullWhen(true)] out SolidBrush? solidBrush)
    {
        solidBrush = inputBrush as SolidBrush;

        if (solidBrush == null)
        {
            return false;
        }

        return options.IsOpaqueColorWithoutBlending(solidBrush.Color);
    }
}
