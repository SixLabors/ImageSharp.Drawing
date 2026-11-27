// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Diagnostics;
using System.Numerics;
using System.Text;
using Avalonia;
using Avalonia.Platform;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Processing.Backends;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Processing.Processors.Transforms;
using AvaloniaEdgeMode = Avalonia.Media.EdgeMode;
using AvaloniaRenderOptions = Avalonia.Media.RenderOptions;
using AvaloniaTextOptions = Avalonia.Media.TextOptions;
using AvaloniaTextRenderingMode = Avalonia.Media.TextRenderingMode;
using BoxShadow = Avalonia.Media.BoxShadow;

namespace SixLabors.ImageSharp.Drawing.Renderers.Avalonia;

/// <summary>
/// Avalonia drawing context implementation backed by an ImageSharp.Drawing canvas.
/// </summary>
internal sealed class DrawingContextImpl : IDrawingContextImpl, IDrawingContextImplWithEffects, IDrawingContextWithAcrylicLikeSupport
{
    private const float AcrylicBlurSigma = 10F;

    private readonly ILockedFramebuffer? framebuffer;
    private readonly Image<Bgra32P>? image;
    private readonly WebGPURenderTarget? renderTarget;
    private readonly WebGPUSurfaceFrame? surfaceFrame;
    private readonly DrawingCanvas canvas;
    private readonly bool useWebGpuBackend;
    private readonly bool ownsCanvas;
    private readonly int traceId;
    private readonly StringBuilder? trace;
    private readonly Stack<Matrix> transforms = new();
    private readonly Stack<AvaloniaRenderOptions> renderOptions = new();
    private readonly Stack<AvaloniaTextOptions> textOptions = new();
    private readonly Stack<double> opacities = new();
    private readonly Stack<bool> opacityLayers = new();
    private readonly Stack<(Brush? Brush, RectangleF Bounds, Rectangle LayerBounds, float Opacity, bool HasLayer)> opacityMasks = new();
    private readonly Stack<bool> effects = new();
    private readonly List<Image> brushImages = [];
    private readonly AvaloniaVector dpi;
    private readonly bool ownsImage;
    private static int nextTraceId;
    private static bool environmentTraceConsumed;
    private int traceOperationIndex;
    private int canvasStateDepth;
    private AvaloniaRenderOptions currentRenderOptions;
    private AvaloniaTextOptions currentTextOptions;
    private double opacity = 1;
    private bool transientResourcesDisposed;

    /// <summary>
    /// Selects the drawing backend used by top-level render targets.
    /// </summary>
    public static DrawingBackendMode BackendMode { get; set; } = DrawingBackendMode.Auto;

    /// <summary>
    /// When set, the next framebuffer render is saved to a PNG in the temp folder for debugging, then the
    /// flag resets so exactly one frame is captured on demand (no per-frame disk spam). Flip it from the
    /// debugger or wire it to an input to grab the rendered framebuffer and inspect rendering vs present.
    /// </summary>
    public static bool DebugSaveNextFrame { get; set; } = false;

    /// <summary>
    /// When set, the next drawing context writes a text trace of clips, clears, layers, and geometry
    /// draws to the temp folder, then resets. Enable it when a frame needs operation-level diagnostics.
    /// </summary>
    public static bool DebugTraceNextFrame { get; set; } = false;

    private static readonly DrawingTextCache TextCache = new();

    /// <summary>
    /// Initializes a drawing context over an Avalonia framebuffer lock.
    /// </summary>
    /// <param name="framebuffer">The locked framebuffer to draw into.</param>
    public DrawingContextImpl(ILockedFramebuffer framebuffer)
        : this(WrapFramebuffer(framebuffer), framebuffer.Dpi, true)
    {
        this.framebuffer = framebuffer;

        this.Trace(() => $"Framebuffer size={framebuffer.Size.Width}x{framebuffer.Size.Height} rowBytes={framebuffer.RowBytes} format={framebuffer.Format} alpha={framebuffer.AlphaFormat}");
    }

    /// <summary>
    /// Initializes a drawing context over an existing ImageSharp image.
    /// </summary>
    /// <param name="image">The ImageSharp image to draw into.</param>
    /// <param name="dpi">The image DPI.</param>
    public DrawingContextImpl(Image<Bgra32P> image, AvaloniaVector dpi)
        : this(image, dpi, false)
    {
    }

    /// <summary>
    /// Initializes a drawing context over an acquired WebGPU surface frame.
    /// </summary>
    /// <param name="surfaceFrame">The acquired surface frame that will present on disposal.</param>
    /// <param name="dpi">The frame DPI.</param>
    /// <param name="pixelSize">The frame size in pixels.</param>
    public DrawingContextImpl(WebGPUSurfaceFrame surfaceFrame, AvaloniaVector dpi, PixelSize pixelSize)
    {
        this.surfaceFrame = surfaceFrame;
        this.canvas = surfaceFrame.Canvas;
        this.dpi = dpi;
        this.useWebGpuBackend = true;
        this.ownsCanvas = false;

        if (ShouldTraceContext(ownsImage: false, pixelSize.Width, pixelSize.Height))
        {
            this.traceId = Interlocked.Increment(ref nextTraceId);
            this.trace = new StringBuilder();
            this.Trace(() => $"Begin size={pixelSize.Width}x{pixelSize.Height} dpi={dpi.X},{dpi.Y} backend=WebGPU ownsImage=false");
        }
    }

    /// <summary>
    /// Initializes a drawing context over a WebGPU offscreen render target.
    /// </summary>
    /// <param name="renderTarget">The WebGPU render target that receives layer drawing commands.</param>
    /// <param name="dpi">The render target DPI.</param>
    /// <param name="pixelSize">The render target size in pixels.</param>
    public DrawingContextImpl(WebGPURenderTarget renderTarget, AvaloniaVector dpi, PixelSize pixelSize)
        : this(renderTarget, dpi, pixelSize, ownsCanvas: true)
    {
    }

    /// <summary>
    /// Initializes a drawing context over a WebGPU offscreen render target.
    /// </summary>
    /// <param name="renderTarget">The WebGPU render target that receives layer drawing commands.</param>
    /// <param name="dpi">The render target DPI.</param>
    /// <param name="pixelSize">The render target size in pixels.</param>
    /// <param name="ownsCanvas">A value indicating whether this context owns the canvas lifetime.</param>
    internal DrawingContextImpl(WebGPURenderTarget renderTarget, AvaloniaVector dpi, PixelSize pixelSize, bool ownsCanvas)
    {
        this.renderTarget = renderTarget;
        this.canvas = renderTarget.CreateCanvas(new DrawingOptions(), TextCache);
        this.dpi = dpi;
        this.useWebGpuBackend = true;
        this.ownsCanvas = ownsCanvas;

        if (ShouldTraceContext(ownsImage: false, pixelSize.Width, pixelSize.Height))
        {
            this.traceId = Interlocked.Increment(ref nextTraceId);
            this.trace = new StringBuilder();
            this.Trace(() => $"Begin size={pixelSize.Width}x{pixelSize.Height} dpi={dpi.X},{dpi.Y} backend=WebGPU-layer ownsImage=false");
        }
    }

    private DrawingContextImpl(Image<Bgra32P> image, AvaloniaVector dpi, bool ownsImage)
    {
        this.image = image;
        this.dpi = dpi;
        this.ownsImage = ownsImage;
        this.useWebGpuBackend = false;
        this.ownsCanvas = true;

        if (ShouldTraceContext(ownsImage, image.Width, image.Height))
        {
            this.traceId = Interlocked.Increment(ref nextTraceId);
            this.trace = new StringBuilder();
            this.Trace(() => $"Begin size={image.Width}x{image.Height} dpi={dpi.X},{dpi.Y} backend=CPU ownsImage={ownsImage}");
        }

        this.canvas = image.Frames.RootFrame.CreateCanvas(Configuration.Default, new DrawingOptions(), TextCache);
    }

    /// <inheritdoc />
    public Matrix Transform { get; set; } = Matrix.Identity;

    /// <summary>
    /// Gets the drawing canvas used by this context.
    /// </summary>
    internal DrawingCanvas Canvas => this.canvas;

    /// <inheritdoc />
    public void Clear(global::Avalonia.Media.Color color)
    {
        this.Trace(() => $"Clear color={Describe(color)}");
        this.canvas.Clear(Brushes.Solid(color.ToColor()));
    }

    /// <inheritdoc />
    public void DrawBitmap(IBitmapImpl source, double opacity, Rect sourceRect, Rect destRect)
    {
        IDrawableBitmapImpl bitmap = (IDrawableBitmapImpl)source;
        double imageOpacity = opacity * this.opacity;
        Rectangle imageSourceRect = sourceRect.ToRectangle();
        RectangleF imageDestinationRect = destRect.ToRectangleF();
        bool isUpscaling = imageDestinationRect.Width > imageSourceRect.Width || imageDestinationRect.Height > imageSourceRect.Height;
        var sampler = this.currentRenderOptions.BitmapInterpolationMode.ToResampler(isUpscaling);
        PixelAlphaCompositionMode alphaCompositionMode = this.currentRenderOptions.BitmapBlendingMode.ToAlphaCompositionMode();

        this.Trace(() => $"DrawBitmap image={bitmap.PixelSize.Width}x{bitmap.PixelSize.Height} opacity={imageOpacity:0.###} source={Describe(sourceRect)} dest={Describe(destRect)} transform={Describe(this.Transform)}");
        bitmap.Draw(this, imageSourceRect, imageDestinationRect, sampler, (float)imageOpacity, alphaCompositionMode);
    }

