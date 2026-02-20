// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.CompilerServices;
using SixLabors.Fonts.Rendering;
using SixLabors.ImageSharp.Drawing.Processing.Backends;
using SixLabors.ImageSharp.Drawing.Processing.Processors.Text;
using SixLabors.ImageSharp.Drawing.Shapes.Rasterization;
using SixLabors.ImageSharp.Drawing.Text;
using SixLabors.ImageSharp.Memory;

namespace SixLabors.ImageSharp.Drawing.Processing;

/// <summary>
/// A drawing canvas over a pixel buffer region.
/// </summary>
/// <typeparam name="TPixel">The pixel format.</typeparam>
public sealed class DrawingCanvas<TPixel> : IDisposable
    where TPixel : unmanaged, IPixel<TPixel>
{
    private readonly Configuration configuration;
    private readonly IDrawingBackend backend;
    private readonly Buffer2DRegion<TPixel> targetRegion;
    private bool isDisposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="DrawingCanvas{TPixel}"/> class.
    /// </summary>
    /// <param name="configuration">The active processing configuration.</param>
    /// <param name="targetRegion">The destination target region.</param>
    public DrawingCanvas(Configuration configuration, Buffer2DRegion<TPixel> targetRegion)
        : this(configuration, configuration.GetDrawingBackend(), targetRegion)
    {
    }

    internal DrawingCanvas(
        Configuration configuration,
        IDrawingBackend backend,
        Buffer2DRegion<TPixel> targetRegion)
    {
        Guard.NotNull(configuration, nameof(configuration));
        Guard.NotNull(targetRegion.Buffer, nameof(targetRegion));
        Guard.NotNull(backend, nameof(backend));

        this.configuration = configuration;
        this.backend = backend;
        this.targetRegion = targetRegion;
        this.Bounds = new Rectangle(0, 0, targetRegion.Width, targetRegion.Height);
    }

    /// <summary>
    /// Gets the local bounds of this canvas.
    /// </summary>
    public Rectangle Bounds { get; }

    /// <summary>
    /// Creates a child canvas over a subregion in local coordinates.
    /// </summary>
    /// <param name="region">The child region in local coordinates.</param>
    /// <returns>A child canvas with local origin at (0,0).</returns>
    public DrawingCanvas<TPixel> CreateRegion(Rectangle region)
    {
        this.EnsureNotDisposed();

        Rectangle clipped = Rectangle.Intersect(this.Bounds, region);
        Buffer2DRegion<TPixel> childRegion = this.targetRegion.GetSubRegion(clipped);
        return new DrawingCanvas<TPixel>(this.configuration, this.backend, childRegion);
    }

    /// <summary>
    /// Fills the whole canvas using the given brush.
    /// </summary>
    /// <param name="brush">Brush used to shade destination pixels.</param>
    /// <param name="graphicsOptions">Graphics blending/composition options.</param>
    public void Fill(Brush brush, GraphicsOptions graphicsOptions)
        => this.FillRegion(this.Bounds, brush, graphicsOptions);

    /// <summary>
    /// Fills a local region using the given brush.
    /// </summary>
    /// <param name="region">Region to fill in local coordinates.</param>
    /// <param name="brush">Brush used to shade destination pixels.</param>
    /// <param name="graphicsOptions">Graphics blending/composition options.</param>
    public void FillRegion(Rectangle region, Brush brush, GraphicsOptions graphicsOptions)
    {
        this.EnsureNotDisposed();
        Guard.NotNull(brush, nameof(brush));
        Guard.NotNull(graphicsOptions, nameof(graphicsOptions));

        this.backend.FillRegion(this.configuration, this.targetRegion, brush, graphicsOptions, region);
    }

    /// <summary>
    /// Fills a path in local coordinates using the given brush.
    /// </summary>
    /// <param name="path">The path to fill.</param>
    /// <param name="brush">Brush used to shade covered pixels.</param>
    /// <param name="options">Drawing options for fill and rasterization behavior.</param>
    public void FillPath(IPath path, Brush brush, DrawingOptions options)
        => this.FillPath(path, brush, options, RasterizerSamplingOrigin.PixelBoundary);

    internal void FillPath(
        IPath path,
        Brush brush,
        DrawingOptions options,
        RasterizerSamplingOrigin samplingOrigin)
    {
        this.EnsureNotDisposed();
        Guard.NotNull(path, nameof(path));
        Guard.NotNull(brush, nameof(brush));
        Guard.NotNull(options, nameof(options));

        GraphicsOptions graphicsOptions = options.GraphicsOptions;
        ShapeOptions shapeOptions = options.ShapeOptions;
        RasterizationMode rasterizationMode = graphicsOptions.Antialias ? RasterizationMode.Antialiased : RasterizationMode.Aliased;

        RectangleF bounds = path.Bounds;
        if (samplingOrigin == RasterizerSamplingOrigin.PixelCenter)
        {
            // Keep rasterizer interest aligned with center-sampled scan conversion.
            bounds = new RectangleF(bounds.X + 0.5F, bounds.Y + 0.5F, bounds.Width, bounds.Height);
        }

        Rectangle interest = Rectangle.FromLTRB(
            (int)MathF.Floor(bounds.Left),
            (int)MathF.Floor(bounds.Top),
            (int)MathF.Ceiling(bounds.Right),
            (int)MathF.Ceiling(bounds.Bottom));

        RasterizerOptions rasterizerOptions = new(
            interest,
            shapeOptions.IntersectionRule,
            rasterizationMode,
            samplingOrigin);

        this.backend.FillPath(this.configuration, this.targetRegion, path, brush, graphicsOptions, rasterizerOptions);
    }

    /// <summary>
    /// Draws a path outline in local coordinates using the given pen.
    /// </summary>
    /// <param name="path">The path to stroke.</param>
    /// <param name="pen">Pen used to generate the outline fill path.</param>
    /// <param name="options">Drawing options for stroke fill and rasterization behavior.</param>
    public void DrawPath(IPath path, Pen pen, DrawingOptions options)
    {
        this.EnsureNotDisposed();
        Guard.NotNull(path, nameof(path));
        Guard.NotNull(pen, nameof(pen));
        Guard.NotNull(options, nameof(options));

        IPath outline = pen.GeneratePath(path);

        DrawingOptions effectiveOptions = options;

        // Non-normalized stroke output can self-overlap; non-zero winding preserves stroke semantics.
        if (!pen.StrokeOptions.NormalizeOutput &&
            options.ShapeOptions.IntersectionRule != IntersectionRule.NonZero)
        {
            ShapeOptions shapeOptions = options.ShapeOptions.DeepClone();
            shapeOptions.IntersectionRule = IntersectionRule.NonZero;
            effectiveOptions = new DrawingOptions(options.GraphicsOptions, shapeOptions, options.Transform);
        }

        this.FillPath(outline, pen.StrokeFill, effectiveOptions, RasterizerSamplingOrigin.PixelCenter);
    }

    /// <summary>
    /// Draws text onto this canvas.
    /// </summary>
    /// <param name="textOptions">The text rendering options.</param>
    /// <param name="text">The text to draw.</param>
    /// <param name="drawingOptions">Drawing options defining blending and shape behavior.</param>
    /// <param name="brush">Optional brush used to fill glyphs.</param>
    /// <param name="pen">Optional pen used to outline glyphs.</param>
    public void DrawText(
        RichTextOptions textOptions,
        string text,
        DrawingOptions drawingOptions,
        Brush? brush,
        Pen? pen)
    {
        this.EnsureNotDisposed();
        Guard.NotNull(textOptions, nameof(textOptions));
        Guard.NotNull(text, nameof(text));
        Guard.NotNull(drawingOptions, nameof(drawingOptions));

        if (brush is null && pen is null)
        {
            throw new ArgumentException($"Expected a {nameof(brush)} or {nameof(pen)}. Both were null");
        }

        RichTextOptions configuredOptions = ConfigureTextOptions(textOptions);
        using RichTextGlyphRenderer textRenderer = new(configuredOptions, drawingOptions, pen, brush);
        TextRenderer renderer = new(textRenderer);
        renderer.RenderText(text, configuredOptions);

        this.DrawTextOperations(textRenderer.DrawingOperations, drawingOptions);
    }

    private void DrawTextOperations(IEnumerable<DrawingOperation> operations, DrawingOptions drawingOptions)
    {
        this.EnsureNotDisposed();
        Guard.NotNull(operations, nameof(operations));
        Guard.NotNull(drawingOptions, nameof(drawingOptions));

        Dictionary<OperationCoverageCacheKey, CoverageCacheEntry> coverageCache = [];
        this.backend.BeginCompositeSession(this.configuration, this.targetRegion);
        try
        {
            // Operations are layered by render pass (fill, outline, decorations).
            foreach (DrawingOperation operation in operations.OrderBy(x => x.RenderPass))
            {
                Brush? compositeBrush = GetCompositeBrush(operation);
                if (compositeBrush is null)
                {
                    continue;
                }

                GraphicsOptions graphicsOptions =
                    drawingOptions.GraphicsOptions.CloneOrReturnForRules(
                        operation.PixelAlphaCompositionMode,
                        operation.PixelColorBlendingMode);
                bool useFallbackCoverage = !this.backend.SupportsCoverageComposition<TPixel>(compositeBrush, graphicsOptions);

                if (!this.TryGetCoverage(
                    operation,
                    drawingOptions,
                    useFallbackCoverage,
                    coverageCache,
                    out CoverageCacheEntry coverageEntry,
                    out Point coverageLocation))
                {
                    continue;
                }

                if (!this.TryGetCompositeRegion(
                    coverageLocation,
                    coverageEntry.RasterizedSize,
                    out Buffer2DRegion<TPixel> compositeRegion,
                    out Point sourceOffset))
                {
                    continue;
                }

                this.backend.CompositeCoverage(
                    this.configuration,
                    compositeRegion,
                    coverageEntry.CoverageHandle,
                    sourceOffset,
                    compositeBrush,
                    graphicsOptions,
                    this.targetRegion.Rectangle);
            }
        }
        finally
        {
            this.backend.EndCompositeSession(this.configuration, this.targetRegion);

            foreach ((_, CoverageCacheEntry coverageEntry) in coverageCache)
            {
                this.backend.ReleaseCoverage(coverageEntry.CoverageHandle);
            }
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (this.isDisposed)
        {
            return;
        }

        this.isDisposed = true;
    }

    private void EnsureNotDisposed()
        => ObjectDisposedException.ThrowIf(this.isDisposed, this);

    private bool TryGetCoverage(
        DrawingOperation operation,
        DrawingOptions drawingOptions,
        bool useFallbackCoverage,
        Dictionary<OperationCoverageCacheKey, CoverageCacheEntry> coverageCache,
        out CoverageCacheEntry coverageEntry,
        out Point coverageLocation)
    {
        coverageLocation = operation.RenderLocation;
        if (!TryCreateCoveragePath(operation, out IPath? coveragePath))
        {
            coverageEntry = default;
            return false;
        }

        Point localOffset = Point.Empty;
        if (operation.Kind == DrawingOperationKind.Draw)
        {
            int strokeHalf = (int)((operation.Pen?.StrokeWidth ?? 0F) / 2F);
            coverageLocation = operation.RenderLocation - new Size(strokeHalf, strokeHalf);

            Point coverageMapOrigin = Point.Truncate(coveragePath.Bounds.Location);
            localOffset = new Point(
                coverageMapOrigin.X - operation.RenderLocation.X,
                coverageMapOrigin.Y - operation.RenderLocation.Y);
            coveragePath = coveragePath.Translate(-coverageMapOrigin);
        }

        OperationCoverageCacheKey cacheKey = CreateOperationCoverageCacheKey(operation, localOffset, useFallbackCoverage);
        if (coverageCache.TryGetValue(cacheKey, out coverageEntry))
        {
            return true;
        }

        Size rasterizedSize = Rectangle.Ceiling(coveragePath.Bounds).Size + new Size(2, 2);
        if (rasterizedSize.Width <= 0 || rasterizedSize.Height <= 0)
        {
            coverageEntry = default;
            return false;
        }

        RasterizationMode rasterizationMode = drawingOptions.GraphicsOptions.Antialias
            ? RasterizationMode.Antialiased
            : RasterizationMode.Aliased;
        RasterizerSamplingOrigin samplingOrigin = operation.Kind == DrawingOperationKind.Draw
            ? RasterizerSamplingOrigin.PixelCenter
            : RasterizerSamplingOrigin.PixelBoundary;

        RasterizerOptions rasterizerOptions = new(
            new Rectangle(0, 0, rasterizedSize.Width, rasterizedSize.Height),
            operation.IntersectionRule,
            rasterizationMode,
            samplingOrigin);

        DrawingCoverageHandle coverageHandle = this.backend.PrepareCoverage(
            coveragePath,
            rasterizerOptions,
            this.configuration.MemoryAllocator,
            useFallbackCoverage ? CoveragePreparationMode.Fallback : CoveragePreparationMode.Default);
        if (!coverageHandle.IsValid)
        {
            coverageEntry = default;
            return false;
        }

        coverageEntry = new CoverageCacheEntry(coverageHandle, rasterizedSize);
        coverageCache.Add(cacheKey, coverageEntry);
        return true;
    }

    private static Brush? GetCompositeBrush(DrawingOperation operation)
    {
        if (operation.Kind == DrawingOperationKind.Fill)
        {
            return operation.Brush;
        }

        return operation.Pen?.StrokeFill;
    }

    private static RichTextOptions ConfigureTextOptions(RichTextOptions options)
    {
        if (options.Path is not null && options.Origin != Vector2.Zero)
        {
            // Path-based text uses the path itself as positioning source; fold origin into the path
            // to avoid applying both path layout and origin translation.
            return new RichTextOptions(options)
            {
                Origin = Vector2.Zero,
                Path = options.Path.Translate(options.Origin)
            };
        }

        return options;
    }

    private static bool TryCreateCoveragePath(
        DrawingOperation operation,
        [NotNullWhen(true)] out IPath? coveragePath)
    {
        if (operation.Kind == DrawingOperationKind.Fill)
        {
            coveragePath = operation.Path;
            return true;
        }

        if (operation.Kind == DrawingOperationKind.Draw && operation.Pen is not null)
        {
            IPath globalPath = operation.Path.Translate(operation.RenderLocation);
            coveragePath = operation.Pen.GeneratePath(globalPath);
            return true;
        }

        coveragePath = null;
        return false;
    }

    private bool TryGetCompositeRegion(
        Point coverageLocation,
        Size coverageSize,
        out Buffer2DRegion<TPixel> compositeRegion,
        out Point sourceOffset)
    {
        Rectangle destination = new(coverageLocation, coverageSize);
        Rectangle clipped = Rectangle.Intersect(this.Bounds, destination);
        if (clipped.Equals(Rectangle.Empty))
        {
            compositeRegion = default;
            sourceOffset = default;
            return false;
        }

        sourceOffset = new Point(clipped.X - destination.X, clipped.Y - destination.Y);
        compositeRegion = this.targetRegion.GetSubRegion(clipped);
        return true;
    }

    private static OperationCoverageCacheKey CreateOperationCoverageCacheKey(
        DrawingOperation operation,
        Point localOffset,
        bool useFallbackCoverage)
    {
        int definitionKey = operation.DefinitionKey > 0
            ? operation.DefinitionKey
            : CreateFallbackDefinitionKey(operation);
        return new OperationCoverageCacheKey(definitionKey, localOffset, useFallbackCoverage);
    }

    private static int CreateFallbackDefinitionKey(DrawingOperation operation)
    {
        HashCode hash = default;
        hash.Add(RuntimeHelpers.GetHashCode(operation.Path));
        hash.Add((int)operation.Kind);
        hash.Add((int)operation.IntersectionRule);
        hash.Add(operation.Brush is null ? 0 : RuntimeHelpers.GetHashCode(operation.Brush));
        hash.Add(operation.Pen is null ? 0 : RuntimeHelpers.GetHashCode(operation.Pen));
        return hash.ToHashCode();
    }

    private readonly struct CoverageCacheEntry
    {
        public CoverageCacheEntry(DrawingCoverageHandle coverageHandle, Size rasterizedSize)
        {
            this.CoverageHandle = coverageHandle;
            this.RasterizedSize = rasterizedSize;
        }

        public DrawingCoverageHandle CoverageHandle { get; }

        public Size RasterizedSize { get; }
    }

    private readonly struct OperationCoverageCacheKey : IEquatable<OperationCoverageCacheKey>
    {
        private readonly int definitionKey;
        private readonly Point localOffset;
        private readonly bool useFallbackCoverage;

        public OperationCoverageCacheKey(int definitionKey, Point localOffset, bool useFallbackCoverage)
        {
            this.definitionKey = definitionKey;
            this.localOffset = localOffset;
            this.useFallbackCoverage = useFallbackCoverage;
        }

        public bool Equals(OperationCoverageCacheKey other)
            => this.definitionKey == other.definitionKey
            && this.localOffset == other.localOffset
            && this.useFallbackCoverage == other.useFallbackCoverage;

        public override bool Equals(object? obj)
            => obj is OperationCoverageCacheKey other && this.Equals(other);

        public override int GetHashCode()
        {
            HashCode hash = default;
            hash.Add(this.definitionKey);
            hash.Add(this.localOffset);
            hash.Add(this.useFallbackCoverage);
            return hash.ToHashCode();
        }
    }
}
