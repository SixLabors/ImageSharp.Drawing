// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Processing.Backends;
using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.PixelFormats;
using SkiaSharp;

namespace SixLabors.ImageSharp.Drawing.Tests.Processing.Backends;

internal sealed class SkiaCoverageDrawingBackend : IDrawingBackend, IDisposable
{
    private readonly ConcurrentDictionary<int, SKBitmap> preparedCoverage = new();
    private int nextCoverageHandleId;
    private bool isDisposed;

    public int PrepareCoverageCallCount { get; private set; }

    public int CompositeCoverageCallCount { get; private set; }

    public int ReleaseCoverageCallCount { get; private set; }

    public int LiveCoverageCount => this.preparedCoverage.Count;

    public void FillPath<TPixel>(
        ICanvasFrame<TPixel> target,
        IPath path,
        Brush brush,
        GraphicsOptions graphicsOptions,
        in RasterizerOptions rasterizerOptions,
        DrawingCanvasBatcher<TPixel> batcher)
        where TPixel : unmanaged, IPixel<TPixel>
        => batcher.AddComposition(
            CompositionCommand.Create(
                path,
                brush,
                graphicsOptions,
                rasterizerOptions,
                target.Bounds.Location));

    public void FlushCompositions<TPixel>(
        Configuration configuration,
        ICanvasFrame<TPixel> target,
        CompositionScene compositionScene)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        if (compositionScene.Commands.Count == 0)
        {
            return;
        }