    /// <summary>
    /// Draws an ImageSharp image into this context using the supplied bitmap draw state.
    /// </summary>
    /// <param name="image">The image to draw.</param>
    /// <param name="sourceRect">The source rectangle in image pixels.</param>
    /// <param name="destinationRect">The destination rectangle in drawing coordinates.</param>
    /// <param name="sampler">The resampler used when scaling the image.</param>
    /// <param name="opacity">The opacity applied to the image draw.</param>
    /// <param name="alphaCompositionMode">The alpha composition mode used to blend the image with the destination.</param>
    public void DrawImage(
        Image image,
        Rectangle sourceRect,
        RectangleF destinationRect,
        IResampler sampler,
        float opacity,
        PixelAlphaCompositionMode alphaCompositionMode)
    {
        DrawingOptions options = this.CreateDrawingOptions(IntersectionRule.EvenOdd);
        options.GraphicsOptions.AlphaCompositionMode = alphaCompositionMode;
        this.SaveCanvasState(options, $"image alpha={alphaCompositionMode}");

        if (opacity >= 1)
        {
            this.canvas.DrawImage(image, sourceRect, destinationRect, sampler);
            this.RestoreCanvasState("DrawImage state");
            return;
        }

        Rectangle layerBounds = ToEnclosingRectangle(destinationRect);
        if (layerBounds.Width <= 0 || layerBounds.Height <= 0)
        {
            this.RestoreCanvasState("DrawImage state");
            return;
        }

        this.SaveCanvasLayer(
            new GraphicsOptions
            {
                BlendPercentage = opacity,
                AlphaCompositionMode = alphaCompositionMode
            },
            layerBounds,
            "DrawImage opacity");

        this.canvas.DrawImage(image, sourceRect, destinationRect, sampler);
        this.RestoreCanvasState("DrawImage opacity layer");
        this.RestoreCanvasState("DrawImage state");
    }

    /// <inheritdoc />
    public void DrawBitmap(IBitmapImpl source, IBrush opacityMask, Rect opacityMaskRect, Rect destRect)
    {
        this.Trace(() => $"DrawBitmap opacityMask={DescribeBrush(opacityMask)} maskRect={Describe(opacityMaskRect)} dest={Describe(destRect)} transform={Describe(this.Transform)}");
        this.PushOpacityMask(opacityMask, opacityMaskRect);
        this.DrawBitmap(source, 1, new Rect(((IDrawableBitmapImpl)source).PixelSize.ToSize(1)), destRect);
        this.PopOpacityMask();
    }

    /// <inheritdoc />
    public void DrawLine(IPen? pen, AvaloniaPoint p1, AvaloniaPoint p2)
    {
        Rect targetRect = new Rect(p1, p2).Normalize();
        Pen? strokePen = this.CreatePen(pen, targetRect);

        if (strokePen is not null)
        {
            this.PushDrawingState(IntersectionRule.EvenOdd, this.GetBrushOpacity(pen?.Brush));
            this.canvas.DrawLine(strokePen, p1.ToPointF(), p2.ToPointF());
            this.RestoreCanvasState("DrawLine state");
        }
    }

    /// <inheritdoc />
    public void DrawGeometry(IBrush? brush, IPen? pen, IGeometryImpl geometry)
    {
        GeometryImpl geometryImpl = (GeometryImpl)geometry;
        Rect fillBounds = geometryImpl.GetRenderBounds(null);
        Rect strokeBounds = geometryImpl.GetRenderBounds(pen);

        this.Trace(() => $"DrawGeometry fillRule={geometryImpl.FillRule} fillBounds={Describe(fillBounds)} strokeBounds={Describe(strokeBounds)} brush={DescribeBrush(brush)} pen={DescribePen(pen)} transform={Describe(this.Transform)}");

        Brush? fillBrush = this.CreateBrush(brush, fillBounds);

        if (fillBrush is not null && geometryImpl.FillPath is not null)
        {
            this.FillPath(fillBrush, geometryImpl.FillPath, geometryImpl.FillRule, this.GetBrushOpacity(brush));
        }

        Pen? strokePen = this.CreatePen(pen, strokeBounds);

        if (strokePen is not null)
        {
            IntersectionRule strokeRule = geometryImpl.FillRule;
            this.Trace(() => $"StrokePath fillRule={strokeRule} pathBounds={Describe(geometryImpl.StrokePath.Bounds)} strokeBounds={Describe(strokeBounds)} transform={Describe(this.Transform)}");

            this.PushDrawingState(strokeRule, this.GetBrushOpacity(pen?.Brush));
            this.canvas.Draw(strokePen, geometryImpl.StrokePath);
            this.RestoreCanvasState("DrawGeometry stroke state");
        }
    }

    /// <inheritdoc />
    public void DrawRectangle(IBrush? brush, IPen? pen, RoundedRect rect, BoxShadows boxShadows = default)
    {
        this.Trace(() => $"DrawRectangle rect={Describe(rect.Rect)} rounded={rect.IsRounded} brush={DescribeBrush(brush)} pen={DescribePen(pen)} shadows={boxShadows.Count} transform={Describe(this.Transform)}");

        IPath path = rect.IsRounded
            ? rect.ToRoundedRectanglePath()
            : new RectanglePolygon(rect.Rect.ToRectangleF());

        this.DrawOuterBoxShadows(rect, path, boxShadows);

        // Inset shadows paint above the fill and below the stroke, matching the Skia backend,
        // so the fill and stroke split around them when any are present.
        if (boxShadows.HasInsetShadows)
        {
            this.DrawGeometry(brush, null, new StreamGeometryImpl(path, path, IntersectionRule.EvenOdd));
            this.DrawInnerBoxShadows(rect, path, boxShadows);
            this.DrawGeometry(null, pen, new StreamGeometryImpl(path, path, IntersectionRule.EvenOdd));
            return;
        }

        this.DrawGeometry(brush, pen, new StreamGeometryImpl(path, path, IntersectionRule.EvenOdd));
    }

    /// <inheritdoc />
    public void DrawRectangle(global::Avalonia.Media.IExperimentalAcrylicMaterial material, RoundedRect rect)
    {
        if (rect.Rect.Height <= 0 || rect.Rect.Width <= 0)
        {
            return;
        }

        this.Trace(() => $"DrawAcrylicRectangle rect={Describe(rect.Rect)} rounded={rect.IsRounded} background={material.BackgroundSource} material={Describe(material.MaterialColor)} tint={Describe(material.TintColor)} transform={Describe(this.Transform)}");

        IPath path = rect.IsRounded
            ? rect.ToRoundedRectanglePath()
            : new RectanglePolygon(rect.Rect.ToRectangleF());

        // The acrylic effect frosts and tints whatever lies beneath the shape and replaces the
        // region with the result: the backdrop write-back carries Src semantics, which is the one
        // thing Skia's Digger mode changes on its paint. Under a transparent window the backdrop
        // is empty and the region becomes the bare tint, exactly Skia's material; over in-app
        // content the material is genuinely frosted, which Skia fakes with a noise tile.
        Color materialTint = FlattenAcrylicTint(material.MaterialColor, material.TintColor);
        this.SaveCanvasLayer(
            new GraphicsOptions(),
            path,
            new WebGPUBackdropAcrylicLayerEffect(AcrylicBlurSigma, materialTint),
            this.CreateDrawingOptions(IntersectionRule.EvenOdd),
            "Acrylic rectangle");

        this.RestoreCanvasState("Acrylic rectangle layer");
    }

    /// <summary>
    /// Flattens the acrylic material and tint into the single colour whose source-over composite
    /// equals tint over material.
    /// </summary>
    /// <remarks>
    /// Skia's acrylic paint composes two constant colour shaders, tint source-over material, per
    /// pixel. Because source-over is associative, tint over (material over backdrop) equals
    /// (tint over material) over backdrop, so the pair collapses to one Porter-Duff over of the two
    /// constants: the weighted sum below is the premultiplied blend and the division by the result
    /// alpha converts back to the straight colour the acrylic effect's matrix expects.
    /// </remarks>
    private static Color FlattenAcrylicTint(AvaloniaColor material, AvaloniaColor tint)
    {
        float materialAlpha = material.A / 255F;
        float tintAlpha = tint.A / 255F;
        float alpha = tintAlpha + (materialAlpha * (1F - tintAlpha));
        if (alpha <= 0F)
        {
            return Color.Transparent;
        }

        byte Flatten(byte tintChannel, byte materialChannel)
            => (byte)Math.Round(((tintChannel * tintAlpha) + (materialChannel * materialAlpha * (1F - tintAlpha))) / alpha);

        return new AvaloniaColor(
            (byte)Math.Round(alpha * 255F),
            Flatten(tint.R, material.R),
            Flatten(tint.G, material.G),
            Flatten(tint.B, material.B)).ToColor();
    }

