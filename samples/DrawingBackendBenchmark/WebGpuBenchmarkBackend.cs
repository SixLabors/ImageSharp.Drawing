// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Silk.NET.WebGPU;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Processing.Backends;
using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.PixelFormats;
using Rectangle = SixLabors.ImageSharp.Rectangle;

namespace DrawingBackendBenchmark;

/// <summary>
/// Small offscreen WebGPU host used by the sample so the benchmark can drive the real backend without a windowed swapchain.
/// </summary>
internal sealed unsafe class WebGpuBenchmarkBackend : IBenchmarkBackend, IDisposable
{
    private readonly WebGPUDrawingBackend backend;
    private readonly Configuration configuration;
    private readonly WebGPU api;
    private Instance* instance;
    private Adapter* adapter;
    private Device* device;
    private Queue* queue;
    private Texture* texture;
    private TextureView* textureView;
    private NativeSurface? surface;
    private Image<Bgra32>? cpuImage;
    private HybridCanvasFrame<Bgra32>? frame;
    private int textureWidth;
    private int textureHeight;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebGpuBenchmarkBackend"/> class.
    /// </summary>
    private WebGpuBenchmarkBackend(WebGPUDrawingBackend backend)
    {
        this.backend = backend;
        this.configuration = Configuration.Default.Clone();
        this.api = WebGPU.GetApi();
        this.InitializeDevice();
        this.configuration.SetDrawingBackend(this.backend);
    }

    public static bool TryCreate([NotNullWhen(true)] out WebGpuBenchmarkBackend? result, [NotNullWhen(false)] out string? error)
    {
        WebGPUDrawingBackend backend = new();
        if (!backend.IsSupported)
        {
            result = null;
            error = "WebGPU unsupported";
            return false;
        }

        result = new WebGpuBenchmarkBackend(backend);
        error = result.InitializeDevice();
        return error is null;
    }

    /// <summary>
    /// Renders the benchmark scene through the WebGPU backend and optionally captures a readback preview.
    /// </summary>
    public BenchmarkRenderResult Render(ReadOnlySpan<VisualLine> lines, int width, int height, bool capturePreview)
    {
        this.ThrowIfUnavailable();
        this.EnsureRenderTarget(width, height);

        Stopwatch stopwatch = Stopwatch.StartNew();
        using (DrawingCanvas<Bgra32> canvas = new(this.configuration, this.frame!, new DrawingOptions()))
        {
            VisualLine.RenderLinesToCanvas(canvas, lines);
            canvas.Flush();
        }

        stopwatch.Stop();

        Image<Bgra32>? preview = null;
        if (capturePreview)
        {
            preview = new Image<Bgra32>(width, height);
            Buffer2DRegion<Bgra32> destination = new(preview.Frames.RootFrame.PixelBuffer, preview.Bounds);
            if (!this.backend.TryReadRegion(this.configuration, this.frame!, new Rectangle(0, 0, width, height), destination))
            {
                preview.Dispose();
                throw new InvalidOperationException("WebGPU readback failed.");
            }
        }

        return new BenchmarkRenderResult(
            stopwatch.Elapsed.TotalMilliseconds,
            preview,
            this.backend.DiagnosticLastFlushUsedGPU,
            this.backend.DiagnosticLastSceneFailure);
    }

    /// <summary>
    /// Gets the name of this backend.
    /// </summary>
    public override string ToString() => "WebGPU";

    /// <inheritdoc />
    public void Dispose()
    {
        this.ReleaseRenderTarget();
        this.backend.Dispose();

        if (this.device is not null)
        {
            this.api.DeviceRelease(this.device);
        }

        if (this.adapter is not null)
        {
            this.api.AdapterRelease(this.adapter);
        }

        if (this.instance is not null)
        {
            this.api.InstanceRelease(this.instance);
        }

        this.api.Dispose();
    }

    /// <summary>
    /// Creates the WebGPU instance, adapter, device, and queue used by the offscreen benchmark host.
    /// </summary>
    private string? InitializeDevice()
    {
        InstanceDescriptor instanceDescriptor = default;
        this.instance = this.api.CreateInstance(&instanceDescriptor);
        if (this.instance is null)
        {
            return "WebGPU instance creation failed.";
        }

        if (!TryRequestAdapter(this.api, this.instance, out this.adapter, out string? adapterError))
        {
            return adapterError;
        }

        if (!TryRequestDevice(this.api, this.adapter, out this.device, out string? deviceError))
        {
            return deviceError;
        }

        this.queue = this.api.DeviceGetQueue(this.device);
        if (this.queue is null)
        {
            return "WebGPU queue acquisition failed.";
        }

        return null;
    }