        List<CompositionBatch> preparedBatches = CompositionScenePlanner.CreatePreparedBatches(
            compositionScene.Commands,
            target.Bounds);
        for (int batchIndex = 0; batchIndex < preparedBatches.Count; batchIndex++)
        {
            CompositionBatch compositionBatch = preparedBatches[batchIndex];
            if (compositionBatch.Commands.Count == 0)
            {
                continue;
            }

            CompositionCoverageDefinition definition = compositionBatch.Definition;
            DrawingCoverageHandle coverageHandle = this.PrepareCoverage(
                definition.Path,
                definition.RasterizerOptions,
                configuration.MemoryAllocator,
                CoveragePreparationMode.Default);
            try
            {
                IReadOnlyList<PreparedCompositionCommand> commands = compositionBatch.Commands;
                for (int i = 0; i < commands.Count; i++)
                {
                    PreparedCompositionCommand composition = commands[i];
                    ICanvasFrame<TPixel> commandTarget = new CanvasRegionFrame<TPixel>(target, composition.DestinationRegion);

                    this.CompositeCoverage(
                        configuration,
                        commandTarget,
                        coverageHandle,
                        composition.SourceOffset,
                        composition.Brush,
                        composition.GraphicsOptions,
                        composition.BrushBounds);
                }
            }
            finally
            {
                this.ReleaseCoverage(coverageHandle);
            }
        }
    }

    public bool TryReadRegion<TPixel>(
        Configuration configuration,
        ICanvasFrame<TPixel> target,
        Rectangle sourceRectangle,
        [NotNullWhen(true)] out Image<TPixel>? image)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        image = null;
        return false;
    }

    public DrawingCoverageHandle PrepareCoverage(
        IPath path,
        in RasterizerOptions rasterizerOptions,
        MemoryAllocator allocator,
        CoveragePreparationMode preparationMode)
    {
        ArgumentNullException.ThrowIfNull(path);

        ArgumentNullException.ThrowIfNull(allocator);
        _ = preparationMode;

        this.PrepareCoverageCallCount++;

        Size size = rasterizerOptions.Interest.Size;
        if (size.Width <= 0 || size.Height <= 0)
        {
            return default;
        }

        SKImageInfo imageInfo = new(size.Width, size.Height, SKColorType.Alpha8, SKAlphaType.Unpremul);
        SKBitmap bitmap = new(imageInfo);
        using SKCanvas canvas = new(bitmap);
        canvas.Clear(SKColors.Transparent);

        if (rasterizerOptions.SamplingOrigin == RasterizerSamplingOrigin.PixelCenter)
        {
            canvas.Translate(0.5F, 0.5F);
        }

        using SKPath skPath = CreateSkPath(path, rasterizerOptions.Interest.Location, rasterizerOptions.IntersectionRule);
        using SKPaint paint = new()
        {
            Color = SKColors.White,
            Style = SKPaintStyle.Fill,
            IsAntialias = rasterizerOptions.RasterizationMode == RasterizationMode.Antialiased
        };

        canvas.DrawPath(skPath, paint);

        int handleId = Interlocked.Increment(ref this.nextCoverageHandleId);
        if (!this.preparedCoverage.TryAdd(handleId, bitmap))
        {
            bitmap.Dispose();
            throw new InvalidOperationException("Failed to cache prepared coverage.");
        }

        return new DrawingCoverageHandle(handleId);
    }

    public void CompositeCoverage<TPixel>(
        Configuration configuration,
        ICanvasFrame<TPixel> target,
        DrawingCoverageHandle coverageHandle,
        Point sourceOffset,
        Brush brush,
        in GraphicsOptions graphicsOptions,
        Rectangle brushBounds)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        ArgumentNullException.ThrowIfNull(configuration);

        ArgumentNullException.ThrowIfNull(brush);

        this.CompositeCoverageCallCount++;

        if (!coverageHandle.IsValid)
        {
            return;
        }

        if (!this.preparedCoverage.TryGetValue(coverageHandle.Value, out SKBitmap bitmap))
        {
            throw new InvalidOperationException($"Prepared coverage handle '{coverageHandle.Value}' is not valid.");
        }

        if (!target.TryGetCpuRegion(out Buffer2DRegion<TPixel> destinationRegion))
        {
            throw new NotSupportedException(
                $"{nameof(SkiaCoverageDrawingBackend)} requires CPU-accessible frame targets for {nameof(this.CompositeCoverage)}.");
        }

        if (bitmap.ColorType != SKColorType.Alpha8)
        {
            throw new InvalidOperationException($"Prepared coverage '{coverageHandle.Value}' is not Alpha8.");
        }

        if ((uint)sourceOffset.X >= (uint)bitmap.Width || (uint)sourceOffset.Y >= (uint)bitmap.Height)
        {
            return;
        }

        int compositeWidth = Math.Min(destinationRegion.Width, bitmap.Width - sourceOffset.X);
        int compositeHeight = Math.Min(destinationRegion.Height, bitmap.Height - sourceOffset.Y);
        if (compositeWidth <= 0 || compositeHeight <= 0)
        {
            return;
        }

        using BrushApplicator<TPixel> applicator = brush.CreateApplicator(
            configuration,
            graphicsOptions,
            destinationRegion,
            brushBounds);

        ReadOnlySpan<byte> source = bitmap.GetPixelSpan();
        int rowBytes = bitmap.RowBytes;
        int absoluteX = destinationRegion.Rectangle.X;
        int absoluteY = destinationRegion.Rectangle.Y;

        float[] rented = ArrayPool<float>.Shared.Rent(compositeWidth);
        try
        {
            Span<float> coverage = rented.AsSpan(0, compositeWidth);
            for (int row = 0; row < compositeHeight; row++)
            {
                int srcRow = (sourceOffset.Y + row) * rowBytes;
                int srcOffset = srcRow + sourceOffset.X;
                for (int x = 0; x < compositeWidth; x++)
                {
                    coverage[x] = source[srcOffset + x] / 255F;
                }

                applicator.Apply(coverage, absoluteX, absoluteY + row);
            }
        }
        finally
        {
            ArrayPool<float>.Shared.Return(rented);
        }
    }

    public void ReleaseCoverage(DrawingCoverageHandle coverageHandle)
    {
        this.ReleaseCoverageCallCount++;

        if (!coverageHandle.IsValid)
        {
            return;
        }

        if (this.preparedCoverage.TryRemove(coverageHandle.Value, out SKBitmap bitmap))
        {
            bitmap.Dispose();
        }
    }

    public void Dispose()
    {
        if (this.isDisposed)
        {
            return;
        }

        foreach (KeyValuePair<int, SKBitmap> kv in this.preparedCoverage)
        {
            kv.Value.Dispose();
        }

        this.preparedCoverage.Clear();
        this.isDisposed = true;
    }

    private static SKPath CreateSkPath(IPath path, Point interestLocation, IntersectionRule intersectionRule)
    {
        SKPath skPath = new()
        {
            FillType = intersectionRule == IntersectionRule.EvenOdd
                ? SKPathFillType.EvenOdd
                : SKPathFillType.Winding
        };

        float offsetX = -interestLocation.X;
        float offsetY = -interestLocation.Y;

        foreach (ISimplePath simplePath in path.Flatten())
        {
            ReadOnlySpan<PointF> points = simplePath.Points.Span;
            if (points.Length == 0)
            {
                continue;
            }

            SKPoint[] skPoints = new SKPoint[points.Length];
            for (int i = 0; i < points.Length; i++)
            {
                skPoints[i] = new SKPoint(points[i].X + offsetX, points[i].Y + offsetY);
            }

            skPath.AddPoly(skPoints, true);
        }

        return skPath;
    }
}