    /// <inheritdoc />
    public void DrawRegion(IBrush? brush, IPen? pen, IPlatformRenderInterfaceRegion region)
    {
        RegionImpl regionImpl = (RegionImpl)region;

        this.Trace(() => $"DrawRegion empty={regionImpl.IsEmpty} bounds={Describe(regionImpl.Bounds)} rects={Describe(regionImpl.Rects)} brush={DescribeBrush(brush)} pen={DescribePen(pen)} transform={Describe(this.Transform)}");

        if (regionImpl.IsEmpty)
        {
            return;
        }

        IPath path = regionImpl.ToPath();

        this.DrawGeometry(brush, pen, new StreamGeometryImpl(path, path, IntersectionRule.NonZero));
    }

    /// <inheritdoc />
    public void DrawEllipse(IBrush? brush, IPen? pen, Rect rect)
    {
        IPath path = new EllipsePolygon(rect.Center.ToPointF(), rect.Size.ToSizeF());
        this.DrawGeometry(brush, pen, new StreamGeometryImpl(path, path, IntersectionRule.EvenOdd));
    }

    /// <inheritdoc />
    public void DrawGlyphRun(IBrush? foreground, IGlyphRunImpl glyphRun)
    {
        if (glyphRun is not GlyphRunImpl glyphRunImpl)
        {
            this.Trace(() => $"DrawGlyphRun unsupported={glyphRun.GetType().Name}");
            return;
        }

        ReadOnlyMemory<ushort> glyphIds = glyphRunImpl.GlyphIds;
        if (glyphIds.IsEmpty)
        {
            this.Trace(() => $"DrawGlyphRun empty bounds={Describe(glyphRunImpl.Bounds)} transform={Describe(this.Transform)}");
            return;
        }

        Brush? brush = this.CreateBrush(foreground, glyphRunImpl.Bounds);

        this.Trace(() => $"DrawGlyphRun glyphs={glyphIds.Length} bounds={Describe(glyphRunImpl.Bounds)} foreground={DescribeBrush(foreground)} transform={Describe(this.Transform)}");

        if (brush is null)
        {
            return;
        }

        // Start with the currently pushed TextOptions, then apply the same RenderOptions
        // fallback shape as the Skia backend before creating the glyph rendering options.
        AvaloniaTextOptions effectiveTextOptions = this.currentTextOptions;
        AvaloniaRenderOptions effectiveRenderOptions = this.currentRenderOptions;

#pragma warning disable CS0618
        if (effectiveTextOptions.TextRenderingMode == AvaloniaTextRenderingMode.Unspecified &&
            effectiveRenderOptions.TextRenderingMode != AvaloniaTextRenderingMode.Unspecified)
        {
            effectiveTextOptions = effectiveTextOptions with
            {
                TextRenderingMode = effectiveRenderOptions.TextRenderingMode
            };
        }
#pragma warning restore CS0618

        // If TextRenderingMode is still unspecified, derive it from EdgeMode as Skia does.
        if (effectiveTextOptions.TextRenderingMode == AvaloniaTextRenderingMode.Unspecified)
        {
            effectiveTextOptions = effectiveTextOptions with
            {
                TextRenderingMode = effectiveRenderOptions.EdgeMode == AvaloniaEdgeMode.Aliased
                    ? AvaloniaTextRenderingMode.Alias
                    : AvaloniaTextRenderingMode.SubpixelAntialias
            };
        }

        bool antialias = effectiveTextOptions.TextRenderingMode != AvaloniaTextRenderingMode.Alias;

        // Keep glyph-level font option mapping on the glyph run, matching Skia's
        // GlyphRunImpl.CreateFont ownership.
        RichGlyphOptions options = glyphRunImpl.CreateGlyphOptions(effectiveTextOptions);

        this.PushDrawingState(IntersectionRule.NonZero, antialias, this.GetBrushOpacity(foreground));
        this.canvas.DrawText(glyphIds.Span, glyphRunImpl.GlyphOrigins.Span, options, brush, pen: null);
        this.RestoreCanvasState("DrawGlyphRun state");
    }

    /// <inheritdoc />
    public IDrawingContextLayerImpl CreateLayer(PixelSize size)
    {
        if (this.useWebGpuBackend)
        {
            if (this.surfaceFrame is not null)
            {
                WebGPURenderTarget layerTarget = this.surfaceFrame.CreateRenderTarget(size.Width, size.Height);

                return new WebGPULayerImpl(layerTarget, size, this.dpi);
            }

            if (this.renderTarget is not null)
            {
                WebGPURenderTarget layerTarget = this.renderTarget.CreateRenderTarget(size.Width, size.Height);

                return new WebGPULayerImpl(layerTarget, size, this.dpi);
            }

            throw new InvalidOperationException("The WebGPU drawing context has no target for layer allocation.");
        }

        return new RenderTargetBitmapImpl(size, this.dpi);
    }

    /// <summary>
    /// Copies a CPU-backed layer into this context.
    /// </summary>
    /// <param name="source">The source image.</param>
    /// <param name="size">The layer size in pixels.</param>
    internal void Blit(Image<Bgra32P> source, PixelSize size)
    {
        using DrawingCanvas sourceCanvas = source.Frames.RootFrame.CreateCanvas(Configuration.Default, new DrawingOptions(), TextCache);
        this.canvas.CopyPixelsFrom(
            sourceCanvas,
            new Rectangle(0, 0, size.Width, size.Height),
            new SixLabors.ImageSharp.Point(0, 0));
    }

    /// <summary>
    /// Copies a layer canvas into this context.
    /// </summary>
    /// <param name="source">The source canvas.</param>
    /// <param name="size">The layer size in pixels.</param>
    internal void Blit(DrawingCanvas source, PixelSize size)
    {
        this.canvas.CopyPixelsFrom(
            source,
            new Rectangle(0, 0, size.Width, size.Height),
            new SixLabors.ImageSharp.Point(0, 0));
    }

    /// <inheritdoc />
    public void PushClip(Rect clip)
    {
        this.Trace(() => $"PushClip rect={Describe(clip)} transform={Describe(this.Transform)}");
        this.SaveTransform();
        this.PushDrawingState(IntersectionRule.EvenOdd, antialias: false);
        this.canvas.Clip(new RectanglePolygon(clip.ToRectangleF()));
    }

    /// <inheritdoc />
    public void PushClip(RoundedRect clip)
    {
        this.Trace(() => $"PushClip roundedRect={Describe(clip.Rect)} rounded={clip.IsRounded} transform={Describe(this.Transform)}");
        this.SaveTransform();
        this.PushDrawingState(IntersectionRule.EvenOdd);
        this.canvas.Clip(clip.ToRoundedRectanglePath());
    }

    /// <inheritdoc />
    public void PushClip(IPlatformRenderInterfaceRegion region)
    {
        RegionImpl regionImpl = (RegionImpl)region;

        this.Trace(() => $"PushClip region empty={regionImpl.IsEmpty} bounds={Describe(regionImpl.Bounds)} rects={Describe(regionImpl.Rects)} transform={Describe(this.Transform)}");
        this.SaveTransform();
        Matrix transform = this.Transform;
        this.Transform = Matrix.Identity;

        // Avalonia's Skia backend uses SkCanvas.ClipRegion here. Skia documents region clips as
        // device-space and aliased, so keep the dirty-region edge hard instead of blending stale
        // pixels at the redraw boundary.
        this.PushDrawingState(IntersectionRule.NonZero, antialias: false);
        this.canvas.Clip(regionImpl.ToPath());
        this.Transform = transform;
    }

    /// <inheritdoc />
    public void PopClip()
    {
        this.Trace(() => "PopClip");
        this.RestoreCanvasState("Clip state");
        this.RestoreTransform();
    }

    /// <inheritdoc />
    public void PushLayer(Rect bounds)
    {
        this.Trace(() => $"PushLayer bounds={Describe(bounds)} transform={Describe(this.Transform)}");
        this.SaveTransform();
        this.SaveCanvasLayer(new GraphicsOptions(), ToEnclosingRectangle(bounds), this.CreateDrawingOptions(IntersectionRule.EvenOdd), "PushLayer");
    }

    /// <inheritdoc />
    public void PopLayer()
    {
        this.Trace(() => "PopLayer");
        this.RestoreCanvasState("Layer");
        this.RestoreTransform();
    }

