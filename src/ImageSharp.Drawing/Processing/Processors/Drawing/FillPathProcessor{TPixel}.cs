// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Diagnostics.CodeAnalysis;
using SixLabors.ImageSharp.Drawing.Processing.Backends;
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
        Rectangle interest = Rectangle.Intersect(this.bounds, source.Bounds);
        if (interest.Equals(Rectangle.Empty))
        {
            return; // No effect inside image;
        }

        int minX = interest.Left;

        // The rasterizer always computes continuous coverage, then aliased mode quantizes coverage
        // in ProcessRasterizedScanline().
        int subpixelCount = FillPathProcessor.FixedRasterizerSubpixelCount;
        using BrushApplicator<TPixel> applicator = brush.CreateApplicator(configuration, graphicsOptions, source, this.bounds);
        MemoryAllocator allocator = this.Configuration.MemoryAllocator;
        IDrawingBackend drawingBackend = configuration.GetDrawingBackend();
        RasterizerOptions rasterizerOptions = new(
            interest,
            subpixelCount,
            shapeOptions.IntersectionRule,
            RasterizerSamplingOrigin.PixelBoundary);

        RasterizationState state = new(
            source,
            applicator,
            minX,
            graphicsOptions.Antialias,
            isSolidBrushWithoutBlending,
            solidBrushColor);

        drawingBackend.RasterizePath(
            this.path,
            rasterizerOptions,
            allocator,
            ref state,
            ProcessRasterizedScanline);
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

    private static void ProcessRasterizedScanline(int y, Span<float> scanline, ref RasterizationState state)
    {
        if (!state.Antialias)
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

            if (state.IsSolidBrushWithoutBlending && hasOnes != hasZeros)
            {
                if (hasOnes)
                {
                    state.Source.PixelBuffer.DangerousGetRowSpan(y).Slice(state.MinX, scanline.Length).Fill(state.SolidBrushColor);
                }

                return;
            }

            if (state.IsSolidBrushWithoutBlending && hasOnes)
            {
                FillOpaqueRuns(state.Source, y, state.MinX, scanline, state.SolidBrushColor);
                return;
            }
        }

        if (state.IsSolidBrushWithoutBlending)
        {
            ApplyCoverageRunsForOpaqueSolidBrush(state.Source, state.Applicator, scanline, state.MinX, y, state.SolidBrushColor);
        }
        else
        {
            ApplyNonZeroCoverageRuns(state.Applicator, scanline, state.MinX, y);
        }
    }

    private static void ApplyNonZeroCoverageRuns(BrushApplicator<TPixel> applicator, Span<float> scanline, int minX, int y)
    {
        int i = 0;
        while (i < scanline.Length)
        {
            while (i < scanline.Length && scanline[i] <= 0F)
            {
                i++;
            }

            int runStart = i;
            while (i < scanline.Length && scanline[i] > 0F)
            {
                i++;
            }

            int runLength = i - runStart;
            if (runLength > 0)
            {
                applicator.Apply(scanline.Slice(runStart, runLength), minX + runStart, y);
            }
        }
    }

    private static void ApplyCoverageRunsForOpaqueSolidBrush(
        ImageFrame<TPixel> source,
        BrushApplicator<TPixel> applicator,
        Span<float> scanline,
        int minX,
        int y,
        TPixel solidBrushColor)
    {
        Span<TPixel> destinationRow = source.PixelBuffer.DangerousGetRowSpan(y).Slice(minX, scanline.Length);
        int i = 0;

        while (i < scanline.Length)
        {
            while (i < scanline.Length && scanline[i] <= 0F)
            {
                i++;
            }

            int runStart = i;
            while (i < scanline.Length && scanline[i] > 0F)
            {
                i++;
            }

            int runEnd = i;
            if (runEnd <= runStart)
            {
                continue;
            }

            int opaqueStart = runStart;
            while (opaqueStart < runEnd && scanline[opaqueStart] < 1F)
            {
                opaqueStart++;
            }

            if (opaqueStart > runStart)
            {
                int prefixLength = opaqueStart - runStart;
                applicator.Apply(scanline.Slice(runStart, prefixLength), minX + runStart, y);
            }

            int opaqueEnd = runEnd;
            while (opaqueEnd > opaqueStart && scanline[opaqueEnd - 1] < 1F)
            {
                opaqueEnd--;
            }

            if (opaqueEnd > opaqueStart)
            {
                destinationRow.Slice(opaqueStart, opaqueEnd - opaqueStart).Fill(solidBrushColor);
            }

            if (runEnd > opaqueEnd)
            {
                int suffixLength = runEnd - opaqueEnd;
                applicator.Apply(scanline.Slice(opaqueEnd, suffixLength), minX + opaqueEnd, y);
            }
        }
    }

    private static void FillOpaqueRuns(ImageFrame<TPixel> source, int y, int minX, Span<float> scanline, TPixel solidBrushColor)
    {
        Span<TPixel> destinationRow = source.PixelBuffer.DangerousGetRowSpan(y).Slice(minX, scanline.Length);
        int i = 0;

        while (i < scanline.Length)
        {
            while (i < scanline.Length && scanline[i] <= 0F)
            {
                i++;
            }

            int runStart = i;
            while (i < scanline.Length && scanline[i] > 0F)
            {
                i++;
            }

            int runLength = i - runStart;
            if (runLength > 0)
            {
                destinationRow.Slice(runStart, runLength).Fill(solidBrushColor);
            }
        }
    }

    private readonly struct RasterizationState
    {
        public RasterizationState(
            ImageFrame<TPixel> source,
            BrushApplicator<TPixel> applicator,
            int minX,
            bool antialias,
            bool isSolidBrushWithoutBlending,
            TPixel solidBrushColor)
        {
            this.Source = source;
            this.Applicator = applicator;
            this.MinX = minX;
            this.Antialias = antialias;
            this.IsSolidBrushWithoutBlending = isSolidBrushWithoutBlending;
            this.SolidBrushColor = solidBrushColor;
        }

        public ImageFrame<TPixel> Source { get; }

        public BrushApplicator<TPixel> Applicator { get; }

        public int MinX { get; }

        public bool Antialias { get; }

        public bool IsSolidBrushWithoutBlending { get; }

        public TPixel SolidBrushColor { get; }
    }
}
