// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using SixLabors.Fonts.Rendering;
using SixLabors.ImageSharp.Drawing.Processing.Backends;
using SixLabors.ImageSharp.Drawing.Processing.Processors.Text;
using SixLabors.ImageSharp.Drawing.Shapes.Rasterization;
using SixLabors.ImageSharp.Drawing.Text;
using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.Processing.Processors.Transforms;

namespace SixLabors.ImageSharp.Drawing.Processing;

/// <summary>
/// A drawing canvas over a frame target.
/// </summary>
/// <typeparam name="TPixel">The pixel format.</typeparam>
public sealed class DrawingCanvas<TPixel> : IDisposable
    where TPixel : unmanaged, IPixel<TPixel>
{
    /// <summary>
    /// Processing configuration used by operations executed through this canvas.
    /// </summary>
    private readonly Configuration configuration;

    /// <summary>
    /// Backend responsible for rasterizing and composing draw commands.
    /// </summary>
    private readonly IDrawingBackend backend;

    /// <summary>
    /// Destination frame receiving rendered output.
    /// </summary>
    private readonly ICanvasFrame<TPixel> targetFrame;

    /// <summary>
    /// Command batcher used to defer and submit composition commands.
    /// </summary>
    private readonly DrawingCanvasBatcher<TPixel> batcher;

    /// <summary>
    /// Temporary image resources that must stay alive until queued commands are flushed.
    /// </summary>
    private readonly List<Image<TPixel>> pendingImageResources = [];

    /// <summary>
    /// Tracks whether this instance has already been disposed.
    /// </summary>
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

    /// <summary>
    /// Initializes a new instance of the <see cref="DrawingCanvas{TPixel}"/> class
    /// with an explicit backend.
    /// </summary>
    /// <param name="configuration">The active processing configuration.</param>
    /// <param name="backend">The drawing backend implementation.</param>
    /// <param name="targetFrame">The destination frame.</param>
    internal DrawingCanvas(
        Configuration configuration,
        IDrawingBackend backend,
        ICanvasFrame<TPixel> targetFrame)
        : this(
            configuration,
            backend,
            targetFrame,
            new DrawingCanvasBatcher<TPixel>(configuration, backend, targetFrame))
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DrawingCanvas{TPixel}"/> class
    /// with explicit backend and batcher instances.
    /// </summary>
    /// <param name="configuration">The active processing configuration.</param>
    /// <param name="backend">The drawing backend implementation.</param>
    /// <param name="targetFrame">The destination frame.</param>
    /// <param name="batcher">The command batcher used for deferred composition.</param>
    private DrawingCanvas(
        Configuration configuration,
        IDrawingBackend backend,
        ICanvasFrame<TPixel> targetFrame,
        DrawingCanvasBatcher<TPixel> batcher)
    {
        Guard.NotNull(configuration, nameof(configuration));
        Guard.NotNull(backend, nameof(backend));
        Guard.NotNull(targetFrame, nameof(targetFrame));
        Guard.NotNull(batcher, nameof(batcher));

        if (!targetFrame.TryGetCpuRegion(out _) && !targetFrame.TryGetNativeSurface(out _))
        {
            throw new NotSupportedException("Canvas frame must expose either a CPU region or a native surface.");
        }

        this.configuration = configuration;
        this.backend = backend;
        this.targetFrame = targetFrame;
        this.batcher = batcher;
        this.Bounds = new Rectangle(0, 0, targetFrame.Bounds.Width, targetFrame.Bounds.Height);
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
        return new DrawingCanvas<TPixel>(this.configuration, this.backend, childFrame, this.batcher);
    }

    /// <summary>
    /// Clears the whole canvas using the given brush and clear-style composition options.
    /// </summary>
    /// <param name="brush">Brush used to shade destination pixels during clear.</param>
    /// <param name="options">Drawing options used as the source for clear operation settings.</param>
    public void Clear(Brush brush, DrawingOptions options)
        => this.Fill(brush, options.CloneForClearOperation());

    /// <summary>
    /// Clears a local region using the given brush and clear-style composition options.
    /// </summary>
    /// <param name="region">Region to clear in local coordinates.</param>
    /// <param name="brush">Brush used to shade destination pixels during clear.</param>
    /// <param name="options">Drawing options used as the source for clear operation settings.</param>
    public void ClearRegion(Rectangle region, Brush brush, DrawingOptions options)
        => this.FillRegion(region, brush, options.CloneForClearOperation());

    /// <summary>
    /// Clears a path region using the given brush and clear-style composition options.
    /// </summary>
    /// <param name="path">The path region to clear.</param>
    /// <param name="brush">Brush used to shade destination pixels during clear.</param>
    /// <param name="options">Drawing options used as the source for clear operation settings.</param>
    public void ClearPath(IPath path, Brush brush, DrawingOptions options)
        => this.FillPath(path, brush, options.CloneForClearOperation());

    /// <summary>
    /// Fills the whole canvas using the given brush.
    /// </summary>
    /// <param name="brush">Brush used to shade destination pixels.</param>
    /// <param name="options">Drawing options for fill and rasterization behavior.</param>
    public void Fill(Brush brush, DrawingOptions options)
        => this.FillRegion(this.Bounds, brush, options);

    /// <summary>
    /// Fills a local region using the given brush.
    /// </summary>
    /// <param name="region">Region to fill in local coordinates.</param>
    /// <param name="brush">Brush used to shade destination pixels.</param>
    /// <param name="options">Drawing options for fill and rasterization behavior.</param>
    public void FillRegion(Rectangle region, Brush brush, DrawingOptions options)
    {
        this.EnsureNotDisposed();
        this.FillPath(new RectangularPolygon(region.X, region.Y, region.Width, region.Height), brush, options);
    }

    /// <summary>
    /// Fills a path in local coordinates using the given brush.
    /// </summary>
    /// <param name="path">The path to fill.</param>
    /// <param name="brush">Brush used to shade covered pixels.</param>
    /// <param name="options">Drawing options for fill and rasterization behavior.</param>
    public void FillPath(IPath path, Brush brush, DrawingOptions options)
    {
        this.EnsureNotDisposed();
        Guard.NotNull(path, nameof(path));
        Guard.NotNull(brush, nameof(brush));
        Guard.NotNull(options, nameof(options));

        IPath transformedPath = options.Transform == Matrix3x2.Identity
            ? path
            : path.Transform(options.Transform);

        this.FillPathCore(transformedPath, brush, options, RasterizerSamplingOrigin.PixelBoundary);
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

        IPath transformedPath = options.Transform == Matrix3x2.Identity
            ? path
            : path.Transform(options.Transform);
        IPath outline = pen.GeneratePath(transformedPath);

        DrawingOptions effectiveOptions = options;

        // Non-normalized stroke output can self-overlap; non-zero winding preserves stroke semantics.
        if (!pen.StrokeOptions.NormalizeOutput &&
            options.ShapeOptions.IntersectionRule != IntersectionRule.NonZero)
        {
            ShapeOptions shapeOptions = options.ShapeOptions.DeepClone();
            shapeOptions.IntersectionRule = IntersectionRule.NonZero;
            effectiveOptions = new DrawingOptions(options.GraphicsOptions, shapeOptions, options.Transform);
        }

        this.FillPathCore(outline, pen.StrokeFill, effectiveOptions, RasterizerSamplingOrigin.PixelCenter);
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

    /// <summary>
    /// Draws an image source region into a destination rectangle.
    /// </summary>
    /// <param name="image">The source image.</param>
    /// <param name="sourceRect">The source rectangle within <paramref name="image"/>.</param>
    /// <param name="destinationRect">The destination rectangle in local canvas coordinates.</param>
    /// <param name="drawingOptions">Drawing options defining blend and transform behavior.</param>
    /// <param name="sampler">
    /// Optional resampler used when scaling or transforming the image. Defaults to <see cref="KnownResamplers.Bicubic"/>.
    /// </param>
    public void DrawImage(
        Image<TPixel> image,
        Rectangle sourceRect,
        RectangleF destinationRect,
        DrawingOptions drawingOptions,
        IResampler? sampler = null)
    {
        this.EnsureNotDisposed();
        Guard.NotNull(image, nameof(image));
        Guard.NotNull(drawingOptions, nameof(drawingOptions));

        if (sourceRect.Width <= 0 ||
            sourceRect.Height <= 0 ||
            destinationRect.Width <= 0 ||
            destinationRect.Height <= 0)
        {
            return;
        }

        Rectangle clippedSourceRect = Rectangle.Intersect(sourceRect, image.Bounds);
        if (clippedSourceRect.Width <= 0 || clippedSourceRect.Height <= 0)
        {
            return;
        }

        RectangleF clippedDestinationRect = MapSourceClipToDestination(sourceRect, destinationRect, clippedSourceRect);
        if (clippedDestinationRect.Width <= 0 || clippedDestinationRect.Height <= 0)
        {
            return;
        }

        Size scaledSize = new(
            Math.Max(1, (int)MathF.Ceiling(clippedDestinationRect.Width)),
            Math.Max(1, (int)MathF.Ceiling(clippedDestinationRect.Height)));

        bool requiresScaling =
            clippedSourceRect.Width != scaledSize.Width ||
            clippedSourceRect.Height != scaledSize.Height;

        Image<TPixel> brushImage = image;
        RectangleF brushImageRegion = clippedSourceRect;
        RectangleF renderDestinationRect = clippedDestinationRect;
        Image<TPixel>? ownedImage = null;

        try
        {
            // Phase 1: Prepare source pixels (crop/scale) in image-local space.
            if (requiresScaling)
            {
                ownedImage = CreateScaledDrawImage(image, clippedSourceRect, scaledSize, sampler);
                brushImage = ownedImage;
                brushImageRegion = ownedImage.Bounds;
            }
            else if (clippedSourceRect != image.Bounds)
            {
                ownedImage = image.Clone(ctx => ctx.Crop(clippedSourceRect));
                brushImage = ownedImage;
                brushImageRegion = ownedImage.Bounds;
            }

            // Phase 2: Apply canvas transform to image content when requested.
            if (drawingOptions.Transform != Matrix3x2.Identity)
            {
                Image<TPixel> transformed = CreateTransformedDrawImage(
                    brushImage,
                    clippedDestinationRect,
                    drawingOptions.Transform,
                    sampler,
                    out renderDestinationRect);

                ownedImage?.Dispose();
                ownedImage = transformed;
                brushImage = transformed;
                brushImageRegion = transformed.Bounds;
            }

            if (renderDestinationRect.Width <= 0 || renderDestinationRect.Height <= 0)
            {
                return;
            }

            // Phase 3: Transfer temp-image ownership to deferred batch execution.
            if (!ReferenceEquals(brushImage, image))
            {
                this.pendingImageResources.Add(brushImage);
                ownedImage = null;
            }

            ImageBrush brush = new(brushImage, brushImageRegion);
            IPath destinationPath = new RectangularPolygon(
                renderDestinationRect.X,
                renderDestinationRect.Y,
                renderDestinationRect.Width,
                renderDestinationRect.Height);

            this.FillPath(destinationPath, brush, drawingOptions);
        }
        finally
        {
            ownedImage?.Dispose();
        }
    }

    private void FillPathCore(
        IPath path,
        Brush brush,
        DrawingOptions options,
        RasterizerSamplingOrigin samplingOrigin)
    {
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
            this.targetFrame,
            path,
            brush,
            graphicsOptions,
            rasterizerOptions,
            this.batcher);
    }

    /// <summary>
    /// Converts rendered text operations to composition commands and submits them to the batcher.
    /// </summary>
    /// <param name="operations">Text drawing operations produced by glyph layout/rendering.</param>
    /// <param name="drawingOptions">Drawing options applied to each operation.</param>
    private void DrawTextOperations(List<DrawingOperation> operations, DrawingOptions drawingOptions)
    {
        this.EnsureNotDisposed();

        // Build composition commands and enforce render-pass ordering while preserving
        // original emission order inside each pass. This preserves overlapping color-font
        // layer compositing semantics (for example emoji mouth/teeth layers).
        Dictionary<int, (IPath Path, int RasterState, int DefinitionKey)> definitionKeyCache = [];
        List<(byte RenderPass, int Sequence, CompositionCommand Command)> entries = new(operations.Count);
        for (int i = 0; i < operations.Count; i++)
        {
            DrawingOperation operation = operations[i];
            entries.Add((operation.RenderPass, i, this.CreateCompositionCommand(operation, drawingOptions, definitionKeyCache)));
        }

        entries.Sort(static (a, b) =>
        {
            int cmp = a.RenderPass.CompareTo(b.RenderPass);
            return cmp != 0 ? cmp : a.Sequence.CompareTo(b.Sequence);
        });

        for (int i = 0; i < entries.Count; i++)
        {
            this.batcher.AddComposition(entries[i].Command);
        }
    }

    /// <summary>
    /// Flushes queued drawing commands to the target in submission order.
    /// </summary>
    public void Flush()
    {
        this.EnsureNotDisposed();
        try
        {
            this.batcher.FlushCompositions();
        }
        finally
        {
            this.DisposePendingImageResources();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (this.isDisposed)
        {
            return;
        }

        try
        {
            this.batcher.FlushCompositions();
        }
        finally
        {
            this.DisposePendingImageResources();
            this.isDisposed = true;
        }
    }

    /// <summary>
    /// Ensures this instance is not disposed.
    /// </summary>
    private void EnsureNotDisposed()
        => ObjectDisposedException.ThrowIf(this.isDisposed, this);

    /// <summary>
    /// Normalizes text options to avoid applying origin translation twice when path-based text is used.
    /// </summary>
    /// <param name="options">Input text options.</param>
    /// <returns>Normalized text options for rendering.</returns>
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

    /// <summary>
    /// Builds a normalized composition command for a text drawing operation.
    /// </summary>
    /// <param name="operation">The source drawing operation.</param>
    /// <param name="drawingOptions">Drawing options applied to the operation.</param>
    /// <param name="definitionKeyCache">Optional cache used to reuse definition key computations.</param>
    /// <returns>A composition command ready for batching.</returns>
    private CompositionCommand CreateCompositionCommand(
        DrawingOperation operation,
        DrawingOptions drawingOptions,
        Dictionary<int, (IPath Path, int RasterState, int DefinitionKey)>? definitionKeyCache = null)
    {
        Brush compositeBrush = operation.Kind == DrawingOperationKind.Fill
            ? operation.Brush!
            : operation.Pen!.StrokeFill;

        GraphicsOptions graphicsOptions =
            drawingOptions.GraphicsOptions.CloneOrReturnForRules(
                operation.PixelAlphaCompositionMode,
                operation.PixelColorBlendingMode);

        IPath compositionPath;
        RasterizerSamplingOrigin samplingOrigin;
        IntersectionRule intersectionRule = operation.IntersectionRule;
        if (operation.Kind == DrawingOperationKind.Draw)
        {
            Pen pen = operation.Pen!;
            compositionPath = pen.GeneratePath(operation.Path);
            samplingOrigin = RasterizerSamplingOrigin.PixelCenter;

            // Keep draw semantics aligned with DrawPath: non-normalized stroke output
            // requires non-zero winding to preserve stroke interior behavior.
            if (!pen.StrokeOptions.NormalizeOutput && intersectionRule != IntersectionRule.NonZero)
            {
                intersectionRule = IntersectionRule.NonZero;
            }
        }
        else
        {
            compositionPath = operation.Path;
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

        RasterizationMode rasterizationMode = graphicsOptions.Antialias
            ? RasterizationMode.Antialiased
            : RasterizationMode.Aliased;

        RasterizerOptions rasterizerOptions = new(
            interest,
            intersectionRule,
            rasterizationMode,
            samplingOrigin);

        Point destinationOffset = new(
            this.targetFrame.Bounds.X + operation.RenderLocation.X,
            this.targetFrame.Bounds.Y + operation.RenderLocation.Y);

        return CompositionCommand.Create(
            compositionPath,
            compositeBrush,
            graphicsOptions,
            rasterizerOptions,
            destinationOffset,
            definitionKeyCache);
    }

    /// <summary>
    /// Creates resize options used for image drawing operations.
    /// </summary>
    /// <param name="size">Requested output size.</param>
    /// <param name="sampler">Optional resampler. Defaults to bicubic.</param>
    /// <returns>A resize options instance configured for stretch behavior.</returns>
    private static ResizeOptions CreateDrawImageResizeOptions(Size size, IResampler? sampler)
        => new()
        {
            Size = size,
            Mode = ResizeMode.Stretch,
            Sampler = sampler ?? KnownResamplers.Bicubic
        };

    /// <summary>
    /// Creates a scaled image for drawing, optionally cropping to a source region first.
    /// </summary>
    /// <param name="image">The source image.</param>
    /// <param name="clippedSourceRect">The clipped source rectangle.</param>
    /// <param name="scaledSize">The target scaled size.</param>
    /// <param name="sampler">Optional resampler used for scaling.</param>
    /// <returns>A new image containing the scaled pixels.</returns>
    private static Image<TPixel> CreateScaledDrawImage(
        Image<TPixel> image,
        Rectangle clippedSourceRect,
        Size scaledSize,
        IResampler? sampler)
    {
        ResizeOptions effectiveResizeOptions = CreateDrawImageResizeOptions(scaledSize, sampler);
        if (clippedSourceRect == image.Bounds)
        {
            return image.Clone(ctx => ctx.Resize(effectiveResizeOptions));
        }

        Image<TPixel> result = image.Clone(ctx => ctx.Crop(clippedSourceRect));
        result.Mutate(ctx => ctx.Resize(effectiveResizeOptions));
        return result;
    }

    /// <summary>
    /// Applies a transform to image content and returns the transformed image.
    /// </summary>
    /// <param name="image">The source image.</param>
    /// <param name="destinationRect">Destination rectangle in canvas coordinates.</param>
    /// <param name="transform">Canvas transform to apply.</param>
    /// <param name="sampler">Optional resampler used during transform.</param>
    /// <param name="transformedDestinationRect">Receives the transformed destination bounds.</param>
    /// <returns>A new image containing transformed pixels.</returns>
    private static Image<TPixel> CreateTransformedDrawImage(
        Image<TPixel> image,
        RectangleF destinationRect,
        Matrix3x2 transform,
        IResampler? sampler,
        out RectangleF transformedDestinationRect)
    {
        // Source space: pixel coordinates in the untransformed source image (0..Width, 0..Height).
        // Destination space: where that image would land on the canvas without any extra transform.
        // This matrix maps source -> destination by scaling to destination size then translating to destination origin.
        Matrix3x2 sourceToDestination = Matrix3x2.CreateScale(
            destinationRect.Width / image.Width,
            destinationRect.Height / image.Height)
            * Matrix3x2.CreateTranslation(destinationRect.X, destinationRect.Y);

        // Apply the canvas transform after source->destination placement:
        // source -> destination -> transformed-canvas.
        Matrix3x2 sourceToTransformedCanvas = sourceToDestination * transform;

        // Compute the transformed axis-aligned bounds so we know how large the output bitmap must be.
        transformedDestinationRect = TransformRectangle(
            new RectangleF(0, 0, image.Width, image.Height),
            sourceToTransformedCanvas);

        // The transform can produce fractional/max bounds; round up to whole pixels for target allocation.
        Size targetSize = new(
            Math.Max(1, (int)MathF.Ceiling(transformedDestinationRect.Width)),
            Math.Max(1, (int)MathF.Ceiling(transformedDestinationRect.Height)));

        // ImageSharp.Transform expects output coordinates relative to the output bitmap origin (0,0).
        // Shift transformed-canvas coordinates so transformedDestinationRect.Left/Top becomes 0,0.
        Matrix3x2 sourceToTarget = sourceToTransformedCanvas
            * Matrix3x2.CreateTranslation(-transformedDestinationRect.X, -transformedDestinationRect.Y);

        // Resample source pixels into the target bitmap using the computed source->target mapping.
        return image.Clone(ctx => ctx.Transform(
            image.Bounds,
            sourceToTarget,
            targetSize,
            sampler ?? KnownResamplers.Bicubic));
    }

    /// <summary>
    /// Maps a clipped source rectangle back to the corresponding destination rectangle.
    /// </summary>
    /// <param name="sourceRect">Original source rectangle.</param>
    /// <param name="destinationRect">Original destination rectangle.</param>
    /// <param name="clippedSourceRect">Source rectangle clipped to image bounds.</param>
    /// <returns>The destination rectangle corresponding to the clipped source region.</returns>
    private static RectangleF MapSourceClipToDestination(
        Rectangle sourceRect,
        RectangleF destinationRect,
        Rectangle clippedSourceRect)
    {
        float scaleX = destinationRect.Width / sourceRect.Width;
        float scaleY = destinationRect.Height / sourceRect.Height;

        float left = destinationRect.Left + ((clippedSourceRect.Left - sourceRect.Left) * scaleX);
        float top = destinationRect.Top + ((clippedSourceRect.Top - sourceRect.Top) * scaleY);
        float width = clippedSourceRect.Width * scaleX;
        float height = clippedSourceRect.Height * scaleY;

        return new RectangleF(left, top, width, height);
    }

    /// <summary>
    /// Computes the axis-aligned bounding rectangle of a transformed rectangle.
    /// </summary>
    /// <param name="rectangle">Input rectangle.</param>
    /// <param name="matrix">Transform matrix.</param>
    /// <returns>Axis-aligned bounds of the transformed rectangle.</returns>
    private static RectangleF TransformRectangle(RectangleF rectangle, Matrix3x2 matrix)
    {
        Vector2 topLeft = Vector2.Transform(new Vector2(rectangle.Left, rectangle.Top), matrix);
        Vector2 topRight = Vector2.Transform(new Vector2(rectangle.Right, rectangle.Top), matrix);
        Vector2 bottomLeft = Vector2.Transform(new Vector2(rectangle.Left, rectangle.Bottom), matrix);
        Vector2 bottomRight = Vector2.Transform(new Vector2(rectangle.Right, rectangle.Bottom), matrix);

        float left = MathF.Min(MathF.Min(topLeft.X, topRight.X), MathF.Min(bottomLeft.X, bottomRight.X));
        float top = MathF.Min(MathF.Min(topLeft.Y, topRight.Y), MathF.Min(bottomLeft.Y, bottomRight.Y));
        float right = MathF.Max(MathF.Max(topLeft.X, topRight.X), MathF.Max(bottomLeft.X, bottomRight.X));
        float bottom = MathF.Max(MathF.Max(topLeft.Y, topRight.Y), MathF.Max(bottomLeft.Y, bottomRight.Y));

        return RectangleF.FromLTRB(left, top, right, bottom);
    }

    /// <summary>
    /// Disposes image resources retained for deferred draw execution.
    /// </summary>
    private void DisposePendingImageResources()
    {
        if (this.pendingImageResources.Count == 0)
        {
            return;
        }

        // Release deferred image resources once queued operations have executed.
        for (int i = 0; i < this.pendingImageResources.Count; i++)
        {
            this.pendingImageResources[i].Dispose();
        }

        this.pendingImageResources.Clear();
    }
}