    /// <inheritdoc />
    public void PushOpacity(double opacity, Rect? bounds)
    {
        this.Trace(() => $"PushOpacity opacity={opacity:0.###} bounds={(bounds.HasValue ? Describe(bounds.Value) : "<null>")} current={this.opacity:0.###} transform={Describe(this.Transform)}");
        this.opacities.Push(this.opacity);

        bool useOpacityLayer = this.currentRenderOptions.RequiresFullOpacityHandling == true;
        this.opacityLayers.Push(useOpacityLayer);

        if (useOpacityLayer)
        {
            float layerOpacity = (float)(this.opacity * opacity);
            Rectangle layerBounds = bounds is not null ? ToEnclosingRectangle(bounds.Value) : this.canvas.Bounds;

            // Some controls require opacity to be applied to the composed result rather than to each
            // child brush. Match Skia by isolating those controls in a layer and fading the layer when
            // it is restored.
            this.opacity = 1;
            this.SaveCanvasLayer(
                new GraphicsOptions { BlendPercentage = layerOpacity },
                layerBounds,
                this.CreateDrawingOptions(IntersectionRule.EvenOdd),
                "PushOpacity full opacity layer");

            return;
        }

        this.opacity *= opacity;
    }

    /// <inheritdoc />
    public void PopOpacity()
    {
        this.Trace(() => $"PopOpacity current={this.opacity:0.###}");
        if (this.opacityLayers.Pop())
        {
            this.RestoreCanvasState("Opacity layer");
        }

        this.opacity = this.opacities.Pop();
    }

    /// <inheritdoc />
    public void PushOpacityMask(IBrush mask, Rect bounds)
    {
        this.SaveTransform();

        Brush? maskBrush = this.CreateBrush(mask, bounds);
        Rectangle layerBounds = ToEnclosingRectangle(bounds);
        bool hasLayer = maskBrush is not null && layerBounds.Width > 0 && layerBounds.Height > 0;

        this.opacityMasks.Push((maskBrush, bounds.ToRectangleF(), layerBounds, this.GetBrushOpacity(mask), hasLayer));

        if (hasLayer)
        {
            // Establish the active transform on the canvas state so the layer (and the content drawn
            // into it) is positioned correctly, then isolate the masked content in its own layer.
            this.PushDrawingState(IntersectionRule.EvenOdd);
            this.SaveCanvasLayer(new GraphicsOptions(), layerBounds, "OpacityMask content");
        }
    }

    /// <inheritdoc />
    public void PopOpacityMask()
    {
        (Brush? brush, RectangleF maskBounds, Rectangle layerBounds, float maskOpacity, bool hasLayer) = this.opacityMasks.Pop();

        if (hasLayer)
        {
            // Draw the mask into a DstIn layer so, on composite, it keeps the content layer's colour
            // but scales its alpha by the mask's alpha; the content then fades wherever the mask is
            // transparent. Both layers and the saved state are unwound here.
            this.SaveCanvasLayer(
                new GraphicsOptions { AlphaCompositionMode = PixelAlphaCompositionMode.DestIn },
                layerBounds,
                this.CreateDrawingOptions(IntersectionRule.EvenOdd, maskOpacity),
                "OpacityMask mask");

            this.canvas.Fill(brush!, new RectanglePolygon(maskBounds));
            this.RestoreCanvasState("OpacityMask mask layer");
            this.RestoreCanvasState("OpacityMask content layer");
            this.RestoreCanvasState("OpacityMask state");
        }

        this.RestoreTransform();
    }

    /// <inheritdoc />
    public void PushEffect(AvaloniaRect? effectClipRect, IEffect effect)
    {
        this.Trace(() => $"PushEffect clip={(effectClipRect.HasValue ? Describe(effectClipRect.Value) : "<null>")} effect={effect.GetType().Name} transform={Describe(this.Transform)}");

        LayerEffect? layerEffect = this.CreateLayerEffect(effect);
        this.effects.Push(layerEffect is not null);

        if (layerEffect is not null)
        {
            // Content drawn between push and pop renders into an isolated effect layer; the canvas
            // applies the effect to the layer's content when the layer is restored. The ambient
            // opacity rides the layer composite so the effect output fades with the content, and is
            // reset inside the layer to avoid applying it twice, matching PushOpacity.
            Rectangle bounds = effectClipRect is not null
                ? ToEnclosingRectangle(effectClipRect.Value)
                : this.canvas.Bounds;

            float layerOpacity = (float)this.opacity;
            this.opacities.Push(this.opacity);
            this.opacity = 1;

            this.SaveCanvasLayer(
                new GraphicsOptions { BlendPercentage = layerOpacity },
                bounds,
                layerEffect,
                this.CreateDrawingOptions(IntersectionRule.EvenOdd),
                "Effect content");
        }
    }

    /// <inheritdoc />
    public void PopEffect()
    {
        this.Trace(() => "PopEffect");

        if (this.effects.Pop())
        {
            this.RestoreCanvasState("Effect layer");
            this.opacity = this.opacities.Pop();
        }
    }

    /// <summary>
    /// Resolves an Avalonia effect into a canvas <see cref="LayerEffect"/> with device-space
    /// parameters, mirroring the Skia backend's effect-to-image-filter mapping. Unsupported effect
    /// types and degenerate blurs resolve to <see langword="null"/> and draw unaffected.
    /// </summary>
    /// <param name="effect">The effect to resolve.</param>
    /// <returns>The resolved effect, or <see langword="null"/>.</returns>
    private LayerEffect? CreateLayerEffect(IEffect effect)
    {
        // Blur sigma and shadow offsets act on the layer's pixels after the transform has been
        // applied to the geometry, so both are mapped through the current transform the same way
        // Skia's image filters follow the canvas matrix.
        AvaloniaMatrix transform = this.Transform;
        float scale = (float)Math.Sqrt(Math.Abs((transform.M11 * transform.M22) - (transform.M12 * transform.M21)));

        if (effect is IBlurEffect blur)
        {
            if (blur.Radius <= 0)
            {
                return null;
            }

            return new WebGPUGaussianBlurLayerEffect(BlurRadiusToSigma(blur.Radius) * scale);
        }

        if (effect is IDropShadowEffect drop)
        {
            float sigma = drop.BlurRadius > 0 ? BlurRadiusToSigma(drop.BlurRadius) * scale : 0F;

            // The offset is a direction vector, so it maps through the transform without translation.
            SixLabors.ImageSharp.Point offset = new(
                (int)Math.Round((drop.OffsetX * transform.M11) + (drop.OffsetY * transform.M21)),
                (int)Math.Round((drop.OffsetX * transform.M12) + (drop.OffsetY * transform.M22)));

            // The colour's own alpha and the effect opacity scale the silhouette's alpha; the
            // ambient opacity rides the effect layer's composite instead, matching the Skia
            // backend's opacity-save-layer path.
            byte alpha = (byte)Math.Clamp(drop.Color.A * drop.Opacity, 0D, 255D);
            Color color = new AvaloniaColor(alpha, drop.Color.R, drop.Color.G, drop.Color.B).ToColor();

            return new WebGPUDropShadowLayerEffect(offset, sigma, color);
        }

        return null;
    }

    /// <inheritdoc />
    public void PushGeometryClip(IGeometryImpl clip)
    {
        this.SaveTransform();

        GeometryImpl geometry = (GeometryImpl)clip;
        this.Trace(() => $"PushGeometryClip fillRule={geometry.FillRule} pathBounds={Describe(geometry.FillPath?.Bounds ?? RectangleF.Empty)} transform={Describe(this.Transform)}");
        this.PushDrawingState(geometry.FillRule);
        this.canvas.Clip(geometry.FillPath ?? EmptyPath.ClosedPath);
    }

    /// <inheritdoc />
    public void PopGeometryClip() => this.PopClip();

    /// <inheritdoc />
    public void PushRenderOptions(RenderOptions renderOptions)
    {
        this.renderOptions.Push(this.currentRenderOptions);
        this.currentRenderOptions = this.currentRenderOptions.MergeWith(renderOptions);
        this.TraceState($"PushRenderOptions requiresFullOpacity={this.currentRenderOptions.RequiresFullOpacityHandling}");
    }

    /// <inheritdoc />
    public void PopRenderOptions()
    {
        this.currentRenderOptions = this.renderOptions.Pop();
        this.TraceState($"PopRenderOptions requiresFullOpacity={this.currentRenderOptions.RequiresFullOpacityHandling}");
    }

    /// <inheritdoc />
    public void PushTextOptions(AvaloniaTextOptions textOptions)
    {
        this.textOptions.Push(this.currentTextOptions);
        this.currentTextOptions = this.currentTextOptions.MergeWith(textOptions);
        this.TraceState($"PushTextOptions rendering={this.currentTextOptions.TextRenderingMode}");
    }

    /// <inheritdoc />
    public void PopTextOptions()
    {
        this.currentTextOptions = this.textOptions.Pop();
        this.TraceState($"PopTextOptions rendering={this.currentTextOptions.TextRenderingMode}");
    }

    /// <inheritdoc />
    public object? GetFeature(Type t) => null;

