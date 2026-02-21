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
/// A drawing canvas over a frame target.
/// </summary>
/// <typeparam name="TPixel">The pixel format.</typeparam>
public sealed class DrawingCanvas<TPixel> : IDisposable
    where TPixel : unmanaged, IPixel<TPixel>
{
    private readonly Configuration configuration;
    private readonly IDrawingBackend backend;
    private readonly ICanvasFrame<TPixel> targetFrame;
    private readonly DrawingCanvasBatcher<TPixel> batcher;
    private bool isDisposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="DrawingCanvas{TPixel}"/> class.
    /// </summary>
    /// <param name="configuration">The active processing configuration.</param>
    /// <param name="targetRegion">The destination target region.</param>
    public DrawingCanvas(Configuration configuration, Buffer2DRegion<TPixel> targetRegion)
        : this(configuration, new CpuCanvasFrame<TPixel>(targetRegion))
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DrawingCanvas{TPixel}"/> class.
    /// </summary>
    /// <param name="configuration">The active processing configuration.</param>
    /// <param name="targetFrame">The destination frame.</param>
    public DrawingCanvas(Configuration configuration, ICanvasFrame<TPixel> targetFrame)
        : this(configuration, configuration.GetDrawingBackend(), targetFrame)
    {
    }

    internal DrawingCanvas(
        Configuration configuration,
        IDrawingBackend backend,
        ICanvasFrame<TPixel> targetFrame)
    {
        Guard.NotNull(configuration, nameof(configuration));
        Guard.NotNull(backend, nameof(backend));
        Guard.NotNull(targetFrame, nameof(targetFrame));

        this.configuration = configuration;
        this.backend = backend;
        this.targetFrame = targetFrame;
        this.Bounds = new Rectangle(0, 0, targetFrame.Bounds.Width, targetFrame.Bounds.Height);
        this.batcher = new DrawingCanvasBatcher<TPixel>(configuration, backend, targetFrame);
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
        ICanvasFrame<TPixel> childFrame = new CanvasRegionFrame<TPixel>(this.targetFrame, clipped);
        return new DrawingCanvas<TPixel>(this.configuration, this.backend, childFrame);
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

        RasterizationMode rasterizationMode = graphicsOptions.Antialias
            ? RasterizationMode.Antialiased
            : RasterizationMode.Aliased;

        RasterizerOptions rasterizerOptions = new(
            region,
            IntersectionRule.NonZero,
            rasterizationMode,
            RasterizerSamplingOrigin.PixelBoundary);

        RectangularPolygon regionPath = new(region.X, region.Y, region.Width, region.Height);
        this.batcher.AddComposition(CompositionCommand.Create(regionPath, brush, graphicsOptions, rasterizerOptions));
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

        this.backend.FillPath(
            this.configuration,
            this.targetFrame,
            path,
            brush,
            graphicsOptions,
            rasterizerOptions,
            this.batcher);
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

        foreach (DrawingOperation operation in operations.OrderBy(x => x.RenderPass))
        {
            if (!TryCreateCompositionCommand(operation, drawingOptions, out CompositionCommand composition))
            {
                continue;
            }

            this.batcher.AddComposition(composition);
        }
    }

    /// <summary>
    /// Flushes queued drawing commands to the target in submission order.
    /// </summary>
    public void Flush()
    {
        this.EnsureNotDisposed();
        this.batcher.FlushCompositions();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (this.isDisposed)
        {
            return;
        }

        this.batcher.FlushCompositions();
        this.isDisposed = true;
    }

    private void EnsureNotDisposed()
        => ObjectDisposedException.ThrowIf(this.isDisposed, this);

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

    private static bool TryCreateCompositionCommand(
        DrawingOperation operation,
        DrawingOptions drawingOptions,
        out CompositionCommand composition)
    {
        Brush? compositeBrush = operation.Kind == DrawingOperationKind.Fill
            ? operation.Brush
            : operation.Pen?.StrokeFill;
        if (compositeBrush is null)
        {
            composition = default;
            return false;
        }

        GraphicsOptions graphicsOptions =
            drawingOptions.GraphicsOptions.CloneOrReturnForRules(
                operation.PixelAlphaCompositionMode,
                operation.PixelColorBlendingMode);

        IPath translatedPath = operation.Path.Translate(operation.RenderLocation);
        IPath compositionPath;
        RasterizerSamplingOrigin samplingOrigin;
        if (operation.Kind == DrawingOperationKind.Draw)
        {
            if (operation.Pen is null)
            {
                composition = default;
                return false;
            }

            compositionPath = operation.Pen.GeneratePath(translatedPath);
            samplingOrigin = RasterizerSamplingOrigin.PixelCenter;
        }
        else
        {
            compositionPath = translatedPath;
            samplingOrigin = RasterizerSamplingOrigin.PixelBoundary;
        }

        RectangleF bounds = compositionPath.Bounds;
        if (samplingOrigin == RasterizerSamplingOrigin.PixelCenter)
        {
            bounds = new RectangleF(bounds.X + 0.5F, bounds.Y + 0.5F, bounds.Width, bounds.Height);
        }

        Rectangle interest = Rectangle.FromLTRB(
            (int)MathF.Floor(bounds.Left),
            (int)MathF.Floor(bounds.Top),
            (int)MathF.Ceiling(bounds.Right),
            (int)MathF.Ceiling(bounds.Bottom));
        if (interest.Width <= 0 || interest.Height <= 0)
        {
            composition = default;
            return false;
        }

        RasterizationMode rasterizationMode = graphicsOptions.Antialias
            ? RasterizationMode.Antialiased
            : RasterizationMode.Aliased;
        RasterizerOptions rasterizerOptions = new(
            interest,
            operation.IntersectionRule,
            rasterizationMode,
            samplingOrigin);

        int definitionKey = operation.DefinitionKey > 0
            ? operation.DefinitionKey
            : CreateFallbackDefinitionKey(operation, compositeBrush);

        composition = CompositionCommand.Create(
            definitionKey,
            compositionPath,
            compositeBrush,
            graphicsOptions,
            rasterizerOptions);
        return true;
    }

    private static int CreateFallbackDefinitionKey(DrawingOperation operation, Brush compositeBrush)
    {
        HashCode hash = default;
        hash.Add(RuntimeHelpers.GetHashCode(operation.Path));
        hash.Add((int)operation.Kind);
        hash.Add((int)operation.IntersectionRule);
        hash.Add(RuntimeHelpers.GetHashCode(compositeBrush));
        if (operation.Pen is not null)
        {
            hash.Add(RuntimeHelpers.GetHashCode(operation.Pen));
        }

        return hash.ToHashCode();
    }
}
