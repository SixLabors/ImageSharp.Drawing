// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Buffers;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Silk.NET.WebGPU;
using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.PixelFormats;
using WgpuBuffer = Silk.NET.WebGPU.Buffer;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Blend mode selection for render-pipeline-based composition passes.
/// </summary>
internal enum CompositePipelineBlendMode
{
    /// <summary>
    /// Uses default blending behavior for the render pipeline variant.
    /// </summary>
    None = 0
}

/// <summary>
/// Per-flush WebGPU execution context created from a single frame target.
/// </summary>
internal sealed unsafe class WebGPUFlushContext : IDisposable
{
    private static readonly ConcurrentDictionary<Type, IDisposable> FallbackStagingCache = new();
    private static readonly ConcurrentDictionary<nint, DeviceSharedState> DeviceStateCache = new();
    private static readonly object SharedHandleSync = new();
    private const int CallbackTimeoutMilliseconds = 10_000;

    private bool disposed;
    private bool ownsTargetTexture;
    private bool ownsTargetView;
    private bool ownsReadbackBuffer;
    private DeviceSharedState.CpuTargetLease? cpuTargetLease;
    private readonly List<nint> transientBindGroups = [];
    private readonly List<nint> transientBuffers = [];
    private readonly List<nint> transientTextureViews = [];
    private readonly List<nint> transientTextures = [];

    // Flush-scoped source image cache:
    // key = source Image reference, value = uploaded texture view handle.
    // Handles are released when this flush context is disposed.
    private readonly Dictionary<Image, nint> cachedSourceTextureViews = new(ReferenceEqualityComparer.Instance);

    private WebGPUFlushContext(
        WebGPURuntime.Lease runtimeLease,
        Device* device,
        Queue* queue,
        in Rectangle targetBounds,
        TextureFormat textureFormat,
        MemoryAllocator memoryAllocator,
        DeviceSharedState deviceState)
    {
        this.RuntimeLease = runtimeLease;
        this.Api = runtimeLease.Api;
        this.Device = device;
        this.Queue = queue;
        this.TargetBounds = targetBounds;
        this.TextureFormat = textureFormat;
        this.MemoryAllocator = memoryAllocator;
        this.DeviceState = deviceState;
    }

    /// <summary>
    /// Gets the runtime lease that keeps the process-level WebGPU API alive.
    /// </summary>
    public WebGPURuntime.Lease RuntimeLease { get; }

    /// <summary>
    /// Gets the WebGPU API facade for this flush.
    /// </summary>
    public WebGPU Api { get; }

    /// <summary>
    /// Gets the device used to create and execute GPU resources.
    /// </summary>
    public Device* Device { get; }

    /// <summary>
    /// Gets the queue used to submit GPU work.
    /// </summary>
    public Queue* Queue { get; }

    /// <summary>
    /// Gets the target bounds for this flush context.
    /// </summary>
    public Rectangle TargetBounds { get; }

    /// <summary>
    /// Gets the target texture format for this flush.
    /// </summary>
    public TextureFormat TextureFormat { get; }

    /// <summary>
    /// Gets the allocator used for temporary CPU staging buffers in this flush context.
    /// </summary>
    public MemoryAllocator MemoryAllocator { get; }

    /// <summary>
    /// Gets device-scoped shared caches and reusable resources.
    /// </summary>
    public DeviceSharedState DeviceState { get; }

    /// <summary>
    /// Gets the target texture receiving render/composite output.
    /// </summary>
    public Texture* TargetTexture { get; private set; }

    /// <summary>
    /// Gets the texture view used when binding the target texture.
    /// </summary>
    public TextureView* TargetView { get; private set; }