    /// <inheritdoc />
    public void Dispose()
    {
        if (this.surfaceFrame is not null)
        {
            // Surface frames own presentation. Disposing the frame flushes its canvas and presents the
            // acquired swapchain texture, matching Skia's direct platform-drawable session shape.
            this.surfaceFrame.Dispose();
        }
        else
        {
            // Layer contexts leave their canvas alive for the owning layer's blit. Other contexts dispose the
            // canvas here so queued commands render into their target.
            if (this.ownsCanvas)
            {
                this.canvas.Dispose();
            }
            else
            {
                this.canvas.Flush();
            }
        }

        // On-demand capture of the rendered framebuffer (the actual window surface, not layers). Splits
        // "holes are in the rendered image" from "holes appear only at present/display".
        if (DebugSaveNextFrame && this.framebuffer is not null && this.image is not null)
        {
            DebugSaveNextFrame = false;
            string path = System.IO.Path.Combine(
                GetCaptureDirectory(),
                $"imagesharp-frame-{DateTime.Now:yyyyMMdd-HHmmss-fff}.png");
            this.image.SaveAsPng(path);
        }

        if (ShouldSaveContextImage() && this.image is not null)
        {
            string path = System.IO.Path.Combine(
                GetCaptureDirectory(),
                $"imagesharp-context-{DateTime.Now:yyyyMMdd-HHmmss-fff}-{this.traceId}-{(this.useWebGpuBackend ? "webgpu" : "cpu")}-{(this.ownsImage ? "owned" : "borrowed")}.png");
            this.image.SaveAsPng(path);
        }

        if (this.trace is not null)
        {
            string path = System.IO.Path.Combine(
                GetCaptureDirectory(),
                $"imagesharp-trace-{DateTime.Now:yyyyMMdd-HHmmss-fff}-{this.traceId}.log");
            File.WriteAllText(path, this.trace.ToString());
            Debug.WriteLine($"ImageSharp drawing trace saved to {path}");
        }

        if (this.ownsCanvas || this.surfaceFrame is not null)
        {
            this.DisposeTransientResources();
        }

        this.framebuffer?.Dispose();

        if (this.ownsImage && this.image is not null)
        {
            this.image.Dispose();
        }
    }

    /// <summary>
    /// Disposes temporary images referenced by queued draw commands after those commands have rendered.
    /// </summary>
    internal void DisposeTransientResources()
    {
        if (this.transientResourcesDisposed)
        {
            return;
        }

        for (int i = 0; i < this.brushImages.Count; i++)
        {
            this.brushImages[i].Dispose();
        }

        this.transientResourcesDisposed = true;
    }

    /// <summary>
    /// Converts an Avalonia brush into an ImageSharp brush for the supplied target bounds.
    /// </summary>
    private Brush? CreateBrush(IBrush? brush, Rect targetRect)
    {
        if (brush is null)
        {
            return null;
        }

        if (brush is ISolidColorBrush solid)
        {
            return Brushes.Solid(solid.Color.ToColor());
        }

        if (brush is IGradientBrush gradient)
        {
            // Brushes are built in local/context space; the DrawingCanvas applies the active
            // transform to the brush, so the backend must not pre-transform it here.
            return CreateGradientBrush(gradient, targetRect);
        }

        if (brush is IImageBrush imageBrush)
        {
            return this.CreateImageBrush(imageBrush, targetRect);
        }

        if (brush is global::Avalonia.Media.ISceneBrush sceneBrush)
        {
            using global::Avalonia.Media.ISceneBrushContent? content = sceneBrush.CreateContent();

            return content is null
                ? null
                : this.CreateSceneBrushContent(content, targetRect);
        }

        if (brush is global::Avalonia.Media.ISceneBrushContent sceneBrushContent)
        {
            return this.CreateSceneBrushContent(sceneBrushContent, targetRect);
        }

        return null;
    }

    /// <summary>
    /// Gets the blend amount for a brush draw from Avalonia's brush opacity and active context opacity.
    /// </summary>
    private float GetBrushOpacity(IBrush? brush)
        => (float)((brush?.Opacity ?? 1) * this.opacity);

    /// <summary>
    /// Wraps the Avalonia framebuffer memory so the CPU canvas can render directly into it.
    /// </summary>
    private static unsafe Image<Bgra32P> WrapFramebuffer(ILockedFramebuffer framebuffer)
    {
        int rowByteCount = framebuffer.Size.Width * 4;
        int bufferByteCount = (framebuffer.RowBytes * (framebuffer.Size.Height - 1)) + rowByteCount;

        return Image.WrapMemory<Bgra32P>(
            framebuffer.Address.ToPointer(),
            bufferByteCount,
            framebuffer.Size.Width,
            framebuffer.Size.Height,
            framebuffer.RowBytes);
    }

    /// <summary>
    /// Saves the canvas state with the current transform, clip, and fill-rule options.
    /// </summary>
    /// <remarks>
    /// Save snapshots the current state and inherits the active clip; the canvas applies the active
    /// transform to drawn paths and clips itself, so nothing is pre-transformed here.
    /// </remarks>
    private void PushDrawingState(IntersectionRule fillRule)
        => this.SaveCanvasState(this.CreateDrawingOptions(fillRule), $"fillRule={fillRule}");

    /// <summary>
    /// Saves a canvas state and records diagnostic stack depth when tracing is enabled.
    /// </summary>
    private void SaveCanvasState(DrawingOptions options, string reason)
    {
        this.canvas.Save(options);
        this.canvasStateDepth++;
        this.TraceState($"Save {reason}");
    }

    /// <summary>
    /// Saves a compositing layer and records diagnostic stack depth when tracing is enabled.
    /// </summary>
    private void SaveCanvasLayer(GraphicsOptions layerOptions, Rectangle bounds, string reason)
    {
        this.canvas.SaveLayer(layerOptions, bounds);
        this.canvasStateDepth++;
        this.TraceState(
            $"SaveLayer {reason} bounds={Describe(bounds)} blend={layerOptions.BlendPercentage:0.###} alpha={layerOptions.AlphaCompositionMode}");
    }

    /// <summary>
    /// Saves a compositing layer with explicit drawing options and records diagnostic stack depth.
    /// </summary>
    private void SaveCanvasLayer(GraphicsOptions layerOptions, Rectangle bounds, DrawingOptions options, string reason)
    {
        this.canvas.SaveLayer(layerOptions, bounds, options);
        this.canvasStateDepth++;
        this.TraceState(
            $"SaveLayer {reason} bounds={Describe(bounds)} blend={layerOptions.BlendPercentage:0.###} alpha={layerOptions.AlphaCompositionMode}");
    }

    /// <summary>
    /// Saves an effect compositing layer and records diagnostic stack depth when tracing is enabled.
    /// </summary>
    private void SaveCanvasLayer(GraphicsOptions layerOptions, Rectangle bounds, LayerEffect effect, DrawingOptions options, string reason)
    {
        this.canvas.SaveLayer(layerOptions, bounds, effect, options);
        this.canvasStateDepth++;
        this.TraceState(
            $"SaveLayer {reason} effect={effect.GetType().Name} bounds={Describe(bounds)} blend={layerOptions.BlendPercentage:0.###} alpha={layerOptions.AlphaCompositionMode}");
    }

    /// <summary>
    /// Saves an effect compositing layer over a path region and records diagnostic stack depth
    /// when tracing is enabled.
    /// </summary>
    private void SaveCanvasLayer(GraphicsOptions layerOptions, IPath region, LayerEffect effect, DrawingOptions options, string reason)
    {
        this.canvas.SaveLayer(layerOptions, region, effect, options);
        this.canvasStateDepth++;
        this.TraceState(
            $"SaveLayer {reason} effect={effect.GetType().Name} bounds={Describe(region.Bounds)} blend={layerOptions.BlendPercentage:0.###} alpha={layerOptions.AlphaCompositionMode}");
    }

    /// <summary>
    /// Restores a canvas state and records diagnostic stack depth when tracing is enabled.
    /// </summary>
    private void RestoreCanvasState(string reason)
    {
        this.canvas.Restore();
        this.canvasStateDepth--;
        this.TraceState($"Restore {reason}");
    }

    /// <summary>
    /// Saves the canvas state with the current transform, clip, fill-rule options, and draw opacity.
    /// </summary>
    private void PushDrawingState(IntersectionRule fillRule, float blendPercentage)
        => this.SaveCanvasState(this.CreateDrawingOptions(fillRule, blendPercentage), $"fillRule={fillRule} blend={blendPercentage:0.###}");

    /// <summary>
    /// Saves the canvas state with an explicit antialiasing value.
    /// </summary>
    private void PushDrawingState(IntersectionRule fillRule, bool antialias, float blendPercentage = 1)
    {
        DrawingOptions options = this.CreateDrawingOptions(fillRule, blendPercentage);
        options.GraphicsOptions.Antialias = antialias;

        this.SaveCanvasState(options, $"fillRule={fillRule} antialias={antialias} blend={blendPercentage:0.###}");
    }

    /// <summary>
    /// Creates ImageSharp drawing options from the active Avalonia drawing state.
    /// </summary>
    private DrawingOptions CreateDrawingOptions(IntersectionRule fillRule)
        => new()
        {
            Transform = this.Transform.ToMatrix4x4(),
            IntersectionRule = fillRule
        };

    /// <summary>
    /// Creates ImageSharp drawing options with Avalonia opacity mapped to brush blend amount.
    /// </summary>
    private DrawingOptions CreateDrawingOptions(IntersectionRule fillRule, float blendPercentage)
    {
        DrawingOptions options = this.CreateDrawingOptions(fillRule);
        options.GraphicsOptions.BlendPercentage = blendPercentage;

        return options;
    }

