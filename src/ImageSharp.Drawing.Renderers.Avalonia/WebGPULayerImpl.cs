// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Processing.Backends;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing.Processors.Transforms;

namespace SixLabors.ImageSharp.Drawing.Renderers.Avalonia;

/// <summary>
/// WebGPU-backed Avalonia drawing layer for the ImageSharp.Drawing sample renderer.
/// </summary>
internal sealed class WebGPULayerImpl : IDrawingContextLayerWithRenderContextAffinityImpl, IDrawableBitmapImpl
{
    private readonly WebGPURenderTarget renderTarget;
    private DrawingCanvas? canvas;
    private DrawingContextImpl? context;
    private int version = 1;

    /// <summary>
    /// Initializes a new WebGPU-backed drawing layer.
    /// </summary>
    /// <param name="session">The WebGPU session shared with the presentation surfaces.</param>
    /// <param name="size">The layer size in pixels.</param>
    /// <param name="dpi">The layer DPI.</param>
    public WebGPULayerImpl(WebGPUSurfaceSession session, PixelSize size, Vector dpi)
        : this(session.CreateRenderTarget(WebGPUTextureFormat.Bgra8Unorm, PixelAlphaRepresentation.Associated, size.Width, size.Height), size, dpi)
    {
    }

    /// <summary>
    /// Initializes a new WebGPU-backed drawing layer.
    /// </summary>
    /// <param name="renderTarget">The render target that stores layer pixels.</param>
    /// <param name="size">The layer size in pixels.</param>
    /// <param name="dpi">The layer DPI.</param>
    public WebGPULayerImpl(WebGPURenderTarget renderTarget, PixelSize size, Vector dpi)
    {
        this.PixelSize = size;
        this.Dpi = dpi;
        this.renderTarget = renderTarget;
    }

    /// <inheritdoc />
    public Vector Dpi { get; }

    /// <inheritdoc />
    public PixelSize PixelSize { get; }

    /// <inheritdoc />
    public int Version => this.version;

    /// <inheritdoc />
    public bool CanBlit => true;

    /// <inheritdoc />
    public bool IsCorrupted => false;

    /// <inheritdoc />
    public PixelFormat? Format => AvaloniaPixelFormats.Bgra8888;

    /// <inheritdoc />
    public AlphaFormat? AlphaFormat => global::Avalonia.Platform.AlphaFormat.Premul;

    /// <inheritdoc />
    public bool HasRenderContextAffinity => true;

    /// <inheritdoc />
    public IDrawingContextImpl CreateDrawingContext()
    {
        this.version++;

        DrawingContextImpl context = new(this.renderTarget, this.Dpi, this.PixelSize, ownsCanvas: false);
        this.canvas = context.Canvas;
        this.context = context;

        return context;
    }

    /// <inheritdoc />
    public void Blit(IDrawingContextImpl context)
    {
        DrawingContextImpl drawingContext = (DrawingContextImpl)context;
        DrawingCanvas source = this.canvas!;
        DrawingContextImpl sourceContext = this.context!;

        try
        {
            drawingContext.Blit(source, this.PixelSize);
        }
        finally
        {
            source.Dispose();
            sourceContext.DisposeTransientResources();
            this.canvas = null;
            this.context = null;
        }
    }

    /// <inheritdoc />
    public IBitmapImpl CreateNonAffinedSnapshot()
    {
        this.canvas?.Dispose();
        this.context?.DisposeTransientResources();
        this.canvas = null;
        this.context = null;

        return new WriteableBitmapImpl(this.renderTarget.ReadbackImage<Bgra32P>(), this.Dpi);
    }

    /// <inheritdoc />
    public void Draw(
        DrawingContextImpl context,
        Rectangle sourceRect,
        RectangleF destinationRect,
        IResampler sampler,
        float opacity,
        PixelAlphaCompositionMode alphaCompositionMode)
    {
        this.canvas?.Dispose();
        this.context?.DisposeTransientResources();
        this.canvas = null;
        this.context = null;

        using Image<Bgra32P> image = this.renderTarget.ReadbackImage<Bgra32P>();
        context.DrawImage(image, sourceRect, destinationRect, sampler, opacity, alphaCompositionMode);
    }

    /// <inheritdoc />
    public void Save(Stream stream, BitmapEncoderOptions options)
    {
        this.canvas?.Dispose();
        this.context?.DisposeTransientResources();
        this.canvas = null;
        this.context = null;

        using Image image = this.renderTarget.ReadbackImage();
        image.Save(stream, options);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        this.canvas?.Dispose();
        this.context?.DisposeTransientResources();
        this.renderTarget.Dispose();
    }
}
