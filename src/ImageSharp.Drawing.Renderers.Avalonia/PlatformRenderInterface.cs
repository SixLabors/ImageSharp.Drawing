// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System;
using System.IO;
using System.Numerics;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Fonts;
using Avalonia.Media.Imaging;
using Avalonia.Media.TextFormatting;
using Avalonia.Platform;
using Avalonia.Platform.Surfaces;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Processing.Backends;
using SixLabors.ImageSharp.Drawing.Text;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Processing.Processors.Transforms;
using AvaloniaGlyphRun = Avalonia.Media.GlyphRun;

namespace SixLabors.ImageSharp.Drawing.Renderers.Avalonia;

/// <summary>
/// Avalonia rendering platform implementation backed by ImageSharp.Drawing.
/// </summary>
internal sealed class PlatformRenderInterface : IPlatformRenderInterface
{
    /// <summary>
    /// Registers the ImageSharp.Drawing platform renderer and font manager with Avalonia.
    /// </summary>
    private static readonly object WebGpuLogGate = new();

    private static readonly string WebGpuLogPath =
        System.IO.Path.Combine(AppContext.BaseDirectory, "webgpu-errors.log");

    public static void Initialize()
    {
        // Surface native WebGPU validation/device-lost errors instead of letting them be
        // silently swallowed. Without this the GPU backend can corrupt the surface or lose
        // the device with no diagnostic, which reads as a mysterious hang or blanked output.
        //
        // This is a WinExe with no attached console, and the callback can fire from a native
        // thread immediately before the process dies, so the only reliable sink is a file that
        // is opened, written, flushed, and closed on every call. Debug.WriteLine is kept for the
        // case where a debugger is attached (run in Debug so the wgpu validation layer is active).
        File.WriteAllText(WebGpuLogPath, $"WebGPU error log started {DateTime.Now:O}{Environment.NewLine}");
        WebGPUEnvironment.UncapturedError = static (type, message) => AppendWebGpuLog($"[WebGPU {type}] {message}");

        // The uncaptured-error callback above does NOT catch the validation error that loses the
        // device (the binding panics on the failing wgpuQueueSubmit before it routes through us).
        // wgpu's own log callback DOES surface that root error, so wire it at Warn level.
        try
        {
            WebGPUEnvironment.EnableNativeLogging(AppendWebGpuLog);
        }
        catch (Exception ex)
        {
            AppendWebGpuLog($"failed to wire wgpu log callback: {ex.Message}");
        }

        PlatformRenderInterface platform = new();

        AvaloniaLocator.CurrentMutable
            .Bind<IPlatformRenderInterface>().ToConstant(platform)
            .Bind<IFontManagerImpl>().ToConstant(new FontManagerImpl());
    }

    private static void AppendWebGpuLog(string message)
    {
        string line = $"{DateTime.Now:HH:mm:ss.fff} {message}";
        System.Diagnostics.Debug.WriteLine(line);
        try
        {
            lock (WebGpuLogGate)
            {
                File.AppendAllText(WebGpuLogPath, line + Environment.NewLine);
            }
        }
        catch (IOException)
        {
            // Never let a logging failure escape the native callback boundary.
        }
    }

    /// <inheritdoc />
    public bool SupportsIndividualRoundRects => true;

    /// <inheritdoc />
    public AlphaFormat DefaultAlphaFormat => AlphaFormat.Premul;

    /// <inheritdoc />
    public PixelFormat DefaultPixelFormat => AvaloniaPixelFormats.Bgra8888;

    /// <inheritdoc />
    public bool SupportsRegions => true;

    /// <inheritdoc />
    public IGeometryImpl CreateEllipseGeometry(Rect rect)
        => new EllipseGeometryImpl(rect);

    /// <inheritdoc />
    public IGeometryImpl CreateLineGeometry(AvaloniaPoint p1, AvaloniaPoint p2)
        => new LineGeometryImpl(p1, p2);

    /// <inheritdoc />
    public IGeometryImpl CreateRectangleGeometry(Rect rect) => new RectangleGeometryImpl(rect);

    /// <inheritdoc />
    public IStreamGeometryImpl CreateStreamGeometry() => new StreamGeometryImpl();

    /// <inheritdoc />
    public IGeometryImpl CreateGeometryGroup(FillRule fillRule, IReadOnlyList<IGeometryImpl> children)
        => new GeometryGroupImpl(fillRule, children);

    /// <inheritdoc />
    public IGeometryImpl CreateCombinedGeometry(GeometryCombineMode combineMode, IGeometryImpl g1, IGeometryImpl g2)
        => CombinedGeometryImpl.Create(combineMode, (GeometryImpl)g1, (GeometryImpl)g2);