    /// <summary>
    /// Saves the current transform before Avalonia pushes a nested drawing state.
    /// </summary>
    private void SaveTransform()
    {
        this.transforms.Push(this.Transform);
        this.TraceState("SaveTransform");
    }

    /// <summary>
    /// Restores the transform saved by the matching push operation.
    /// </summary>
    private void RestoreTransform()
    {
        this.Transform = this.transforms.Pop();
        this.TraceState("RestoreTransform");
    }

    /// <summary>
    /// Converts an Avalonia pen into an ImageSharp pen for the supplied target bounds.
    /// </summary>
    private Pen? CreatePen(IPen? pen, Rect targetRect)
    {
        if (pen is null || pen.Thickness <= 0)
        {
            return null;
        }

        Brush? brush = this.CreateBrush(pen.Brush, targetRect);

        return brush is null ? null : pen.ToPen(brush);
    }

    /// <summary>
    /// Draws non-inset rectangle shadows before the rectangle fill/stroke.
    /// </summary>
    private void DrawOuterBoxShadows(RoundedRect rect, IPath contentPath, BoxShadows boxShadows)
    {
        if (boxShadows.Count == 0)
        {
            return;
        }

        foreach (BoxShadow shadow in boxShadows)
        {
            if (shadow == default || shadow.Color.A == 0 || shadow.IsInset)
            {
                continue;
            }

            this.DrawOuterBoxShadow(rect, contentPath, shadow);
        }
    }

    /// <summary>
    /// Draws inset rectangle shadows after the rectangle fill and before its stroke.
    /// </summary>
    private void DrawInnerBoxShadows(RoundedRect rect, IPath contentPath, BoxShadows boxShadows)
    {
        foreach (BoxShadow shadow in boxShadows)
        {
            if (shadow == default || shadow.Color.A == 0 || !shadow.IsInset)
            {
                continue;
            }

            this.DrawInnerBoxShadow(rect, contentPath, shadow);
        }
    }

    /// <summary>
    /// Draws one inset shadow. Gaussian blur is linear and preserves constants, so the
    /// blurred complement of the hole equals one minus the blurred hole mask: the content is
    /// filled with the shadow color and the blurred hole mask is punched out of it with a
    /// DestOut layer composite. No complement geometry padded by the blur reach is needed,
    /// because one minus the mask saturates on its own away from the hole, and the technique
    /// works for any content geometry the clip can express.
    /// </summary>
    private void DrawInnerBoxShadow(RoundedRect rect, IPath contentPath, BoxShadow shadow)
    {
        if (rect.Rect.Width <= 0 || rect.Rect.Height <= 0)
        {
            return;
        }

        // The hole the light shines through: the content deflated by the spread and shifted
        // by the offset. Everything outside it, inside the content, is in shadow.
        RoundedRect holeRect = TranslateRoundedRect(
            rect.Deflate(shadow.Spread, shadow.Spread),
            shadow.OffsetX,
            shadow.OffsetY);

        this.PushDrawingState(IntersectionRule.EvenOdd);
        this.canvas.Clip(ClipOperation.Intersection, contentPath);
        this.SaveCanvasLayer(new GraphicsOptions { BlendPercentage = (float)this.opacity }, ToEnclosingRectangle(rect.Rect), "InnerBoxShadow");
        this.canvas.Fill(Brushes.Solid(shadow.Color.ToColor()), contentPath);

        // A spread large enough to consume the content leaves no hole; the whole interior
        // stays in shadow.
        if (holeRect.Rect.Width > 0 && holeRect.Rect.Height > 0)
        {
            IPath holePath = holeRect.IsRounded
                ? holeRect.ToRoundedRectanglePath()
                : new RectanglePolygon(holeRect.Rect.ToRectangleF());

            // The hole mask must be opaque so the composite removes the shadow entirely
            // inside the hole; the shadow color's own alpha applies to the shadow, not here.
            // The hole is drawn through a blur effect layer so the mask feathers before the
            // DestOut composite; a zero blur passes through unchanged.
            this.SaveCanvasLayer(
                new GraphicsOptions { AlphaCompositionMode = PixelAlphaCompositionMode.DestOut },
                ToEnclosingRectangle(holeRect.Rect),
                new WebGPUGaussianBlurLayerEffect(BlurRadiusToSigma(shadow.Blur)),
                this.CreateDrawingOptions(IntersectionRule.EvenOdd),
                "InnerBoxShadow hole");

            this.canvas.Fill(Brushes.Solid(Color.Black), holePath);
            this.RestoreCanvasState("InnerBoxShadow hole layer");
        }

        this.RestoreCanvasState("InnerBoxShadow layer");
        this.RestoreCanvasState("InnerBoxShadow intersection clip");
    }

    /// <summary>
    /// Draws one non-inset shadow.
    /// </summary>
    private void DrawOuterBoxShadow(RoundedRect rect, IPath contentPath, BoxShadow shadow)
    {
        RoundedRect shadowRect = TranslateRoundedRect(
            rect.Inflate(shadow.Spread, shadow.Spread),
            shadow.OffsetX,
            shadow.OffsetY);

        IPath shadowPath = shadowRect.IsRounded
            ? shadowRect.ToRoundedRectanglePath()
            : new RectanglePolygon(shadowRect.Rect.ToRectangleF());

        if (shadow.Blur > 0)
        {
            // The shadow geometry fills a blur effect layer; the canvas blurs it when the layer is
            // restored, and the layer's blend carries the ambient opacity.
            this.PushDrawingState(IntersectionRule.EvenOdd);
            this.canvas.Clip(ClipOperation.Difference, contentPath);
            this.SaveCanvasLayer(
                new GraphicsOptions { BlendPercentage = (float)this.opacity },
                ToEnclosingRectangle(shadowRect.Rect),
                new WebGPUGaussianBlurLayerEffect(BlurRadiusToSigma(shadow.Blur)),
                this.CreateDrawingOptions(IntersectionRule.EvenOdd),
                "OuterBoxShadow blur");

            this.canvas.Fill(Brushes.Solid(shadow.Color.ToColor()), shadowPath);
            this.RestoreCanvasState("OuterBoxShadow blur layer");
            this.RestoreCanvasState("OuterBoxShadow difference clip");

            return;
        }

        this.FillPath(Brushes.Solid(shadow.Color.ToColor()), new ComplexPolygon(shadowPath, contentPath), IntersectionRule.EvenOdd, (float)this.opacity);
    }

    /// <summary>
    /// Converts an Avalonia blur radius to the sigma used by ImageSharp's Gaussian blur.
    /// </summary>
    private static float BlurRadiusToSigma(double radius)
        => radius <= 0 ? 0 : (float)radius / 3F;

    /// <summary>
    /// Converts a floating-point Avalonia rectangle to a pixel rectangle that fully contains it.
    /// </summary>
    private static Rectangle ToEnclosingRectangle(Rect rect)
    {
        int left = (int)Math.Floor(rect.Left);
        int top = (int)Math.Floor(rect.Top);
        int right = (int)Math.Ceiling(rect.Right);
        int bottom = (int)Math.Ceiling(rect.Bottom);

        return new Rectangle(left, top, right - left, bottom - top);
    }

    /// <summary>
    /// Converts a floating-point ImageSharp rectangle to a pixel rectangle that fully contains it.
    /// </summary>
    private static Rectangle ToEnclosingRectangle(RectangleF rect)
    {
        int left = (int)Math.Floor(rect.Left);
        int top = (int)Math.Floor(rect.Top);
        int right = (int)Math.Ceiling(rect.Right);
        int bottom = (int)Math.Ceiling(rect.Bottom);

        return new Rectangle(left, top, right - left, bottom - top);
    }

    /// <summary>
    /// Translates a rounded rectangle without changing its corner radii.
    /// </summary>
    private static RoundedRect TranslateRoundedRect(RoundedRect rect, double offsetX, double offsetY)
        => new(
            rect.Rect.Translate(new AvaloniaVector(offsetX, offsetY)),
            rect.RadiiTopLeft,
            rect.RadiiTopRight,
            rect.RadiiBottomRight,
            rect.RadiiBottomLeft);

    /// <summary>
    /// Fills a path using the caller-supplied fill rule.
    /// </summary>
    private void FillPath(Brush brush, IPath path, IntersectionRule fillRule, float blendPercentage = 1)
    {
        this.Trace(() => $"FillPath fillRule={fillRule} pathBounds={Describe(path.Bounds)} transform={Describe(this.Transform)}");
        this.PushDrawingState(fillRule, blendPercentage);
        this.canvas.Fill(brush, path);
        this.RestoreCanvasState("FillPath state");
    }

