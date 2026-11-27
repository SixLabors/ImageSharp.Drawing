// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Surfaces;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing.Processors.Transforms;

namespace SixLabors.ImageSharp.Drawing.Renderers.Avalonia;

/// <summary>
/// ImageSharp-backed render-target bitmap implementation for offscreen layers and render-target bitmaps.
/// </summary>
internal sealed class RenderTargetBitmapImpl : IRenderTargetBitmapImpl, IDrawingContextLayerImpl, IFramebufferPlatformSurface, IDrawableBitmapImpl
{
    private readonly WriteableBitmapImpl bitmap;
    private readonly RenderTargetImpl renderTarget;

    /// <summary>
    /// Initializes a new render-target bitmap.
    /// </summary>
    /// <param name="size">The bitmap size in pixels.</param>
    /// <param name="dpi">The bitmap DPI.</param>
    public RenderTargetBitmapImpl(PixelSize size, Vector dpi)
    {
        this.bitmap = new WriteableBitmapImpl(size, dpi);
        this.renderTarget = new RenderTargetImpl(this.CreateFramebufferRenderTarget());
    }

    /// <summary>
    /// Gets the ImageSharp pixels drawn for this render target.
    /// </summary>
    public Image<Bgra32P> Image => this.bitmap.Image;

    /// <inheritdoc />
    public Vector Dpi => this.bitmap.Dpi;

    /// <inheritdoc />
    public PixelSize PixelSize => this.bitmap.PixelSize;

    /// <inheritdoc />
    public int Version => this.bitmap.Version;

    /// <inheritdoc />
    public PixelFormat? Format => this.bitmap.Format;

    /// <inheritdoc />
    public AlphaFormat? AlphaFormat => this.bitmap.AlphaFormat;

    /// <inheritdoc />
    public bool CanBlit => true;

    /// <inheritdoc />
    public bool IsCorrupted => false;

    /// <inheritdoc />
    public void Blit(IDrawingContextImpl context)
    {
        DrawingContextImpl drawingContext = (DrawingContextImpl)context;
        drawingContext.Blit(this.Image, this.PixelSize);
    }

    /// <inheritdoc />
    public void Draw(
        DrawingContextImpl context,
        Rectangle sourceRect,
        RectangleF destinationRect,
        IResampler sampler,
        float opacity,
        PixelAlphaCompositionMode alphaCompositionMode)
        => context.DrawImage(this.Image, sourceRect, destinationRect, sampler, opacity, alphaCompositionMode);

    /// <inheritdoc />
    public IDrawingContextImpl CreateDrawingContext()
        => this.renderTarget.CreateDrawingContext(
            new IRenderTarget.RenderTargetSceneInfo(this.PixelSize, this.Dpi.X / 96D, global::Avalonia.Rendering.Composition.CompositionTransparencyLevel.None),
            out _);

    /// <inheritdoc />
    public ILockedFramebuffer Lock() => this.bitmap.Lock();

    /// <inheritdoc />
    public IFramebufferRenderTarget CreateFramebufferRenderTarget()
        => new FuncFramebufferRenderTarget(
            (IRenderTarget.RenderTargetSceneInfo _, out FramebufferLockProperties properties) =>
            {
                // Dirty-rect rendering clears and redraws only the active clip, so pixels outside
                // that clip must remain from the previous lock until the layer is copied out.
                properties = new FramebufferLockProperties(PreviousFrameIsRetained: true);
                return this.Lock();
            },
            retainsFrameContents: true);

    /// <inheritdoc />
    public void Save(Stream stream, BitmapEncoderOptions options) => this.bitmap.Save(stream, options);

    /// <inheritdoc />
    public void Dispose()
    {
        this.renderTarget.Dispose();
        this.bitmap.Dispose();
    }
}