    /// <inheritdoc />
    public IGeometryImpl BuildGlyphRunGeometry(AvaloniaGlyphRun glyphRun)
    {
        SixLabors.Fonts.Font font = ((PlatformTypeface)glyphRun.GlyphTypeface.PlatformTypeface).CreateFont((float)glyphRun.FontRenderingEmSize);
        GlyphRunImpl glyphRunImpl = new(glyphRun.GlyphTypeface, glyphRun.FontRenderingEmSize, glyphRun.GlyphInfos, glyphRun.BaselineOrigin, font);
        RichGlyphOptions options = new()
        {
            Font = font,
            HintingMode = HintingMode.None,

            // Avalonia positions glyph runs by their baseline origins, so the run must be
            // anchored by the alphabetic baseline rather than the line-box default.
            TextBaseline = TextBaseline.Alphabetic,
            ColorFontSupport = ColorFontSupport.ColrV0 | ColorFontSupport.ColrV1 | ColorFontSupport.Svg
        };

        ComplexPolygon glyphPath = new(TextBuilder.GeneratePaths(glyphRunImpl.GlyphIds.Span, glyphRunImpl.GlyphOrigins.Span, options));

        return new StreamGeometryImpl(glyphPath, glyphPath, IntersectionRule.NonZero);
    }

    /// <inheritdoc />
    public IRenderTargetBitmapImpl CreateRenderTargetBitmap(PixelSize size, AvaloniaVector dpi)
        => new RenderTargetBitmapImpl(size, dpi);

    /// <inheritdoc />
    public IWriteableBitmapImpl CreateWriteableBitmap(PixelSize size, AvaloniaVector dpi, PixelFormat format, AlphaFormat alphaFormat)
        => new WriteableBitmapImpl(size, dpi, format, alphaFormat);

    /// <inheritdoc />
    public IBitmapImpl LoadBitmap(string fileName)
    {
        using FileStream stream = File.OpenRead(fileName);

        return this.LoadBitmap(stream);
    }

    /// <inheritdoc />
    public IBitmapImpl LoadBitmap(Stream stream)
        => new ImmutableBitmapImpl(stream);

    /// <inheritdoc />
    public IWriteableBitmapImpl LoadWriteableBitmapToWidth(Stream stream, int width, BitmapInterpolationMode interpolationMode = BitmapInterpolationMode.HighQuality)
        => new WriteableBitmapImpl(stream, width, horizontal: true, interpolationMode);

    /// <inheritdoc />
    public IWriteableBitmapImpl LoadWriteableBitmapToHeight(Stream stream, int height, BitmapInterpolationMode interpolationMode = BitmapInterpolationMode.HighQuality)
        => new WriteableBitmapImpl(stream, height, horizontal: false, interpolationMode);

    /// <inheritdoc />
    public IWriteableBitmapImpl LoadWriteableBitmap(string fileName)
    {
        using FileStream stream = File.OpenRead(fileName);

        return this.LoadWriteableBitmap(stream);
    }

    /// <inheritdoc />
    public IWriteableBitmapImpl LoadWriteableBitmap(Stream stream)
        => new WriteableBitmapImpl(stream);

    /// <inheritdoc />
    public IBitmapImpl LoadBitmapToWidth(Stream stream, int width, BitmapInterpolationMode interpolationMode = BitmapInterpolationMode.HighQuality)
        => new ImmutableBitmapImpl(stream, width, horizontal: true, interpolationMode);

    /// <inheritdoc />
    public IBitmapImpl LoadBitmapToHeight(Stream stream, int height, BitmapInterpolationMode interpolationMode = BitmapInterpolationMode.HighQuality)
        => new ImmutableBitmapImpl(stream, height, horizontal: false, interpolationMode);

    /// <inheritdoc />
    public IBitmapImpl ResizeBitmap(IBitmapImpl bitmapImpl, PixelSize destinationSize, BitmapInterpolationMode interpolationMode = BitmapInterpolationMode.HighQuality)
    {
        ImmutableBitmapImpl? bitmap = bitmapImpl as ImmutableBitmapImpl;

        if (bitmap is not null)
        {
            return new ImmutableBitmapImpl(bitmap, destinationSize, interpolationMode);
        }

        throw new InvalidOperationException("Invalid source bitmap type.");
    }

    /// <inheritdoc />
    public unsafe IBitmapImpl LoadBitmap(PixelFormat format, AlphaFormat alphaFormat, IntPtr data, PixelSize size, AvaloniaVector dpi, int stride)
        => new ImmutableBitmapImpl(size, dpi, stride, format, alphaFormat, data);