    /// <summary>
    /// Converts an Avalonia gradient brush into the matching ImageSharp gradient brush.
    /// </summary>
    private static Brush? CreateGradientBrush(IGradientBrush gradient, Rect targetRect)
    {
        ColorStop[] stops = ToColorStops(gradient.GradientStops);
        GradientRepetitionMode repetitionMode = gradient.SpreadMethod.ToGradientRepetitionMode();
        ILinearGradientBrush? linear = gradient as ILinearGradientBrush;

        if (linear is not null)
        {
            PointF start = linear.StartPoint.ToPixels(targetRect).ToPointF();
            PointF end = linear.EndPoint.ToPixels(targetRect).ToPointF();

            return new LinearGradientBrush(start, end, repetitionMode, stops);
        }

        IRadialGradientBrush? radial = gradient as IRadialGradientBrush;

        if (radial is not null)
        {
            PointF center = radial.Center.ToPixels(targetRect).ToPointF();
            PointF origin = radial.GradientOrigin.ToPixels(targetRect).ToPointF();
            float radiusX = (float)radial.RadiusX.ToValue(targetRect.Width);
            float radiusY = (float)radial.RadiusY.ToValue(targetRect.Height);

            if (origin == center && radiusX != radiusY)
            {
                return new EllipticGradientBrush(
                    center,
                    new PointF(center.X + radiusX, center.Y),
                    radiusY / radiusX,
                    repetitionMode,
                    stops);
            }

            return origin == center
                ? new RadialGradientBrush(center, radiusX, repetitionMode, stops)
                : new RadialGradientBrush(origin, 0, center, radiusX, repetitionMode, stops);
        }

        IConicGradientBrush? conic = gradient as IConicGradientBrush;

        if (conic is not null)
        {
            PointF center = conic.Center.ToPixels(targetRect).ToPointF();
            float startAngle = (float)(90 - conic.Angle);

            return new SweepGradientBrush(center, startAngle, startAngle + 360, repetitionMode, stops);
        }

        return null;
    }

    /// <summary>
    /// Converts an Avalonia image brush into an ImageSharp tiled image brush.
    /// </summary>
    private ImageBrush<Bgra32P>? CreateImageBrush(IImageBrush brush, Rect targetRect)
    {
        IDrawableBitmapImpl? bitmap = brush.Source?.GetBitmap() as IDrawableBitmapImpl;

        if (bitmap is null)
        {
            return null;
        }

        // Skia feeds TileBrushCalculator the bitmap size in the current drawing DPI, then draws
        // from source pixels into that logical size. Keep the same split here: layout in Avalonia
        // DIPs, source sampling through the bitmap implementation.
        AvaloniaSize sourceSize = bitmap.PixelSize.ToSizeWithDpi(this.dpi);
        return this.CreateTileBrush(bitmap, sourceSize, brush, targetRect);
    }

    /// <summary>
    /// Converts an Avalonia scene brush into an ImageSharp tiled image brush.
    /// </summary>
    private ImageBrush<Bgra32P>? CreateSceneBrushContent(global::Avalonia.Media.ISceneBrushContent content, Rect targetRect)
    {
        Rect contentRect = content.Rect;
        int width = (int)Math.Ceiling(contentRect.Width);
        int height = (int)Math.Ceiling(contentRect.Height);

        if (width < 1 || height < 1)
        {
            return null;
        }

        Image<Bgra32P> sourceImage = new(WriteableBitmapImpl.ImageConfiguration, width, height);

        using (DrawingContextImpl context = new(sourceImage, this.dpi))
        {
            // Scene brushes render from their own content bounds, not from the bounds of the
            // visual being painted:
            //
            // +----------------------+  target visual
            // |                      |
            // |   +-----+----+       |  content bounds
            // |   |     |    |       |
            // |   +-----+    |       |
            // |        +--+  |       |
            // |        +--+  |       |
            // +----------------------+
            //
            // Translate non-zero content bounds back to the intermediate origin, matching Skia's
            // VisualBrush path before the generated image is tiled over the target box.
            Matrix? transform = contentRect.TopLeft == default
                ? null
                : Matrix.CreateTranslation(-contentRect.X, -contentRect.Y);

            context.PushRenderOptions(this.currentRenderOptions);
            context.Clear(global::Avalonia.Media.Colors.Transparent);
            content.Render(context, transform);
            context.PopRenderOptions();
        }

        using WriteableBitmapImpl sourceBitmap = new(sourceImage, this.dpi);

        return this.CreateTileBrush(sourceBitmap, contentRect.Size, content.Brush, targetRect);
    }

    /// <summary>
    /// Converts a source bitmap and Avalonia tile brush settings into an ImageSharp tiled image brush.
    /// </summary>
    private ImageBrush<Bgra32P>? CreateTileBrush(
        IDrawableBitmapImpl sourceBitmap,
        AvaloniaSize sourceSize,
        ITileBrush brush,
        Rect targetRect)
    {
        TileBrushLayout layout = CalculateTileBrushLayout(brush, sourceSize, targetRect);

        if (!layout.IsValid)
        {
            return null;
        }

        Image<Bgra32P> tileImage = new(WriteableBitmapImpl.ImageConfiguration, layout.IntermediateWidth, layout.IntermediateHeight);

        using (DrawingContextImpl context = new(tileImage, this.dpi))
        {
            context.Clear(global::Avalonia.Media.Colors.Transparent);
            context.PushClip(layout.IntermediateClip);
            context.PushRenderOptions(this.currentRenderOptions);
            context.Transform = layout.IntermediateTransform;
            context.DrawBitmap(sourceBitmap, 1, new Rect(sourceBitmap.PixelSize.ToSize(1)), new Rect(sourceSize));
            context.PopRenderOptions();
            context.PopClip();
        }

        this.brushImages.Add(tileImage);

        (WrapMode wrapX, WrapMode wrapY) = ToWrapModes(brush.TileMode);
        return new ImageBrush<Bgra32P>(tileImage, tileImage.Bounds, layout.Offset, wrapX, wrapY);
    }

    /// <summary>
    /// Maps Avalonia tile modes to ImageSharp wrap modes.
    /// </summary>
    private static (WrapMode X, WrapMode Y) ToWrapModes(TileMode tileMode)
        => tileMode switch
        {
            TileMode.Tile => (WrapMode.Repeat, WrapMode.Repeat),
            TileMode.FlipX => (WrapMode.Mirror, WrapMode.Repeat),
            TileMode.FlipY => (WrapMode.Repeat, WrapMode.Mirror),
            TileMode.FlipXY => (WrapMode.Mirror, WrapMode.Mirror),
            _ => (WrapMode.None, WrapMode.None),
        };

    /// <summary>
    /// Converts Avalonia gradient stops to ImageSharp color stops.
    /// </summary>
    private static ColorStop[] ToColorStops(IReadOnlyList<IGradientStop> gradientStops)
    {
        ColorStop[] stops = new ColorStop[gradientStops.Count];

        for (int i = 0; i < gradientStops.Count; i++)
        {
            IGradientStop stop = gradientStops[i];
            stops[i] = new ColorStop((float)stop.Offset, stop.Color.ToColor());
        }

        return stops;
    }

    /// <summary>
    /// Calculates the ImageSharp tile image that represents Avalonia's tile brush viewbox/viewport rules.
    /// </summary>
    private static TileBrushLayout CalculateTileBrushLayout(ITileBrush brush, AvaloniaSize sourceSize, Rect targetRect)
    {
        Rect sourceRect = brush.SourceRect.ToPixels(sourceSize);
        AvaloniaSize targetSize = targetRect.Size;
        Rect destinationRect = brush.DestinationRect.ToPixels(targetSize);

        if (sourceRect.Width <= 0 || sourceRect.Height <= 0 || destinationRect.Width <= 0 || destinationRect.Height <= 0)
        {
            return default;
        }

        AvaloniaVector scale = global::Avalonia.Media.MediaExtensions.CalculateScaling(
            brush.Stretch,
            destinationRect.Size,
            sourceRect.Size);

        AvaloniaSize scaledSize = new(sourceRect.Width * scale.X, sourceRect.Height * scale.Y);
        AvaloniaVector translate = CalculateTileTranslate(
            brush.AlignmentX,
            brush.AlignmentY,
            scaledSize,
            destinationRect.Size);

        bool isTiled = brush.TileMode != TileMode.None;
        AvaloniaSize intermediateSize = isTiled ? destinationRect.Size : targetSize;
        Matrix intermediateTransform =
            Matrix.CreateTranslation(-sourceRect.Position) *
            Matrix.CreateScale(scale) *
            Matrix.CreateTranslation(translate);

        Rect intermediateClip;

        if (isTiled)
        {
            intermediateClip = new Rect(destinationRect.Size);
        }
        else
        {
            intermediateClip = destinationRect;
            intermediateTransform *= Matrix.CreateTranslation(destinationRect.Position);
        }

        int intermediateWidth = Math.Max(1, (int)Math.Ceiling(intermediateSize.Width));
        int intermediateHeight = Math.Max(1, (int)Math.Ceiling(intermediateSize.Height));

        // Relative viewports are anchored to the painted target. Absolute viewports are anchored
        // in drawing coordinates, so subtract the target origin before ImageSharp adds it back
        // when creating the brush renderer for the painted shape.
        double offsetX = isTiled ? destinationRect.X : 0;
        double offsetY = isTiled ? destinationRect.Y : 0;

        if (brush.DestinationRect.Unit == RelativeUnit.Absolute)
        {
            offsetX -= targetRect.X;
            offsetY -= targetRect.Y;
        }

        SixLabors.ImageSharp.Point offset = new((int)Math.Floor(offsetX), (int)Math.Floor(offsetY));
        return new TileBrushLayout(intermediateClip, intermediateTransform, intermediateWidth, intermediateHeight, offset);
    }