    /// <summary>
    /// Gets a value indicating whether CPU readback is required after GPU execution.
    /// </summary>
    public bool RequiresReadback { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the current target texture can be sampled in a compute shader.
    /// </summary>
    public bool CanSampleTargetTexture { get; private set; }

    /// <summary>
    /// Gets the readback buffer used when CPU readback is required.
    /// </summary>
    public WgpuBuffer* ReadbackBuffer { get; private set; }

    /// <summary>
    /// Gets the readback row stride in bytes.
    /// </summary>
    public uint ReadbackBytesPerRow { get; private set; }

    /// <summary>
    /// Gets the readback buffer byte size.
    /// </summary>
    public ulong ReadbackByteCount { get; private set; }

    /// <summary>
    /// Gets the shared instance-data buffer used for parameter uploads.
    /// </summary>
    public WgpuBuffer* InstanceBuffer { get; private set; }

    /// <summary>
    /// Gets the instance buffer capacity in bytes.
    /// </summary>
    public nuint InstanceBufferCapacity { get; private set; }

    /// <summary>
    /// Gets or sets the current write offset into <see cref="InstanceBuffer"/>.
    /// </summary>
    public nuint InstanceBufferWriteOffset { get; internal set; }

    /// <summary>
    /// Gets or sets the active command encoder.
    /// </summary>
    public CommandEncoder* CommandEncoder { get; set; }

    /// <summary>
    /// Gets the currently open render pass encoder, if any.
    /// </summary>
    public RenderPassEncoder* PassEncoder { get; private set; }

    /// <summary>
    /// Creates a flush context for either a native WebGPU surface or a CPU-backed frame.
    /// </summary>
    public static WebGPUFlushContext Create<TPixel>(
        ICanvasFrame<TPixel> frame,
        TextureFormat expectedTextureFormat,
        int pixelSizeInBytes,
        MemoryAllocator memoryAllocator,
        Rectangle? initialUploadBounds = null)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        WebGPUSurfaceCapability? nativeCapability = TryGetNativeSurfaceCapability(frame, expectedTextureFormat);
        WebGPURuntime.Lease lease = WebGPURuntime.Acquire();
        try
        {
            Device* device;
            Queue* queue;
            TextureFormat textureFormat;
            Rectangle bounds = frame.Bounds;
            DeviceSharedState deviceState;
            WebGPUFlushContext context;

            if (nativeCapability is not null)
            {
                device = (Device*)nativeCapability.Device;
                queue = (Queue*)nativeCapability.Queue;
                textureFormat = WebGPUTextureFormatMapper.ToSilk(nativeCapability.TargetFormat);
                bounds = new Rectangle(0, 0, nativeCapability.Width, nativeCapability.Height);
                deviceState = GetOrCreateDeviceState(lease.Api, device);
                context = new WebGPUFlushContext(lease, device, queue, in bounds, textureFormat, memoryAllocator, deviceState);
                context.InitializeNativeTarget(nativeCapability);
                return context;
            }

            if (!frame.TryGetCpuRegion(out Buffer2DRegion<TPixel> cpuRegion))
            {
                throw new NotSupportedException("Frame does not expose a GPU-native surface or CPU region.");
            }

            if (!TryGetOrCreateSharedHandles(lease.Api, out device, out queue, out string? error))
            {
                throw new InvalidOperationException(error ?? "WebGPU shared handles are unavailable.");
            }

            deviceState = GetOrCreateDeviceState(lease.Api, device);
            context = new WebGPUFlushContext(lease, device, queue, in bounds, expectedTextureFormat, memoryAllocator, deviceState);
            context.InitializeCpuTarget(cpuRegion, pixelSizeInBytes, initialUploadBounds);
            return context;
        }
        catch
        {
            lease.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Creates a flush context intended for fallback upload into a writable native surface.
    /// </summary>
    public static WebGPUFlushContext CreateUploadContext<TPixel>(ICanvasFrame<TPixel> frame, MemoryAllocator memoryAllocator)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        WebGPUSurfaceCapability? nativeCapability =
            TryGetWritableNativeSurfaceCapability(frame)
            ?? throw new NotSupportedException("Fallback upload requires a native WebGPU surface exposing writable device, queue, and texture handles.");

        WebGPURuntime.Lease lease = WebGPURuntime.Acquire();
        try
        {
            Rectangle bounds = new(0, 0, nativeCapability.Width, nativeCapability.Height);
            TextureFormat textureFormat = WebGPUTextureFormatMapper.ToSilk(nativeCapability.TargetFormat);
            Device* device = (Device*)nativeCapability.Device;
            DeviceSharedState deviceState = GetOrCreateDeviceState(lease.Api, device);
            WebGPUFlushContext context = new(
                lease,
                device,
                (Queue*)nativeCapability.Queue,
                in bounds,
                textureFormat,
                memoryAllocator,
                deviceState);
            context.InitializeNativeTarget(nativeCapability);
            return context;
        }
        catch
        {
            lease.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Rents a CPU fallback staging buffer for the specified pixel type and bounds.
    /// </summary>
    public static FallbackStagingLease<TPixel> RentFallbackStaging<TPixel>(MemoryAllocator allocator, in Rectangle bounds)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        IDisposable entry = FallbackStagingCache.GetOrAdd(
            typeof(TPixel),
            static _ => new FallbackStagingEntry<TPixel>());

        return ((FallbackStagingEntry<TPixel>)entry).Rent(allocator, in bounds);
    }

    /// <summary>
    /// Clears all cached CPU fallback staging buffers.
    /// </summary>
    public static void ClearFallbackStagingCache()
    {
        foreach (IDisposable entry in FallbackStagingCache.Values)
        {
            entry.Dispose();
        }

        FallbackStagingCache.Clear();
    }

    /// <summary>
    /// Clears all cached device-scoped shared state.
    /// </summary>
    public static void ClearDeviceStateCache()
    {
        foreach (DeviceSharedState state in DeviceStateCache.Values)
        {
            state.Dispose();
        }

        DeviceStateCache.Clear();
    }

    /// <summary>
    /// Tries to get shared native interop handles for the active WebGPU device and queue.
    /// </summary>
    /// <param name="deviceHandle">When this method returns <see langword="true"/>, contains the native device handle.</param>
    /// <param name="queueHandle">When this method returns <see langword="true"/>, contains the native queue handle.</param>
    /// <param name="error">When this method returns <see langword="false"/>, contains an error message.</param>
    /// <returns><see langword="true"/> if shared handles are available; otherwise <see langword="false"/>.</returns>
    public static bool TryGetInteropHandles(out nint deviceHandle, out nint queueHandle, out string? error)
    {
        if (WebGPURuntime.TryGetSharedHandles(out Device* sharedDevice, out Queue* sharedQueue))
        {
            deviceHandle = (nint)sharedDevice;
            queueHandle = (nint)sharedQueue;
            error = null;
            return true;
        }

        using WebGPURuntime.Lease lease = WebGPURuntime.Acquire();
        if (TryGetOrCreateSharedHandles(lease.Api, out Device* device, out Queue* queue, out error))
        {
            deviceHandle = (nint)device;
            queueHandle = (nint)queue;
            return true;
        }

        deviceHandle = 0;
        queueHandle = 0;
        return false;
    }

    /// <summary>
    /// Ensures that the instance buffer exists and can hold at least the requested number of bytes.
    /// </summary>
    /// <param name="requiredBytes">The required number of bytes for the current flush.</param>
    /// <param name="minimumCapacityBytes">The minimum allocation size to enforce when creating a new buffer.</param>
    /// <returns><see langword="true"/> if the buffer is available with sufficient capacity; otherwise <see langword="false"/>.</returns>
    public bool EnsureInstanceBufferCapacity(nuint requiredBytes, nuint minimumCapacityBytes)
    {
        if (this.InstanceBuffer is not null && this.InstanceBufferCapacity >= requiredBytes)
        {
            return true;
        }

        if (this.InstanceBuffer is not null)
        {
            this.Api.BufferRelease(this.InstanceBuffer);
            this.InstanceBuffer = null;
            this.InstanceBufferCapacity = 0;
        }

        nuint targetSize = requiredBytes > minimumCapacityBytes ? requiredBytes : minimumCapacityBytes;
        BufferDescriptor descriptor = new()
        {
            Usage = BufferUsage.Storage | BufferUsage.CopyDst,
            Size = targetSize
        };

        this.InstanceBuffer = this.Api.DeviceCreateBuffer(this.Device, in descriptor);
        if (this.InstanceBuffer is null)
        {
            return false;
        }

        this.InstanceBufferCapacity = targetSize;
        return true;
    }

    /// <summary>
    /// Ensures that a command encoder is available for recording GPU commands.
    /// </summary>
    /// <returns><see langword="true"/> if an encoder is available; otherwise <see langword="false"/>.</returns>
    public bool EnsureCommandEncoder()
    {
        if (this.CommandEncoder is not null)
        {
            return true;
        }

        CommandEncoderDescriptor descriptor = default;
        this.CommandEncoder = this.Api.DeviceCreateCommandEncoder(this.Device, in descriptor);
        return this.CommandEncoder is not null;
    }

    /// <summary>
    /// Begins a render pass that targets the specified texture view.
    /// </summary>
    public bool BeginRenderPass(TextureView* targetView)
        => this.BeginRenderPass(targetView, loadExisting: false);

    /// <summary>
    /// Begins a render pass that targets the specified texture view, optionally preserving existing contents.
    /// </summary>
    public bool BeginRenderPass(TextureView* targetView, bool loadExisting)
    {
        if (this.PassEncoder is not null)
        {
            return true;
        }

        if (this.CommandEncoder is null || targetView is null)
        {
            return false;
        }

        RenderPassColorAttachment colorAttachment = new()
        {
            View = targetView,
            ResolveTarget = null,
            LoadOp = loadExisting ? LoadOp.Load : LoadOp.Clear,
            StoreOp = StoreOp.Store,
            ClearValue = default
        };

        RenderPassDescriptor renderPassDescriptor = new()
        {
            ColorAttachmentCount = 1,
            ColorAttachments = &colorAttachment
        };

        this.PassEncoder = this.Api.CommandEncoderBeginRenderPass(this.CommandEncoder, in renderPassDescriptor);
        return this.PassEncoder is not null;
    }

    /// <summary>
    /// Ends and releases the current render pass if one is active.
    /// </summary>
    public void EndRenderPassIfOpen()
    {
        if (this.PassEncoder is null)
        {
            return;
        }

        this.Api.RenderPassEncoderEnd(this.PassEncoder);
        this.Api.RenderPassEncoderRelease(this.PassEncoder);
        this.PassEncoder = null;
    }

    /// <summary>
    /// Tracks a transient bind group allocated during this flush.
    /// </summary>
    /// <param name="bindGroup">The bind group to track.</param>
    public void TrackBindGroup(BindGroup* bindGroup)
    {
        if (bindGroup is not null)
        {
            this.transientBindGroups.Add((nint)bindGroup);
        }
    }

    /// <summary>
    /// Tracks a transient buffer allocated during this flush.
    /// </summary>
    public void TrackBuffer(WgpuBuffer* buffer)
    {
        if (buffer is not null)
        {
            this.transientBuffers.Add((nint)buffer);
        }
    }

    /// <summary>
    /// Tracks a transient texture view allocated during this flush.
    /// </summary>
    public void TrackTextureView(TextureView* textureView)
    {
        if (textureView is not null)
        {
            this.transientTextureViews.Add((nint)textureView);
        }
    }

    /// <summary>
    /// Tracks a transient texture allocated during this flush.
    /// </summary>
    public void TrackTexture(Texture* texture)
    {
        if (texture is not null)
        {
            this.transientTextures.Add((nint)texture);
        }
    }

    /// <summary>
    /// Tries to resolve a cached source texture view for an input image.
    /// </summary>
    /// <param name="image">The source image key.</param>
    /// <param name="textureView">When this method returns <see langword="true"/>, contains the cached texture view.</param>
    /// <returns><see langword="true"/> if a cached texture view exists; otherwise <see langword="false"/>.</returns>
    public bool TryGetCachedSourceTextureView(Image image, out TextureView* textureView)
    {
        if (this.cachedSourceTextureViews.TryGetValue(image, out nint handle) && handle != 0)
        {
            textureView = (TextureView*)handle;
            return true;
        }

        textureView = null;
        return false;
    }

    /// <summary>
    /// Caches a source texture view for reuse within the flush.
    /// </summary>
    /// <param name="image">The source image key.</param>
    /// <param name="textureView">The texture view to cache.</param>
    public void CacheSourceTextureView(Image image, TextureView* textureView)
        => this.cachedSourceTextureViews[image] = (nint)textureView;

    /// <summary>
    /// Releases transient GPU resources owned by this flush context.
    /// </summary>
    public void Dispose()
    {
        if (this.disposed)
        {
            return;
        }

        this.EndRenderPassIfOpen();

        if (this.CommandEncoder is not null)
        {
            this.Api.CommandEncoderRelease(this.CommandEncoder);
            this.CommandEncoder = null;
        }

        if (this.InstanceBuffer is not null)
        {
            this.Api.BufferRelease(this.InstanceBuffer);
            this.InstanceBuffer = null;
            this.InstanceBufferCapacity = 0;
        }

        this.InstanceBufferWriteOffset = 0;

        this.cpuTargetLease?.Dispose();
        this.cpuTargetLease = null;

        if (this.ownsReadbackBuffer && this.ReadbackBuffer is not null)
        {
            this.Api.BufferRelease(this.ReadbackBuffer);
        }

        if (this.ownsTargetView && this.TargetView is not null)
        {
            this.Api.TextureViewRelease(this.TargetView);
        }

        if (this.ownsTargetTexture && this.TargetTexture is not null)
        {
            this.Api.TextureRelease(this.TargetTexture);
        }

        for (int i = 0; i < this.transientBindGroups.Count; i++)
        {
            this.Api.BindGroupRelease((BindGroup*)this.transientBindGroups[i]);
        }

        for (int i = 0; i < this.transientBuffers.Count; i++)
        {
            this.Api.BufferRelease((WgpuBuffer*)this.transientBuffers[i]);
        }

        for (int i = 0; i < this.transientTextureViews.Count; i++)
        {
            this.Api.TextureViewRelease((TextureView*)this.transientTextureViews[i]);
        }

        for (int i = 0; i < this.transientTextures.Count; i++)
        {
            this.Api.TextureRelease((Texture*)this.transientTextures[i]);
        }

        this.transientBindGroups.Clear();
        this.transientBuffers.Clear();
        this.transientTextureViews.Clear();
        this.transientTextures.Clear();

        // Cache entries point to transient texture views that are released above.
        this.cachedSourceTextureViews.Clear();

        this.ReadbackBuffer = null;
        this.TargetView = null;
        this.TargetTexture = null;
        this.ReadbackBytesPerRow = 0;
        this.ReadbackByteCount = 0;
        this.RequiresReadback = false;
        this.CanSampleTargetTexture = false;
        this.ownsReadbackBuffer = false;
        this.ownsTargetView = false;
        this.ownsTargetTexture = false;

        this.RuntimeLease.Dispose();
        this.disposed = true;
    }

    private static DeviceSharedState GetOrCreateDeviceState(WebGPU api, Device* device)
    {
        nint cacheKey = (nint)device;
        if (DeviceStateCache.TryGetValue(cacheKey, out DeviceSharedState? existing))
        {
            return existing;
        }

        DeviceSharedState created = new(api, device);
        DeviceSharedState winner = DeviceStateCache.GetOrAdd(cacheKey, created);
        if (!ReferenceEquals(winner, created))
        {
            created.Dispose();
        }

        return winner;
    }

    private static bool TryGetOrCreateSharedHandles(
        WebGPU api,
        out Device* device,
        out Queue* queue,
        out string? error)
    {
        if (WebGPURuntime.TryGetSharedHandles(out device, out queue))
        {
            error = null;
            return true;
        }

        lock (SharedHandleSync)
        {
            if (WebGPURuntime.TryGetSharedHandles(out device, out queue))
            {
                error = null;
                return true;
            }

            Instance* instance = api.CreateInstance((InstanceDescriptor*)null);
            if (instance is null)
            {
                error = "WebGPU.CreateInstance returned null.";
                device = null;
                queue = null;
                return false;
            }

            Adapter* adapter = null;
            Device* requestedDevice = null;
            Queue* requestedQueue = null;
            bool initialized = false;
            try
            {
                if (!TryRequestAdapter(api, instance, out adapter, out error))
                {
                    device = null;
                    queue = null;
                    return false;
                }

                if (!TryRequestDevice(api, adapter, out requestedDevice, out error))
                {
                    device = null;
                    queue = null;
                    return false;
                }

                requestedQueue = api.DeviceGetQueue(requestedDevice);
                if (requestedQueue is null)
                {
                    error = "WebGPU.DeviceGetQueue returned null.";
                    device = null;
                    queue = null;
                    return false;
                }

                WebGPURuntime.SetSharedHandles((nint)requestedDevice, (nint)requestedQueue);
                device = requestedDevice;
                queue = requestedQueue;
                error = null;
                initialized = true;
                return true;
            }
            finally
            {
                if (adapter is not null)
                {
                    api.AdapterRelease(adapter);
                }

                api.InstanceRelease(instance);

                if (!initialized)
                {
                    if (requestedQueue is not null)
                    {
                        api.QueueRelease(requestedQueue);
                    }

                    if (requestedDevice is not null)
                    {
                        api.DeviceRelease(requestedDevice);
                    }
                }
            }
        }
    }

    private static bool TryRequestAdapter(WebGPU api, Instance* instance, out Adapter* adapter, out string? error)
    {
        RequestAdapterStatus callbackStatus = RequestAdapterStatus.Unknown;
        Adapter* callbackAdapter = null;
        using ManualResetEventSlim callbackReady = new(false);
        void Callback(RequestAdapterStatus status, Adapter* adapterPtr, byte* message, void* userData)
        {
            callbackStatus = status;
            callbackAdapter = adapterPtr;
            callbackReady.Set();
        }

        using PfnRequestAdapterCallback callbackPtr = PfnRequestAdapterCallback.From(Callback);
        RequestAdapterOptions options = new()
        {
            PowerPreference = PowerPreference.HighPerformance
        };

        api.InstanceRequestAdapter(instance, in options, callbackPtr, null);
        if (!WaitForSignal(callbackReady))
        {
            adapter = null;
            error = "Timed out while waiting for WebGPU adapter request callback.";
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

    private static bool TryRequestDevice(WebGPU api, Adapter* adapter, out Device* device, out string? error)
    {
        RequestDeviceStatus callbackStatus = RequestDeviceStatus.Unknown;
        Device* callbackDevice = null;
        using ManualResetEventSlim callbackReady = new(false);
        void Callback(RequestDeviceStatus status, Device* devicePtr, byte* message, void* userData)
        {
            callbackStatus = status;
            callbackDevice = devicePtr;
            callbackReady.Set();
        }

        using PfnRequestDeviceCallback callbackPtr = PfnRequestDeviceCallback.From(Callback);
        DeviceDescriptor descriptor = default;
        api.AdapterRequestDevice(adapter, in descriptor, callbackPtr, null);
        if (!WaitForSignal(callbackReady))
        {
            device = null;
            error = "Timed out while waiting for WebGPU device request callback.";
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool WaitForSignal(ManualResetEventSlim signal)
        => signal.Wait(CallbackTimeoutMilliseconds);

    private void InitializeNativeTarget(WebGPUSurfaceCapability capability)
    {
        this.TargetTexture = (Texture*)capability.TargetTexture;
        this.TargetView = (TextureView*)capability.TargetTextureView;
        this.RequiresReadback = false;
        this.CanSampleTargetTexture = capability.SupportsTextureSampling;
        this.ReadbackBuffer = null;
        this.ReadbackBytesPerRow = 0;
        this.ReadbackByteCount = 0;
        this.ownsTargetTexture = false;
        this.ownsTargetView = false;
        this.ownsReadbackBuffer = false;
    }

    private void InitializeCpuTarget<TPixel>(
        Buffer2DRegion<TPixel> cpuRegion,
        int pixelSizeInBytes,
        Rectangle? initialUploadBounds)
        where TPixel : unmanaged
    {
        int width = cpuRegion.Width;
        int height = cpuRegion.Height;
        DeviceSharedState.CpuTargetLease lease = this.DeviceState.RentCpuTarget(
            this.TextureFormat,
            width,
            height,
            pixelSizeInBytes);
        Texture* targetTexture = lease.TargetTexture;
        TextureView* targetView = lease.TargetView;
        WgpuBuffer* readbackBuffer = lease.ReadbackBuffer;
        uint readbackRowBytes = lease.ReadbackBytesPerRow;
        ulong readbackByteCount = lease.ReadbackByteCount;

        try
        {
            if (initialUploadBounds is Rectangle uploadBounds &&
                uploadBounds.Width > 0 &&
                uploadBounds.Height > 0)
            {
                Buffer2DRegion<TPixel> uploadRegion = cpuRegion.GetSubRegion(uploadBounds);
                UploadTextureFromRegion(this.Api, this.Queue, targetTexture, uploadRegion, this.MemoryAllocator, (uint)uploadBounds.X, (uint)uploadBounds.Y, 0);
            }
            else
            {
                UploadTextureFromRegion(this.Api, this.Queue, targetTexture, cpuRegion, this.MemoryAllocator);
            }
        }
        catch
        {
            lease.Dispose();
            throw;
        }

        this.cpuTargetLease = lease;
        this.TargetTexture = targetTexture;
        this.TargetView = targetView;
        this.ReadbackBuffer = readbackBuffer;
        this.ReadbackBytesPerRow = readbackRowBytes;
        this.ReadbackByteCount = readbackByteCount;
        this.RequiresReadback = true;
        this.CanSampleTargetTexture = true;
        this.ownsTargetTexture = false;
        this.ownsTargetView = false;
        this.ownsReadbackBuffer = false;
    }

    private static WebGPUSurfaceCapability? TryGetNativeSurfaceCapability<TPixel>(ICanvasFrame<TPixel> frame, TextureFormat expectedTextureFormat)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        if (!frame.TryGetNativeSurface(out NativeSurface? nativeSurface) ||
            !nativeSurface.TryGetCapability(out WebGPUSurfaceCapability? capability))
        {
            return null;
        }

        if (capability.Device == 0 ||
            capability.Queue == 0 ||
            capability.TargetTextureView == 0 ||
            WebGPUTextureFormatMapper.ToSilk(capability.TargetFormat) != expectedTextureFormat)
        {
            return null;
        }

        Rectangle bounds = frame.Bounds;
        if (bounds.X < 0 ||
            bounds.Y < 0 ||
            bounds.Right > capability.Width ||
            bounds.Bottom > capability.Height)
        {
            return null;
        }

        return capability;
    }

    private static WebGPUSurfaceCapability? TryGetWritableNativeSurfaceCapability<TPixel>(ICanvasFrame<TPixel> frame)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        if (!frame.TryGetNativeSurface(out NativeSurface? nativeSurface) ||
            !nativeSurface.TryGetCapability(out WebGPUSurfaceCapability? capability))
        {
            return null;
        }

        if (capability.Device == 0 || capability.Queue == 0 || capability.TargetTexture == 0)
        {
            return null;
        }

        Rectangle bounds = frame.Bounds;
        if (bounds.X < 0 ||
            bounds.Y < 0 ||
            bounds.Right > capability.Width ||
            bounds.Bottom > capability.Height)
        {
            return null;
        }

        return capability;
    }

    internal static void UploadTextureFromRegion<TPixel>(
        WebGPU api,
        Queue* queue,
        Texture* destinationTexture,
        Buffer2DRegion<TPixel> sourceRegion,
        MemoryAllocator memoryAllocator)
        where TPixel : unmanaged
        => UploadTextureFromRegion(api, queue, destinationTexture, sourceRegion, memoryAllocator, 0, 0, 0);

    internal static void UploadTextureFromRegion<TPixel>(
        WebGPU api,
        Queue* queue,
        Texture* destinationTexture,
        Buffer2DRegion<TPixel> sourceRegion,
        MemoryAllocator memoryAllocator,
        uint destinationX,
        uint destinationY,
        uint destinationLayer)
        where TPixel : unmanaged
    {
        int pixelSizeInBytes = Unsafe.SizeOf<TPixel>();
        ImageCopyTexture destination = new()
        {
            Texture = destinationTexture,
            MipLevel = 0,
            Origin = new Origin3D(destinationX, destinationY, destinationLayer),
            Aspect = TextureAspect.All
        };

        Extent3D writeSize = new((uint)sourceRegion.Width, (uint)sourceRegion.Height, 1);
        int rowBytes = checked(sourceRegion.Width * pixelSizeInBytes);
        uint alignedRowBytes = AlignTo256((uint)rowBytes);

        if (sourceRegion.Buffer.MemoryGroup.Count == 1)
        {
            int sourceStrideBytes = checked(sourceRegion.Buffer.Width * pixelSizeInBytes);
            long directByteCount = ((long)sourceStrideBytes * (sourceRegion.Height - 1)) + rowBytes;
            long packedByteCountEstimate = (long)alignedRowBytes * sourceRegion.Height;

            // Only use the direct path when the stride satisfies WebGPU's alignment requirement.
            if ((uint)sourceStrideBytes == alignedRowBytes && directByteCount <= packedByteCountEstimate * 2)
            {
                int startPixelIndex = checked((sourceRegion.Rectangle.Y * sourceRegion.Buffer.Width) + sourceRegion.Rectangle.X);
                int startByteOffset = checked(startPixelIndex * pixelSizeInBytes);
                int uploadByteCount = checked((int)directByteCount);
                nuint uploadByteCountNuint = checked((nuint)uploadByteCount);

                TextureDataLayout layout = new()
                {
                    Offset = 0,
                    BytesPerRow = (uint)sourceStrideBytes,
                    RowsPerImage = (uint)sourceRegion.Height
                };

                Memory<TPixel> sourceMemory = sourceRegion.Buffer.MemoryGroup[0];
                Span<byte> sourceBytes = MemoryMarshal.AsBytes(sourceMemory.Span).Slice(startByteOffset, uploadByteCount);
                fixed (byte* uploadPtr = sourceBytes)
                {
                    api.QueueWriteTexture(queue, in destination, uploadPtr, uploadByteCountNuint, in layout, in writeSize);
                }

                return;
            }
        }

        int alignedRowBytesInt = checked((int)alignedRowBytes);
        int packedByteCount = checked(alignedRowBytesInt * sourceRegion.Height);
        using IMemoryOwner<byte> packedOwner = memoryAllocator.Allocate<byte>(packedByteCount);
        Span<byte> packedData = packedOwner.Memory.Span[..packedByteCount];
        packedData.Clear();
        for (int y = 0; y < sourceRegion.Height; y++)
        {
            ReadOnlySpan<TPixel> sourceRow = sourceRegion.DangerousGetRowSpan(y);
            MemoryMarshal.AsBytes(sourceRow).Slice(0, rowBytes).CopyTo(packedData.Slice(y * alignedRowBytesInt, rowBytes));
        }

        TextureDataLayout packedLayout = new()
        {
            Offset = 0,
            BytesPerRow = alignedRowBytes,
            RowsPerImage = (uint)sourceRegion.Height
        };

        fixed (byte* uploadPtr = packedData)
        {
            api.QueueWriteTexture(queue, in destination, uploadPtr, (nuint)packedByteCount, in packedLayout, in writeSize);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint AlignTo256(uint value) => (value + 255U) & ~255U;

    /// <summary>
    /// Shared device-scoped caches for pipelines, bind groups, and reusable GPU resources.
    /// </summary>
    internal sealed class DeviceSharedState : IDisposable
    {
        private readonly ConcurrentDictionary<CpuTargetCacheKey, CpuTargetEntry> cpuTargetCache = new();
        private readonly ConcurrentDictionary<string, CompositePipelineInfrastructure> compositePipelines = new(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, CompositeComputePipelineInfrastructure> compositeComputePipelines = new(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, SharedBufferInfrastructure> sharedBuffers = new(StringComparer.Ordinal);
        private bool disposed;

        internal DeviceSharedState(WebGPU api, Device* device)
        {
            this.Api = api;
            this.Device = device;
        }

        private static ReadOnlySpan<byte> CompositeVertexEntryPoint => "vs_main\0"u8;

        private static ReadOnlySpan<byte> CompositeFragmentEntryPoint => "fs_main\0"u8;

        private static ReadOnlySpan<byte> CompositeComputeEntryPoint => "cs_main\0"u8;

        /// <summary>
        /// Gets the synchronization object used for shared state mutation.
        /// </summary>
        public object SyncRoot { get; } = new();

        /// <summary>
        /// Gets the WebGPU API instance used by this shared state.
        /// </summary>
        public WebGPU Api { get; }

        /// <summary>
        /// Gets the device associated with this shared state.
        /// </summary>
        public Device* Device { get; }

        /// <summary>
        /// Rents CPU-target staging resources for a destination texture shape and format.
        /// </summary>
        /// <param name="textureFormat">The destination texture format.</param>
        /// <param name="width">The destination width.</param>
        /// <param name="height">The destination height.</param>
        /// <param name="pixelSizeInBytes">The destination pixel size in bytes.</param>
        /// <returns>A lease for staging resources.</returns>
        public CpuTargetLease RentCpuTarget(
            TextureFormat textureFormat,
            int width,
            int height,
            int pixelSizeInBytes)
        {
            CpuTargetCacheKey key = new(textureFormat, width, height, pixelSizeInBytes);
            CpuTargetEntry entry = this.cpuTargetCache.GetOrAdd(key, static _ => new CpuTargetEntry());
            return entry.Rent(this.Api, this.Device, in key);
        }

        /// <summary>
        /// Gets or creates a graphics pipeline used for composite rendering.
        /// </summary>
        public bool TryGetOrCreateCompositePipeline(
            string pipelineKey,
            ReadOnlySpan<byte> shaderCode,
            WebGPUCompositeBindGroupLayoutFactory bindGroupLayoutFactory,
            TextureFormat textureFormat,
            CompositePipelineBlendMode blendMode,
            out BindGroupLayout* bindGroupLayout,
            out RenderPipeline* pipeline,
            out string? error)
        {
            bindGroupLayout = null;
            pipeline = null;

            if (this.disposed)
            {
                error = "WebGPU device state is disposed.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(pipelineKey))
            {
                error = "Composite pipeline key cannot be empty.";
                return false;
            }

            if (shaderCode.IsEmpty)
            {
                error = $"Composite shader code is missing for pipeline '{pipelineKey}'.";
                return false;
            }

            CompositePipelineInfrastructure infrastructure = this.compositePipelines.GetOrAdd(
                pipelineKey,
                static _ => new CompositePipelineInfrastructure());

            lock (infrastructure)
            {
                if (infrastructure.BindGroupLayout is null ||
                    infrastructure.PipelineLayout is null ||
                    infrastructure.ShaderModule is null)
                {
                    if (!this.TryCreateCompositeInfrastructure(
                            shaderCode,
                            bindGroupLayoutFactory,
                            out BindGroupLayout* createdBindGroupLayout,
                            out PipelineLayout* createdPipelineLayout,
                            out ShaderModule* createdShaderModule,
                            out error))
                    {
                        return false;
                    }

                    infrastructure.BindGroupLayout = createdBindGroupLayout;
                    infrastructure.PipelineLayout = createdPipelineLayout;
                    infrastructure.ShaderModule = createdShaderModule;
                }

                bindGroupLayout = infrastructure.BindGroupLayout;
                (TextureFormat TextureFormat, CompositePipelineBlendMode BlendMode) variantKey = (textureFormat, blendMode);
                if (infrastructure.Pipelines.TryGetValue(variantKey, out nint cachedPipelineHandle) && cachedPipelineHandle != 0)
                {
                    pipeline = (RenderPipeline*)cachedPipelineHandle;
                    error = null;
                    return true;
                }

                RenderPipeline* createdPipeline = this.CreateCompositePipeline(
                    infrastructure.PipelineLayout,
                    infrastructure.ShaderModule,
                    textureFormat,
                    blendMode);
                if (createdPipeline is null)
                {
                    error = $"Failed to create composite pipeline '{pipelineKey}' for format '{textureFormat}'.";
                    return false;
                }

                infrastructure.Pipelines[variantKey] = (nint)createdPipeline;
                pipeline = createdPipeline;
                error = null;
                return true;
            }
        }

        /// <summary>
        /// Gets or creates a compute pipeline used for composite execution.
        /// </summary>
        public bool TryGetOrCreateCompositeComputePipeline(
            string pipelineKey,
            ReadOnlySpan<byte> shaderCode,
            WebGPUCompositeBindGroupLayoutFactory bindGroupLayoutFactory,
            out BindGroupLayout* bindGroupLayout,
            out ComputePipeline* pipeline,
            out string? error)
        {
            bindGroupLayout = null;
            pipeline = null;

            if (this.disposed)
            {
                error = "WebGPU device state is disposed.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(pipelineKey))
            {
                error = "Composite compute pipeline key cannot be empty.";
                return false;
            }

            if (shaderCode.IsEmpty)
            {
                error = $"Composite compute shader code is missing for pipeline '{pipelineKey}'.";
                return false;
            }

            CompositeComputePipelineInfrastructure infrastructure = this.compositeComputePipelines.GetOrAdd(
                pipelineKey,
                static _ => new CompositeComputePipelineInfrastructure());

            lock (infrastructure)
            {
                if (infrastructure.BindGroupLayout is null ||
                    infrastructure.PipelineLayout is null ||
                    infrastructure.ShaderModule is null)
                {
                    if (!this.TryCreateCompositeInfrastructure(
                            shaderCode,
                            bindGroupLayoutFactory,
                            out BindGroupLayout* createdBindGroupLayout,
                            out PipelineLayout* createdPipelineLayout,
                            out ShaderModule* createdShaderModule,
                            out error))
                    {
                        return false;
                    }

                    infrastructure.BindGroupLayout = createdBindGroupLayout;
                    infrastructure.PipelineLayout = createdPipelineLayout;
                    infrastructure.ShaderModule = createdShaderModule;
                }

                bindGroupLayout = infrastructure.BindGroupLayout;
                if (infrastructure.Pipeline is not null)
                {
                    pipeline = infrastructure.Pipeline;
                    error = null;
                    return true;
                }

                ComputePipeline* createdPipeline = this.CreateCompositeComputePipeline(
                    infrastructure.PipelineLayout,
                    infrastructure.ShaderModule);
                if (createdPipeline is null)
                {
                    error = $"Failed to create composite compute pipeline '{pipelineKey}'.";
                    return false;
                }

                infrastructure.Pipeline = createdPipeline;
                pipeline = createdPipeline;
                error = null;
                return true;
            }
        }

        /// <summary>
        /// Gets or creates a reusable shared buffer for device-scoped operations.
        /// </summary>
        public bool TryGetOrCreateSharedBuffer(
            string bufferKey,
            BufferUsage usage,
            nuint requiredSize,
            out WgpuBuffer* buffer,
            out nuint capacity,
            out string? error)
        {
            buffer = null;
            capacity = 0;

            if (this.disposed)
            {
                error = "WebGPU device state is disposed.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(bufferKey))
            {
                error = "Shared buffer key cannot be empty.";
                return false;
            }

            if (requiredSize == 0)
            {
                error = $"Shared buffer '{bufferKey}' requires a non-zero size.";
                return false;
            }

            SharedBufferInfrastructure infrastructure = this.sharedBuffers.GetOrAdd(
                bufferKey,
                static _ => new SharedBufferInfrastructure());
            lock (infrastructure)
            {
                if (infrastructure.Buffer is not null &&
                    infrastructure.Capacity >= requiredSize &&
                    infrastructure.Usage == usage)
                {
                    buffer = infrastructure.Buffer;
                    capacity = infrastructure.Capacity;
                    error = null;
                    return true;
                }

                if (infrastructure.Buffer is not null)
                {
                    this.Api.BufferRelease(infrastructure.Buffer);
                    infrastructure.Buffer = null;
                    infrastructure.Capacity = 0;
                }

                BufferDescriptor descriptor = new()
                {
                    Usage = usage,
                    Size = requiredSize
                };

                WgpuBuffer* createdBuffer = this.Api.DeviceCreateBuffer(this.Device, in descriptor);
                if (createdBuffer is null)
                {
                    error = $"Failed to create shared buffer '{bufferKey}'.";
                    return false;
                }

                infrastructure.Buffer = createdBuffer;
                infrastructure.Capacity = requiredSize;
                infrastructure.Usage = usage;
                buffer = createdBuffer;
                capacity = requiredSize;
                error = null;
                return true;
            }
        }

        /// <summary>
        /// Releases all cached pipelines, buffers, and CPU-target entries owned by this state.
        /// </summary>
        public void Dispose()
        {
            if (this.disposed)
            {
                return;
            }

            foreach (CompositePipelineInfrastructure infrastructure in this.compositePipelines.Values)
            {
                this.ReleaseCompositeInfrastructure(infrastructure);
            }

            this.compositePipelines.Clear();

            foreach (CompositeComputePipelineInfrastructure infrastructure in this.compositeComputePipelines.Values)
            {
                this.ReleaseCompositeComputeInfrastructure(infrastructure);
            }

            this.compositeComputePipelines.Clear();

            foreach (SharedBufferInfrastructure infrastructure in this.sharedBuffers.Values)
            {
                lock (infrastructure)
                {
                    if (infrastructure.Buffer is not null)
                    {
                        this.Api.BufferRelease(infrastructure.Buffer);
                        infrastructure.Buffer = null;
                        infrastructure.Capacity = 0;
                    }
                }
            }

            this.sharedBuffers.Clear();

            foreach (CpuTargetEntry entry in this.cpuTargetCache.Values)
            {
                entry.Dispose(this.Api);
            }

            this.cpuTargetCache.Clear();
            this.disposed = true;
        }

        private bool TryCreateCompositeInfrastructure(
            ReadOnlySpan<byte> shaderCode,
            WebGPUCompositeBindGroupLayoutFactory bindGroupLayoutFactory,
            out BindGroupLayout* bindGroupLayout,
            out PipelineLayout* pipelineLayout,
            out ShaderModule* shaderModule,
            out string? error)
        {
            bindGroupLayout = null;
            pipelineLayout = null;
            shaderModule = null;

            if (!bindGroupLayoutFactory(this.Api, this.Device, out bindGroupLayout, out error))
            {
                return false;
            }

            BindGroupLayout** bindGroupLayouts = stackalloc BindGroupLayout*[1];
            bindGroupLayouts[0] = bindGroupLayout;
            PipelineLayoutDescriptor pipelineLayoutDescriptor = new()
            {
                BindGroupLayoutCount = 1,
                BindGroupLayouts = bindGroupLayouts
            };

            pipelineLayout = this.Api.DeviceCreatePipelineLayout(this.Device, in pipelineLayoutDescriptor);
            if (pipelineLayout is null)
            {
                this.Api.BindGroupLayoutRelease(bindGroupLayout);
                error = "Failed to create composite pipeline layout.";
                return false;
            }

            shaderModule = this.CreateShaderModule(shaderCode);

            if (shaderModule is null)
            {
                this.Api.PipelineLayoutRelease(pipelineLayout);
                this.Api.BindGroupLayoutRelease(bindGroupLayout);
                error = "Failed to create composite shader module.";
                return false;
            }

            error = null;
            return true;
        }

        private RenderPipeline* CreateCompositePipeline(
            PipelineLayout* pipelineLayout,
            ShaderModule* shaderModule,
            TextureFormat textureFormat,
            CompositePipelineBlendMode blendMode)
        {
            ReadOnlySpan<byte> vertexEntryPoint = CompositeVertexEntryPoint;
            ReadOnlySpan<byte> fragmentEntryPoint = CompositeFragmentEntryPoint;
            fixed (byte* vertexEntryPointPtr = vertexEntryPoint)
            {
                fixed (byte* fragmentEntryPointPtr = fragmentEntryPoint)
                {
                    return this.CreateCompositePipelineCore(
                        pipelineLayout,
                        shaderModule,
                        vertexEntryPointPtr,
                        fragmentEntryPointPtr,
                        textureFormat,
                        blendMode);
                }
            }
        }

        private RenderPipeline* CreateCompositePipelineCore(
            PipelineLayout* pipelineLayout,
            ShaderModule* shaderModule,
            byte* vertexEntryPointPtr,
            byte* fragmentEntryPointPtr,
            TextureFormat textureFormat,
            CompositePipelineBlendMode blendMode)
        {
            _ = blendMode;
            VertexState vertexState = new()
            {
                Module = shaderModule,
                EntryPoint = vertexEntryPointPtr,
                BufferCount = 0,
                Buffers = null
            };

            ColorTargetState* colorTargets = stackalloc ColorTargetState[1];
            colorTargets[0] = new ColorTargetState
            {
                Format = textureFormat,
                Blend = null,
                WriteMask = ColorWriteMask.All
            };

            FragmentState fragmentState = new()
            {
                Module = shaderModule,
                EntryPoint = fragmentEntryPointPtr,
                TargetCount = 1,
                Targets = colorTargets
            };

            RenderPipelineDescriptor descriptor = new()
            {
                Layout = pipelineLayout,
                Vertex = vertexState,
                Primitive = new PrimitiveState
                {
                    Topology = PrimitiveTopology.TriangleList,
                    StripIndexFormat = IndexFormat.Undefined,
                    FrontFace = FrontFace.Ccw,
                    CullMode = CullMode.None
                },
                DepthStencil = null,
                Multisample = new MultisampleState
                {
                    Count = 1,
                    Mask = uint.MaxValue,
                    AlphaToCoverageEnabled = false
                },
                Fragment = &fragmentState
            };

            return this.Api.DeviceCreateRenderPipeline(this.Device, in descriptor);
        }

        private ComputePipeline* CreateCompositeComputePipeline(
            PipelineLayout* pipelineLayout,
            ShaderModule* shaderModule)
        {
            ReadOnlySpan<byte> entryPoint = CompositeComputeEntryPoint;
            fixed (byte* entryPointPtr = entryPoint)
            {
                ProgrammableStageDescriptor computeState = new()
                {
                    Module = shaderModule,
                    EntryPoint = entryPointPtr
                };

                ComputePipelineDescriptor descriptor = new()
                {
                    Layout = pipelineLayout,
                    Compute = computeState
                };

                return this.Api.DeviceCreateComputePipeline(this.Device, in descriptor);
            }
        }

        private ShaderModule* CreateShaderModule(ReadOnlySpan<byte> shaderCode)
        {
            System.Diagnostics.Debug.Assert(
                !shaderCode.IsEmpty && shaderCode[^1] == 0,
                "WGSL shader code must be null-terminated at the call site.");

            fixed (byte* shaderCodePtr = shaderCode)
            {
                ShaderModuleWGSLDescriptor wgslDescriptor = new()
                {
                    Chain = new ChainedStruct { SType = SType.ShaderModuleWgslDescriptor },
                    Code = shaderCodePtr
                };

                ShaderModuleDescriptor shaderDescriptor = new()
                {
                    NextInChain = (ChainedStruct*)&wgslDescriptor
                };

                return this.Api.DeviceCreateShaderModule(this.Device, in shaderDescriptor);
            }
        }

        private void ReleaseCompositeInfrastructure(CompositePipelineInfrastructure infrastructure)
        {
            foreach (nint pipelineHandle in infrastructure.Pipelines.Values)
            {
                if (pipelineHandle != 0)
                {
                    this.Api.RenderPipelineRelease((RenderPipeline*)pipelineHandle);
                }
            }

            infrastructure.Pipelines.Clear();

            if (infrastructure.PipelineLayout is not null)
            {
                this.Api.PipelineLayoutRelease(infrastructure.PipelineLayout);
                infrastructure.PipelineLayout = null;
            }

            if (infrastructure.ShaderModule is not null)
            {
                this.Api.ShaderModuleRelease(infrastructure.ShaderModule);
                infrastructure.ShaderModule = null;
            }

            if (infrastructure.BindGroupLayout is not null)
            {
                this.Api.BindGroupLayoutRelease(infrastructure.BindGroupLayout);
                infrastructure.BindGroupLayout = null;
            }
        }

        private void ReleaseCompositeComputeInfrastructure(CompositeComputePipelineInfrastructure infrastructure)
        {
            if (infrastructure.Pipeline is not null)
            {
                this.Api.ComputePipelineRelease(infrastructure.Pipeline);
                infrastructure.Pipeline = null;
            }

            if (infrastructure.PipelineLayout is not null)
            {
                this.Api.PipelineLayoutRelease(infrastructure.PipelineLayout);
                infrastructure.PipelineLayout = null;
            }

            if (infrastructure.ShaderModule is not null)
            {
                this.Api.ShaderModuleRelease(infrastructure.ShaderModule);
                infrastructure.ShaderModule = null;
            }

            if (infrastructure.BindGroupLayout is not null)
            {
                this.Api.BindGroupLayoutRelease(infrastructure.BindGroupLayout);
                infrastructure.BindGroupLayout = null;
            }
        }

        /// <summary>
        /// Cache key for CPU-target staging resources.
        /// </summary>
        internal readonly struct CpuTargetCacheKey(
            TextureFormat textureFormat,
            int width,
            int height,
            int pixelSizeInBytes) : IEquatable<CpuTargetCacheKey>
        {
            /// <summary>
            /// Gets the texture format for the cached CPU target.
            /// </summary>
            public TextureFormat TextureFormat { get; } = textureFormat;

            /// <summary>
            /// Gets the target width.
            /// </summary>
            public int Width { get; } = width;

            /// <summary>
            /// Gets the target height.
            /// </summary>
            public int Height { get; } = height;

            /// <summary>
            /// Gets the pixel size in bytes.
            /// </summary>
            public int PixelSizeInBytes { get; } = pixelSizeInBytes;

            /// <summary>
            /// Determines whether this key equals another CPU target cache key.
            /// </summary>
            /// <param name="other">The key to compare.</param>
            /// <returns><see langword="true"/> if all dimensions and format match; otherwise <see langword="false"/>.</returns>
            public bool Equals(CpuTargetCacheKey other)
                => this.TextureFormat == other.TextureFormat &&
                   this.Width == other.Width &&
                   this.Height == other.Height &&
                   this.PixelSizeInBytes == other.PixelSizeInBytes;

            /// <inheritdoc/>
            public override bool Equals(object? obj) => obj is CpuTargetCacheKey other && this.Equals(other);

            /// <inheritdoc/>
            public override int GetHashCode() => HashCode.Combine((int)this.TextureFormat, this.Width, this.Height, this.PixelSizeInBytes);
        }

        /// <summary>
        /// Cache entry that owns the CPU-target staging resources.
        /// </summary>
        internal sealed class CpuTargetEntry
        {
            private Texture* targetTexture;
            private TextureView* targetView;
            private WgpuBuffer* readbackBuffer;
            private uint readbackBytesPerRow;
            private ulong readbackByteCount;
            private int inUse;

            /// <summary>
            /// Rents staging resources for the specified cache key.
            /// </summary>
            internal CpuTargetLease Rent(WebGPU api, Device* device, in CpuTargetCacheKey key)
            {
                if (Interlocked.CompareExchange(ref this.inUse, 1, 0) == 0)
                {
                    try
                    {
                        this.EnsureResources(api, device, in key);
                    }
                    catch
                    {
                        this.Release();
                        throw;
                    }

                    return new CpuTargetLease(
                        api,
                        this,
                        ownsResources: false,
                        this.targetTexture,
                        this.targetView,
                        this.readbackBuffer,
                        this.readbackBytesPerRow,
                        this.readbackByteCount);
                }

                if (!TryCreateCpuTargetResources(
                        api,
                        device,
                        in key,
                        out Texture* temporaryTexture,
                        out TextureView* temporaryView,
                        out WgpuBuffer* temporaryReadbackBuffer,
                        out uint temporaryReadbackRowBytes,
                        out ulong temporaryReadbackByteCount))
                {
                    throw new InvalidOperationException("Failed to create temporary CPU flush target resources.");
                }

                return new CpuTargetLease(
                    api,
                    owner: null,
                    ownsResources: true,
                    temporaryTexture,
                    temporaryView,
                    temporaryReadbackBuffer,
                    temporaryReadbackRowBytes,
                    temporaryReadbackByteCount);
            }

            /// <summary>
            /// Marks this entry as available for reuse.
            /// </summary>
            internal void Release() => Volatile.Write(ref this.inUse, 0);

            /// <summary>
            /// Releases all resources currently owned by this entry.
            /// </summary>
            internal void Dispose(WebGPU api)
            {
                ReleaseCpuTargetResources(api, this.targetTexture, this.targetView, this.readbackBuffer);
                this.targetTexture = null;
                this.targetView = null;
                this.readbackBuffer = null;
                this.readbackBytesPerRow = 0;
                this.readbackByteCount = 0;
                this.inUse = 0;
            }

            private void EnsureResources(WebGPU api, Device* device, in CpuTargetCacheKey key)
            {
                if (this.targetTexture is not null &&
                    this.targetView is not null &&
                    this.readbackBuffer is not null)
                {
                    return;
                }

                ReleaseCpuTargetResources(api, this.targetTexture, this.targetView, this.readbackBuffer);
                this.targetTexture = null;
                this.targetView = null;
                this.readbackBuffer = null;
                this.readbackBytesPerRow = 0;
                this.readbackByteCount = 0;

                if (!TryCreateCpuTargetResources(
                        api,
                        device,
                        in key,
                        out this.targetTexture,
                        out this.targetView,
                        out this.readbackBuffer,
                        out this.readbackBytesPerRow,
                        out this.readbackByteCount))
                {
                    throw new InvalidOperationException("Failed to create cached CPU flush target resources.");
                }
            }

            private static bool TryCreateCpuTargetResources(
                WebGPU api,
                Device* device,
                in CpuTargetCacheKey key,
                out Texture* targetTexture,
                out TextureView* targetView,
                out WgpuBuffer* readbackBuffer,
                out uint readbackBytesPerRow,
                out ulong readbackByteCount)
            {
                targetTexture = null;
                targetView = null;
                readbackBuffer = null;
                readbackBytesPerRow = 0;
                readbackByteCount = 0;

                uint textureRowBytes = checked((uint)key.Width * (uint)key.PixelSizeInBytes);
                readbackBytesPerRow = AlignTo256(textureRowBytes);
                readbackByteCount = checked((ulong)readbackBytesPerRow * (uint)key.Height);

                TextureDescriptor targetTextureDescriptor = new()
                {
                    Usage = TextureUsage.RenderAttachment | TextureUsage.CopySrc | TextureUsage.CopyDst | TextureUsage.TextureBinding,
                    Dimension = TextureDimension.Dimension2D,
                    Size = new Extent3D((uint)key.Width, (uint)key.Height, 1),
                    Format = key.TextureFormat,
                    MipLevelCount = 1,
                    SampleCount = 1
                };

                targetTexture = api.DeviceCreateTexture(device, in targetTextureDescriptor);
                if (targetTexture is null)
                {
                    return false;
                }

                TextureViewDescriptor targetViewDescriptor = new()
                {
                    Format = key.TextureFormat,
                    Dimension = TextureViewDimension.Dimension2D,
                    BaseMipLevel = 0,
                    MipLevelCount = 1,
                    BaseArrayLayer = 0,
                    ArrayLayerCount = 1,
                    Aspect = TextureAspect.All
                };

                targetView = api.TextureCreateView(targetTexture, in targetViewDescriptor);
                if (targetView is null)
                {
                    api.TextureRelease(targetTexture);
                    targetTexture = null;
                    return false;
                }

                BufferDescriptor readbackDescriptor = new()
                {
                    Usage = BufferUsage.MapRead | BufferUsage.CopyDst,
                    Size = readbackByteCount
                };

                readbackBuffer = api.DeviceCreateBuffer(device, in readbackDescriptor);
                if (readbackBuffer is null)
                {
                    api.TextureViewRelease(targetView);
                    api.TextureRelease(targetTexture);
                    targetView = null;
                    targetTexture = null;
                    return false;
                }

                return true;
            }

            private static void ReleaseCpuTargetResources(
                WebGPU api,
                Texture* targetTexture,
                TextureView* targetView,
                WgpuBuffer* readbackBuffer)
            {
                if (readbackBuffer is not null)
                {
                    api.BufferRelease(readbackBuffer);
                }

                if (targetView is not null)
                {
                    api.TextureViewRelease(targetView);
                }

                if (targetTexture is not null)
                {
                    api.TextureRelease(targetTexture);
                }
            }
        }

        /// <summary>
        /// Lease wrapper for CPU-target staging resources.
        /// </summary>
        public sealed class CpuTargetLease : IDisposable
        {
            private readonly WebGPU api;
            private readonly CpuTargetEntry? owner;
            private readonly bool ownsResources;
            private int disposed;

            internal CpuTargetLease(
                WebGPU api,
                CpuTargetEntry? owner,
                bool ownsResources,
                Texture* targetTexture,
                TextureView* targetView,
                WgpuBuffer* readbackBuffer,
                uint readbackBytesPerRow,
                ulong readbackByteCount)
            {
                this.api = api;
                this.owner = owner;
                this.ownsResources = ownsResources;
                this.TargetTexture = targetTexture;
                this.TargetView = targetView;
                this.ReadbackBuffer = readbackBuffer;
                this.ReadbackBytesPerRow = readbackBytesPerRow;
                this.ReadbackByteCount = readbackByteCount;
            }

            /// <summary>
            /// Gets the target texture used for CPU staging operations.
            /// </summary>
            public Texture* TargetTexture { get; }

            /// <summary>
            /// Gets the texture view of <see cref="TargetTexture"/>.
            /// </summary>
            public TextureView* TargetView { get; }

            /// <summary>
            /// Gets the readback buffer used to copy staged pixels to CPU memory.
            /// </summary>
            public WgpuBuffer* ReadbackBuffer { get; }

            /// <summary>
            /// Gets the readback row stride in bytes.
            /// </summary>
            public uint ReadbackBytesPerRow { get; }

            /// <summary>
            /// Gets the total readback buffer size in bytes.
            /// </summary>
            public ulong ReadbackByteCount { get; }

            /// <summary>
            /// Releases leased resources or returns ownership to the shared cache entry.
            /// </summary>
            public void Dispose()
            {
                if (Interlocked.Exchange(ref this.disposed, 1) != 0)
                {
                    return;
                }

                if (this.ownsResources)
                {
                    if (this.ReadbackBuffer is not null)
                    {
                        this.api.BufferRelease(this.ReadbackBuffer);
                    }

                    if (this.TargetView is not null)
                    {
                        this.api.TextureViewRelease(this.TargetView);
                    }

                    if (this.TargetTexture is not null)
                    {
                        this.api.TextureRelease(this.TargetTexture);
                    }
                }

                this.owner?.Release();
            }
        }

        /// <summary>
        /// Shared render-pipeline infrastructure for compositing variants.
        /// </summary>
        private sealed class CompositePipelineInfrastructure
        {
            public Dictionary<(TextureFormat TextureFormat, CompositePipelineBlendMode BlendMode), nint> Pipelines { get; } = [];

            public BindGroupLayout* BindGroupLayout { get; set; }

            public PipelineLayout* PipelineLayout { get; set; }

            public ShaderModule* ShaderModule { get; set; }
        }

        private sealed class CompositeComputePipelineInfrastructure
        {
            public BindGroupLayout* BindGroupLayout { get; set; }

            public PipelineLayout* PipelineLayout { get; set; }

            public ShaderModule* ShaderModule { get; set; }

            public ComputePipeline* Pipeline { get; set; }
        }

        private sealed class SharedBufferInfrastructure
        {
            public WgpuBuffer* Buffer { get; set; }

            public nuint Capacity { get; set; }

            public BufferUsage Usage { get; set; }
        }
    }

    /// <summary>
    /// Lease over a CPU fallback staging region.
    /// </summary>
    /// <typeparam name="TPixel">The pixel type of the staging region.</typeparam>
    public sealed class FallbackStagingLease<TPixel> : IDisposable
        where TPixel : unmanaged, IPixel<TPixel>
    {
        private readonly FallbackStagingEntry<TPixel>? owner;
        private readonly Buffer2D<TPixel>? temporaryBuffer;
        private int disposed;

        /// <summary>
        /// Initializes a new instance of the <see cref="FallbackStagingLease{TPixel}"/> class.
        /// </summary>
        internal FallbackStagingLease(
            Buffer2DRegion<TPixel> region,
            FallbackStagingEntry<TPixel>? owner,
            Buffer2D<TPixel>? temporaryBuffer)
        {
            this.Region = region;
            this.owner = owner;
            this.temporaryBuffer = temporaryBuffer;
        }

        /// <summary>
        /// Gets the staging region for fallback rendering.
        /// </summary>
        public Buffer2DRegion<TPixel> Region { get; }

        /// <inheritdoc />
        public void Dispose()
        {
            if (Interlocked.Exchange(ref this.disposed, 1) != 0)
            {
                return;
            }

            this.temporaryBuffer?.Dispose();
            this.owner?.Release();
        }
    }

    /// <summary>
    /// Cached staging entry for one pixel type.
    /// </summary>
    /// <typeparam name="TPixel">The pixel type stored by this entry.</typeparam>
    internal sealed class FallbackStagingEntry<TPixel> : IDisposable
        where TPixel : unmanaged, IPixel<TPixel>
    {
        private Buffer2D<TPixel>? buffer;
        private Size size;
        private int inUse;

        /// <summary>
        /// Rents a staging lease for the specified bounds.
        /// </summary>
        public FallbackStagingLease<TPixel> Rent(MemoryAllocator allocator, in Rectangle bounds)
        {
            if (Interlocked.CompareExchange(ref this.inUse, 1, 0) == 0)
            {
                this.EnsureSize(allocator, bounds.Size);
                Buffer2D<TPixel>? current = this.buffer;
                if (current is null)
                {
                    this.Release();
                    throw new InvalidOperationException("Fallback staging buffer is not initialized.");
                }

                return new FallbackStagingLease<TPixel>(
                    new Buffer2DRegion<TPixel>(current, bounds),
                    this,
                    temporaryBuffer: null);
            }

            Buffer2D<TPixel> temporary = allocator.Allocate2D<TPixel>(bounds.Size, AllocationOptions.Clean);
            return new FallbackStagingLease<TPixel>(
                new Buffer2DRegion<TPixel>(temporary, bounds),
                owner: null,
                temporaryBuffer: temporary);
        }

        /// <summary>
        /// Releases an acquired cached staging entry.
        /// </summary>
        public void Release()
            => Volatile.Write(ref this.inUse, 0);

        /// <inheritdoc />
        public void Dispose()
        {
            this.buffer?.Dispose();
            this.buffer = null;
            this.size = default;
            this.inUse = 0;
        }

        private void EnsureSize(MemoryAllocator allocator, Size requiredSize)
        {
            if (this.buffer is not null &&
                this.size.Width >= requiredSize.Width &&
                this.size.Height >= requiredSize.Height)
            {
                return;
            }

            this.buffer?.Dispose();
            this.buffer = allocator.Allocate2D<TPixel>(requiredSize, AllocationOptions.Clean);
            this.size = requiredSize;
        }
    }
}