    /// <inheritdoc />
    public IGlyphRunImpl CreateGlyphRun(GlyphTypeface glyphTypeface, double fontRenderingEmSize, IReadOnlyList<GlyphInfo> glyphInfos, AvaloniaPoint baselineOrigin)
    {
        // Resolve the exact SixLabors face this run was shaped against (via the public PlatformTypeface)
        // so the glyph ids line up - mirroring how the Skia backend carries the SKTypeface.
        SixLabors.Fonts.Font font = ((PlatformTypeface)glyphTypeface.PlatformTypeface).CreateFont((float)fontRenderingEmSize);

        return new GlyphRunImpl(glyphTypeface, fontRenderingEmSize, glyphInfos, baselineOrigin, font);
    }

    /// <inheritdoc />
    public IPlatformRenderInterfaceContext CreateBackendContext(IPlatformGraphicsContext? graphicsApiContext)
        => new PlatformRenderInterfaceContext(graphicsApiContext is not null);

    /// <inheritdoc />
    public bool IsSupportedBitmapPixelFormat(PixelFormat format)
        => format == AvaloniaPixelFormats.Rgb565
        || format == AvaloniaPixelFormats.Bgra8888
        || format == AvaloniaPixelFormats.Rgba8888;

    /// <inheritdoc />
    public IPlatformRenderInterfaceRegion CreateRegion() => new RegionImpl();
}

/// <summary>
/// ImageSharp.Drawing render-context implementation used by Avalonia render targets and layers.
/// </summary>
internal sealed class PlatformRenderInterfaceContext : IPlatformRenderInterfaceContext
{
    private readonly bool hasPlatformGraphicsContext;
    private readonly WebGPUSurfaceSession webGpuSurfaceSession = new();
    private bool useWebGpuBackend;

    /// <summary>
    /// Initializes a new backend context.
    /// </summary>
    /// <param name="hasPlatformGraphicsContext">Whether Avalonia created a platform graphics context for this renderer.</param>
    public PlatformRenderInterfaceContext(bool hasPlatformGraphicsContext)
        => this.hasPlatformGraphicsContext = hasPlatformGraphicsContext;

    /// <inheritdoc />
    public bool IsLost => this.webGpuSurfaceSession.IsLost;

    /// <inheritdoc />
    public IReadOnlyDictionary<Type, object> PublicFeatures { get; } = new Dictionary<Type, object>();

    /// <inheritdoc />
    public PixelSize? MaxOffscreenRenderTargetPixelSize => null;

    /// <inheritdoc />
    public IRenderTarget CreateRenderTarget(IEnumerable<IPlatformRenderSurface> surfaces)
    {
        bool useWebGpu = DrawingContextImpl.BackendMode == DrawingBackendMode.WebGpu
            || (DrawingContextImpl.BackendMode == DrawingBackendMode.Auto && this.hasPlatformGraphicsContext);

        if (useWebGpu)
        {
            foreach (IPlatformRenderSurface surface in surfaces)
            {
                if (surface is INativePlatformHandleSurface nativeSurface &&
                    WebGPURenderTargetImpl.TryCreate(this.webGpuSurfaceSession, nativeSurface, out WebGPURenderTargetImpl? renderTarget))
                {
                    this.useWebGpuBackend = true;
                    return renderTarget;
                }
            }

            if (DrawingContextImpl.BackendMode == DrawingBackendMode.WebGpu)
            {
                throw new NotSupportedException("WebGPU rendering requires an Avalonia native surface with enough platform handles to create a WebGPU surface.");
            }
        }

        foreach (IPlatformRenderSurface surface in surfaces)
        {
            if (surface is IFramebufferPlatformSurface framebufferSurface)
            {
                this.useWebGpuBackend = false;
                return new RenderTargetImpl(framebufferSurface.CreateFramebufferRenderTarget());
            }
        }

        throw new NotSupportedException("ImageSharp.Drawing sample renderer requires a framebuffer surface.");
    }

    /// <inheritdoc />
    public IDrawingContextLayerImpl CreateOffscreenRenderTarget(PixelSize pixelSize, AvaloniaVector scaling, bool enableTextAntialiasing)
    {
        AvaloniaVector dpi = scaling * 96;
        if (this.useWebGpuBackend)
        {
            return new WebGPULayerImpl(this.webGpuSurfaceSession, pixelSize, dpi);
        }

        return new RenderTargetBitmapImpl(pixelSize, dpi);
    }

    /// <inheritdoc />
    public object? TryGetFeature(Type featureType) => null;

    /// <inheritdoc />
    public void Dispose()
    {
        this.webGpuSurfaceSession.Dispose();
    }

}