    /// <summary>
    /// Calculates Avalonia tile alignment translation for the scaled source size.
    /// </summary>
    private static AvaloniaVector CalculateTileTranslate(AlignmentX alignmentX, AlignmentY alignmentY, AvaloniaSize sourceSize, AvaloniaSize destinationSize)
    {
        double x = alignmentX switch
        {
            AlignmentX.Center => (destinationSize.Width - sourceSize.Width) / 2,
            AlignmentX.Right => destinationSize.Width - sourceSize.Width,
            _ => 0
        };

        double y = alignmentY switch
        {
            AlignmentY.Center => (destinationSize.Height - sourceSize.Height) / 2,
            AlignmentY.Bottom => destinationSize.Height - sourceSize.Height,
            _ => 0
        };

        return new AvaloniaVector(x, y);
    }

    /// <summary>
    /// Holds the baked tile image placement required by an Avalonia tile brush.
    /// </summary>
    private readonly struct TileBrushLayout
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="TileBrushLayout"/> struct.
        /// </summary>
        /// <param name="intermediateClip">The tile-space clip applied while drawing the source image.</param>
        /// <param name="intermediateTransform">The tile-space transform applied while drawing the source image.</param>
        /// <param name="intermediateWidth">The baked tile image width.</param>
        /// <param name="intermediateHeight">The baked tile image height.</param>
        /// <param name="offset">The offset that anchors the baked tile in drawing coordinates.</param>
        public TileBrushLayout(
            Rect intermediateClip,
            Matrix intermediateTransform,
            int intermediateWidth,
            int intermediateHeight,
            SixLabors.ImageSharp.Point offset)
        {
            this.IntermediateClip = intermediateClip;
            this.IntermediateTransform = intermediateTransform;
            this.IntermediateWidth = intermediateWidth;
            this.IntermediateHeight = intermediateHeight;
            this.Offset = offset;
            this.IsValid = true;
        }

        /// <summary>
        /// Gets a value indicating whether the layout contains drawable tile geometry.
        /// </summary>
        public bool IsValid { get; }

        /// <summary>
        /// Gets the tile-space clip applied while drawing the source image.
        /// </summary>
        public Rect IntermediateClip { get; }

        /// <summary>
        /// Gets the tile-space transform applied while drawing the source image.
        /// </summary>
        public Matrix IntermediateTransform { get; }

        /// <summary>
        /// Gets the baked tile image width.
        /// </summary>
        public int IntermediateWidth { get; }

        /// <summary>
        /// Gets the baked tile image height.
        /// </summary>
        public int IntermediateHeight { get; }

        /// <summary>
        /// Gets the offset that anchors the baked tile in drawing coordinates.
        /// </summary>
        public SixLabors.ImageSharp.Point Offset { get; }
    }

    /// <summary>
    /// Writes a trace entry when tracing is enabled for this context.
    /// </summary>
    private void Trace(Func<string> message)
    {
        if (this.trace is null)
        {
            return;
        }

        this.trace.Append(this.traceOperationIndex++).Append(": ").AppendLine(message());
    }

    /// <summary>
    /// Writes the current sample and canvas state stack depths to the active diagnostic trace.
    /// </summary>
    private void TraceState(string action)
    {
        if (this.trace is null)
        {
            return;
        }

        this.trace
            .Append(this.traceOperationIndex++)
            .Append(": ")
            .Append(action)
            .Append(" canvasDepth=")
            .Append(this.canvasStateDepth)
            .Append(" transformDepth=")
            .Append(this.transforms.Count)
            .Append(" opacityDepth=")
            .Append(this.opacities.Count)
            .Append(" opacityLayerDepth=")
            .Append(this.opacityLayers.Count)
            .Append(" opacityMaskDepth=")
            .Append(this.opacityMasks.Count)
            .Append(" renderOptionsDepth=")
            .Append(this.renderOptions.Count)
            .Append(" textOptionsDepth=")
            .Append(this.textOptions.Count)
            .Append(" opacity=")
            .Append(this.opacity.ToString("0.###"))
            .Append(" transform=")
            .AppendLine(Describe(this.Transform));
    }

    /// <summary>
    /// Determines whether the next drawing context should capture operation-level diagnostics.
    /// </summary>
    private static bool ShouldTraceContext(bool ownsImage, int width, int height)
    {
        if (DebugTraceNextFrame || DebugSaveNextFrame)
        {
            DebugTraceNextFrame = false;
            return true;
        }

        if (ownsImage && Environment.GetEnvironmentVariable("IMAGESHARP_DRAWING_TRACE_FRAMEBUFFER_CONTEXTS") == "1")
        {
            return true;
        }

        if (width >= 256 && height >= 256 && Environment.GetEnvironmentVariable("IMAGESHARP_DRAWING_TRACE_LARGE_CONTEXTS") == "1")
        {
            return true;
        }

        if (Environment.GetEnvironmentVariable("IMAGESHARP_DRAWING_TRACE_ALL_CONTEXTS") == "1")
        {
            return true;
        }

        if (!environmentTraceConsumed && Environment.GetEnvironmentVariable("IMAGESHARP_DRAWING_TRACE_NEXT_FRAME") == "1")
        {
            environmentTraceConsumed = true;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Determines whether every drawing context should save its rendered image.
    /// </summary>
    private static bool ShouldSaveContextImage()
        => Environment.GetEnvironmentVariable("IMAGESHARP_DRAWING_SAVE_ALL_CONTEXTS") == "1";

    /// <summary>
    /// Gets the directory used for on-demand image and trace captures.
    /// </summary>
    private static string GetCaptureDirectory()
    {
        string? directory = Environment.GetEnvironmentVariable("IMAGESHARP_DRAWING_CAPTURE_DIRECTORY");

        if (string.IsNullOrWhiteSpace(directory))
        {
            return System.IO.Path.GetTempPath();
        }

        Directory.CreateDirectory(directory);

        return directory;
    }

    /// <summary>
    /// Formats an Avalonia color for diagnostic traces.
    /// </summary>
    private static string Describe(global::Avalonia.Media.Color color)
        => $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";

    /// <summary>
    /// Formats an Avalonia rectangle for diagnostic traces.
    /// </summary>
    private static string Describe(Rect rect)
        => $"({rect.X:0.###},{rect.Y:0.###},{rect.Width:0.###},{rect.Height:0.###})";

    /// <summary>
    /// Formats an ImageSharp rectangle for diagnostic traces.
    /// </summary>
    private static string Describe(RectangleF rect)
        => $"({rect.X:0.###},{rect.Y:0.###},{rect.Width:0.###},{rect.Height:0.###})";

    /// <summary>
    /// Formats an ImageSharp pixel rectangle for diagnostic traces.
    /// </summary>
    private static string Describe(Rectangle rect)
        => $"({rect.X},{rect.Y},{rect.Width},{rect.Height})";

    /// <summary>
    /// Formats a pixel rectangle for diagnostic traces.
    /// </summary>
    private static string Describe(LtrbPixelRect rect)
        => $"({rect.Left},{rect.Top},{rect.Right},{rect.Bottom})";

    /// <summary>
    /// Formats a list of pixel rectangles for diagnostic traces.
    /// </summary>
    private static string Describe(IList<LtrbPixelRect> rects)
    {
        StringBuilder builder = new();
        builder.Append(rects.Count).Append('[');

        for (int i = 0; i < rects.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(',');
            }

            builder.Append(Describe(rects[i]));
        }

        return builder.Append(']').ToString();
    }

    /// <summary>
    /// Formats an Avalonia transform matrix for diagnostic traces.
    /// </summary>
    private static string Describe(Matrix matrix)
        => $"[{matrix.M11:0.###},{matrix.M12:0.###},{matrix.M21:0.###},{matrix.M22:0.###},{matrix.M31:0.###},{matrix.M32:0.###}]";

    /// <summary>
    /// Formats an Avalonia brush for diagnostic traces.
    /// </summary>
    private static string DescribeBrush(IBrush? brush)
    {
        if (brush is null)
        {
            return "<null>";
        }

        if (brush is ISolidColorBrush solid)
        {
            return $"Solid({Describe(solid.Color)}, opacity={brush.Opacity:0.###})";
        }

        return $"{brush.GetType().Name}(opacity={brush.Opacity:0.###})";
    }

    /// <summary>
    /// Formats an Avalonia pen for diagnostic traces.
    /// </summary>
    private static string DescribePen(IPen? pen)
    {
        if (pen is null)
        {
            return "<null>";
        }

        return $"Pen(thickness={pen.Thickness:0.###}, brush={DescribeBrush(pen.Brush)})";
    }
}