    /// <summary>
    /// Ensures the offscreen render target matches the current benchmark size.
    /// </summary>
    /// <remarks>
    /// The sample keeps a native texture for the real GPU render path and a CPU image only so the
    /// hybrid frame can satisfy the canvas abstraction while previews are read back from the texture.
    /// </remarks>
    private void EnsureRenderTarget(int width, int height)
    {
        if (this.frame is not null && this.textureWidth == width && this.textureHeight == height)
        {
            return;
        }

        this.ReleaseRenderTarget();

        TextureDescriptor descriptor = new()
        {
            Usage = TextureUsage.RenderAttachment | TextureUsage.CopySrc | TextureUsage.CopyDst | TextureUsage.TextureBinding | TextureUsage.StorageBinding,
            Dimension = TextureDimension.Dimension2D,
            Size = new Extent3D((uint)width, (uint)height, 1),
            Format = TextureFormat.Bgra8Unorm,
            MipLevelCount = 1,
            SampleCount = 1,
        };

        this.texture = this.api.DeviceCreateTexture(this.device, in descriptor);
        if (this.texture is null)
        {
            throw new InvalidOperationException("WebGPU texture allocation failed.");
        }

        TextureViewDescriptor viewDescriptor = new()
        {
            Format = TextureFormat.Bgra8Unorm,
            Dimension = TextureViewDimension.Dimension2D,
            BaseMipLevel = 0,
            MipLevelCount = 1,
            BaseArrayLayer = 0,
            ArrayLayerCount = 1,
            Aspect = TextureAspect.All,
        };

        this.textureView = this.api.TextureCreateView(this.texture, in viewDescriptor);
        if (this.textureView is null)
        {
            throw new InvalidOperationException("WebGPU texture view allocation failed.");
        }

        this.surface = WebGPUNativeSurfaceFactory.Create<Bgra32>(
            (nint)this.device,
            (nint)this.queue,
            (nint)this.texture,
            (nint)this.textureView,
            WebGPUTextureFormatId.Bgra8Unorm,
            width,
            height);
        this.cpuImage = new Image<Bgra32>(width, height);
        Buffer2DRegion<Bgra32> cpuRegion = new(this.cpuImage.Frames.RootFrame.PixelBuffer, this.cpuImage.Bounds);
        this.frame = new HybridCanvasFrame<Bgra32>(new Rectangle(0, 0, width, height), cpuRegion, this.surface);
        this.textureWidth = width;
        this.textureHeight = height;
    }

    /// <summary>
    /// Releases the current offscreen texture, view, surface, and CPU-side frame wrapper.
    /// </summary>
    private void ReleaseRenderTarget()
    {
        this.frame = null;
        this.surface = null;
        this.cpuImage?.Dispose();
        this.cpuImage = null;
        this.textureWidth = 0;
        this.textureHeight = 0;

        if (this.textureView is not null)
        {
            this.api.TextureViewRelease(this.textureView);
            this.textureView = null;
        }

        if (this.texture is not null)
        {
            this.api.TextureRelease(this.texture);
            this.texture = null;
        }
    }

    /// <summary>
    /// Throws when the host failed to initialize WebGPU.
    /// </summary>
    private void ThrowIfUnavailable()
    {
        if (this.device is null || this.queue is null)
        {
            throw new InvalidOperationException("WebGPU is unavailable.");
        }
    }

    /// <summary>
    /// Requests the high-performance adapter used by the offscreen benchmark host.
    /// </summary>
    private static bool TryRequestAdapter(WebGPU api, Instance* instance, out Adapter* adapter, out string? error)
    {
        RequestAdapterStatus callbackStatus = RequestAdapterStatus.Unknown;
        Adapter* callbackAdapter = null;
        using ManualResetEventSlim callbackReady = new(false);

        void Callback(RequestAdapterStatus status, Adapter* adapterPtr, byte* message, void* userData)
        {
            _ = message;
            _ = userData;
            callbackStatus = status;
            callbackAdapter = adapterPtr;
            callbackReady.Set();
        }

        using PfnRequestAdapterCallback callbackPtr = PfnRequestAdapterCallback.From(Callback);
        RequestAdapterOptions options = new()
        {
            PowerPreference = PowerPreference.HighPerformance,
        };

        api.InstanceRequestAdapter(instance, in options, callbackPtr, null);
        if (!callbackReady.Wait(10_000))
        {
            adapter = null;
            error = "Timed out while waiting for the WebGPU adapter request callback.";
            return false;
        }

        adapter = callbackAdapter;
        if (callbackStatus != RequestAdapterStatus.Success || callbackAdapter is null)
        {
            error = $"WebGPU adapter request failed with status '{callbackStatus}'.";
            return false;
        }

        error = null;
        return true;
    }

    /// <summary>
    /// Requests the logical device used by the offscreen benchmark host.
    /// </summary>
    private static bool TryRequestDevice(WebGPU api, Adapter* adapter, out Device* device, out string? error)
    {
        RequestDeviceStatus callbackStatus = RequestDeviceStatus.Unknown;
        Device* callbackDevice = null;
        using ManualResetEventSlim callbackReady = new(false);

        void Callback(RequestDeviceStatus status, Device* devicePtr, byte* message, void* userData)
        {
            _ = message;
            _ = userData;
            callbackStatus = status;
            callbackDevice = devicePtr;
            callbackReady.Set();
        }

        using PfnRequestDeviceCallback callbackPtr = PfnRequestDeviceCallback.From(Callback);

        Span<FeatureName> requestedFeatures = stackalloc FeatureName[1];
        int requestedCount = 0;
        if (api.AdapterHasFeature(adapter, FeatureName.Bgra8UnormStorage))
        {
            requestedFeatures[requestedCount++] = FeatureName.Bgra8UnormStorage;
        }

        DeviceDescriptor descriptor;
        if (requestedCount > 0)
        {
            fixed (FeatureName* featuresPtr = requestedFeatures)
            {
                descriptor = new DeviceDescriptor
                {
                    RequiredFeatureCount = (uint)requestedCount,
                    RequiredFeatures = featuresPtr,
                };

                api.AdapterRequestDevice(adapter, in descriptor, callbackPtr, null);
            }
        }
        else
        {
            descriptor = default;
            api.AdapterRequestDevice(adapter, in descriptor, callbackPtr, null);
        }

        if (!callbackReady.Wait(10_000))
        {
            device = null;
            error = "Timed out while waiting for the WebGPU device request callback.";
            return false;
        }

        device = callbackDevice;
        if (callbackStatus != RequestDeviceStatus.Success || callbackDevice is null)
        {
            error = $"WebGPU device request failed with status '{callbackStatus}'.";
            return false;
        }

        error = null;
        return true;
    }
}
