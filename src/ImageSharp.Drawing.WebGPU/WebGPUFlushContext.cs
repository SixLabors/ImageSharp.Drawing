// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Silk.NET.WebGPU;
using SixLabors.ImageSharp.Drawing.Shapes.Rasterization;
using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.PixelFormats;
using WgpuBuffer = Silk.NET.WebGPU.Buffer;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Per-flush WebGPU execution context created from a single frame target.
/// </summary>
internal sealed unsafe class WebGPUFlushContext : IDisposable
{
    private static readonly ConcurrentDictionary<Type, IDisposable> FallbackStagingCache = new();
    private static readonly ConcurrentDictionary<nint, DeviceSharedState> DeviceStateCache = new();
    private static readonly ConcurrentDictionary<int, WebGPUFlushContext> CpuReadbackFlushContexts = new();
    private static readonly object SharedHandleSync = new();
    private const int CallbackTimeoutMilliseconds = 10_000;

    private bool disposed;
    private bool ownsTargetTexture;
    private bool ownsTargetView;
    private bool ownsReadbackBuffer;
    private readonly List<nint> transientBindGroups = [];

    private WebGPUFlushContext(
        WebGPURuntime.Lease runtimeLease,
        Device* device,
        Queue* queue,
        in Rectangle targetBounds,
        TextureFormat textureFormat,
        DeviceSharedState deviceState)
    {
        this.RuntimeLease = runtimeLease;
        this.Api = runtimeLease.Api;
        this.Device = device;
        this.Queue = queue;
        this.TargetBounds = targetBounds;
        this.TextureFormat = textureFormat;
        this.DeviceState = deviceState;
    }

    public WebGPURuntime.Lease RuntimeLease { get; }

    public WebGPU Api { get; }

    public Device* Device { get; }

    public Queue* Queue { get; }

    public Rectangle TargetBounds { get; }

    public TextureFormat TextureFormat { get; }

    public DeviceSharedState DeviceState { get; }

    public Texture* TargetTexture { get; private set; }

    public TextureView* TargetView { get; private set; }

    public bool RequiresReadback { get; private set; }

    public WgpuBuffer* ReadbackBuffer { get; private set; }

    public uint ReadbackBytesPerRow { get; private set; }

    public ulong ReadbackByteCount { get; private set; }

    public WgpuBuffer* InstanceBuffer { get; private set; }

    public nuint InstanceBufferCapacity { get; private set; }

    public CommandEncoder* CommandEncoder { get; set; }

    public RenderPassEncoder* PassEncoder { get; private set; }

    public static WebGPUFlushContext Create<TPixel>(
        ICanvasFrame<TPixel> frame,
        TextureFormat expectedTextureFormat,
        int pixelSizeInBytes)
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
                context = new WebGPUFlushContext(lease, device, queue, in bounds, textureFormat, deviceState);
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
            context = new WebGPUFlushContext(lease, device, queue, in bounds, expectedTextureFormat, deviceState);
            context.InitializeCpuTarget(cpuRegion, pixelSizeInBytes);
            return context;
        }
        catch
        {
            lease.Dispose();
            throw;
        }
    }

    public static WebGPUFlushContext CreateUploadContext<TPixel>(ICanvasFrame<TPixel> frame)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        WebGPUSurfaceCapability? nativeCapability =
            TryGetWritableNativeSurfaceCapability(frame)
            ?? throw new NotSupportedException("Fallback upload requires a native WebGPU surface exposing writable device, queue, and texture handles.");

        WebGPURuntime.Lease lease = WebGPURuntime.Acquire();
        try
        {
            Rectangle bounds = new Rectangle(0, 0, nativeCapability.Width, nativeCapability.Height);
            TextureFormat textureFormat = WebGPUTextureFormatMapper.ToSilk(nativeCapability.TargetFormat);
            Device* device = (Device*)nativeCapability.Device;
            DeviceSharedState deviceState = GetOrCreateDeviceState(lease.Api, device);
            WebGPUFlushContext context = new(
                lease,
                device,
                (Queue*)nativeCapability.Queue,
                in bounds,
                textureFormat,
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

    public static FallbackStagingLease<TPixel> RentFallbackStaging<TPixel>(MemoryAllocator allocator, in Rectangle bounds)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        IDisposable entry = FallbackStagingCache.GetOrAdd(
            typeof(TPixel),
            static _ => new FallbackStagingEntry<TPixel>());

        return ((FallbackStagingEntry<TPixel>)entry).Rent(allocator, in bounds);
    }

    public static void ClearFallbackStagingCache()
    {
        foreach (IDisposable entry in FallbackStagingCache.Values)
        {
            entry.Dispose();
        }

        FallbackStagingCache.Clear();
    }

    public static void ClearDeviceStateCache()
    {
        foreach (WebGPUFlushContext context in CpuReadbackFlushContexts.Values)
        {
            context.Dispose();
        }

        CpuReadbackFlushContexts.Clear();

        foreach (DeviceSharedState state in DeviceStateCache.Values)
        {
            state.Dispose();
        }

        DeviceStateCache.Clear();
    }

    public static WebGPUFlushContext GetOrCreateCpuReadbackFlushContext<TPixel>(
        int flushId,
        ICanvasFrame<TPixel> frame,
        TextureFormat expectedTextureFormat,
        int pixelSizeInBytes,
        out bool fromCache)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        if (CpuReadbackFlushContexts.TryGetValue(flushId, out WebGPUFlushContext? cached))
        {
            fromCache = true;
            return cached;
        }

        fromCache = false;
        WebGPUFlushContext created = Create(frame, expectedTextureFormat, pixelSizeInBytes);
        if (!created.RequiresReadback)
        {
            return created;
        }

        if (CpuReadbackFlushContexts.TryAdd(flushId, created))
        {
            return created;
        }

        created.Dispose();
        fromCache = true;
        return CpuReadbackFlushContexts[flushId];
    }

    public static void CompleteCpuReadbackFlushContext(int flushId)
    {
        if (CpuReadbackFlushContexts.TryRemove(flushId, out WebGPUFlushContext? context))
        {
            context.Dispose();
        }
    }

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

    public bool BeginRenderPass()
    {
        if (this.PassEncoder is not null)
        {
            return true;
        }

        if (this.CommandEncoder is null || this.TargetView is null)
        {
            return false;
        }

        RenderPassColorAttachment colorAttachment = new()
        {
            View = this.TargetView,
            ResolveTarget = null,
            LoadOp = LoadOp.Load,
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

    public void TrackBindGroup(BindGroup* bindGroup)
    {
        if (bindGroup is not null)
        {
            this.transientBindGroups.Add((nint)bindGroup);
        }
    }

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

        this.transientBindGroups.Clear();

        this.ReadbackBuffer = null;
        this.TargetView = null;
        this.TargetTexture = null;
        this.ReadbackBytesPerRow = 0;
        this.ReadbackByteCount = 0;
        this.RequiresReadback = false;
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
        if (DeviceStateCache.TryAdd(cacheKey, created))
        {
            return created;
        }

        created.Dispose();
        return DeviceStateCache.TryGetValue(cacheKey, out DeviceSharedState? winner)
            ? winner
            : GetOrCreateDeviceState(api, device);
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
        this.ReadbackBuffer = null;
        this.ReadbackBytesPerRow = 0;
        this.ReadbackByteCount = 0;
        this.ownsTargetTexture = false;
        this.ownsTargetView = false;
        this.ownsReadbackBuffer = false;
    }

    private void InitializeCpuTarget<TPixel>(Buffer2DRegion<TPixel> cpuRegion, int pixelSizeInBytes)
        where TPixel : unmanaged
    {
        int width = cpuRegion.Width;
        int height = cpuRegion.Height;
        uint textureRowBytes = checked((uint)width * (uint)pixelSizeInBytes);
        uint readbackRowBytes = AlignTo256(textureRowBytes);
        ulong readbackByteCount = checked((ulong)readbackRowBytes * (uint)height);

        TextureDescriptor targetTextureDescriptor = new()
        {
            Usage = TextureUsage.RenderAttachment | TextureUsage.CopySrc | TextureUsage.CopyDst,
            Dimension = TextureDimension.Dimension2D,
            Size = new Extent3D((uint)width, (uint)height, 1),
            Format = this.TextureFormat,
            MipLevelCount = 1,
            SampleCount = 1
        };

        Texture* targetTexture = this.Api.DeviceCreateTexture(this.Device, in targetTextureDescriptor);
        if (targetTexture is null)
        {
            throw new InvalidOperationException("Failed to create CPU flush target texture.");
        }

        TextureViewDescriptor targetViewDescriptor = new()
        {
            Format = this.TextureFormat,
            Dimension = TextureViewDimension.Dimension2D,
            BaseMipLevel = 0,
            MipLevelCount = 1,
            BaseArrayLayer = 0,
            ArrayLayerCount = 1,
            Aspect = TextureAspect.All
        };

        TextureView* targetView = this.Api.TextureCreateView(targetTexture, in targetViewDescriptor);
        if (targetView is null)
        {
            this.Api.TextureRelease(targetTexture);
            throw new InvalidOperationException("Failed to create CPU flush target view.");
        }

        BufferDescriptor readbackDescriptor = new()
        {
            Usage = BufferUsage.MapRead | BufferUsage.CopyDst,
            Size = readbackByteCount
        };

        WgpuBuffer* readbackBuffer = this.Api.DeviceCreateBuffer(this.Device, in readbackDescriptor);
        if (readbackBuffer is null)
        {
            this.Api.TextureViewRelease(targetView);
            this.Api.TextureRelease(targetTexture);
            throw new InvalidOperationException("Failed to create CPU flush readback buffer.");
        }

        try
        {
            QueueWriteTextureFromRegion(this.Api, this.Queue, targetTexture, cpuRegion);
        }
        catch
        {
            this.Api.BufferRelease(readbackBuffer);
            this.Api.TextureViewRelease(targetView);
            this.Api.TextureRelease(targetTexture);
            throw;
        }

        this.TargetTexture = targetTexture;
        this.TargetView = targetView;
        this.ReadbackBuffer = readbackBuffer;
        this.ReadbackBytesPerRow = readbackRowBytes;
        this.ReadbackByteCount = readbackByteCount;
        this.RequiresReadback = true;
        this.ownsTargetTexture = true;
        this.ownsTargetView = true;
        this.ownsReadbackBuffer = true;
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

    private static void QueueWriteTextureFromRegion<TPixel>(
        WebGPU api,
        Queue* queue,
        Texture* destinationTexture,
        Buffer2DRegion<TPixel> sourceRegion)
        where TPixel : unmanaged
    {
        int pixelSizeInBytes = Unsafe.SizeOf<TPixel>();
        ImageCopyTexture destination = new()
        {
            Texture = destinationTexture,
            MipLevel = 0,
            Origin = new Origin3D(0, 0, 0),
            Aspect = TextureAspect.All
        };

        Extent3D writeSize = new((uint)sourceRegion.Width, (uint)sourceRegion.Height, 1);

        if (sourceRegion.Rectangle.X == 0 &&
            sourceRegion.Width == sourceRegion.Buffer.Width &&
            sourceRegion.Buffer.MemoryGroup.Count == 1)
        {
            int sourceStrideBytes = checked(sourceRegion.Buffer.Width * pixelSizeInBytes);
            int sourceRowBytes = checked(sourceRegion.Width * pixelSizeInBytes);
            nuint sourceByteCount = checked((nuint)(((long)sourceStrideBytes * (sourceRegion.Height - 1)) + sourceRowBytes));

            TextureDataLayout layout = new()
            {
                Offset = 0,
                BytesPerRow = (uint)sourceStrideBytes,
                RowsPerImage = (uint)sourceRegion.Height
            };

            Span<TPixel> firstRow = sourceRegion.DangerousGetRowSpan(0);
            fixed (TPixel* uploadPtr = firstRow)
            {
                api.QueueWriteTexture(queue, in destination, uploadPtr, sourceByteCount, in layout, in writeSize);
            }

            return;
        }

        int packedRowBytes = checked(sourceRegion.Width * pixelSizeInBytes);
        int packedByteCount = checked(packedRowBytes * sourceRegion.Height);
        byte[] rented = ArrayPool<byte>.Shared.Rent(packedByteCount);
        try
        {
            Span<byte> packedData = rented.AsSpan(0, packedByteCount);
            for (int y = 0; y < sourceRegion.Height; y++)
            {
                ReadOnlySpan<TPixel> sourceRow = sourceRegion.DangerousGetRowSpan(y);
                MemoryMarshal.AsBytes(sourceRow).CopyTo(packedData.Slice(y * packedRowBytes, packedRowBytes));
            }

            TextureDataLayout layout = new()
            {
                Offset = 0,
                BytesPerRow = (uint)packedRowBytes,
                RowsPerImage = (uint)sourceRegion.Height
            };

            fixed (byte* uploadPtr = packedData)
            {
                api.QueueWriteTexture(queue, in destination, uploadPtr, (nuint)packedByteCount, in layout, in writeSize);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint AlignTo256(uint value) => (value + 255U) & ~255U;

    internal sealed class DeviceSharedState : IDisposable
    {
        private readonly Dictionary<int, CoverageEntry> coverageCache = [];
        private readonly ConcurrentDictionary<TextureFormat, nint> compositePipelines = new();
        private WebGPURasterizer? coverageRasterizer;
        private PipelineLayout* compositePipelineLayout;
        private bool disposed;

        internal DeviceSharedState(WebGPU api, Device* device)
        {
            this.Api = api;
            this.Device = device;
        }

        private static ReadOnlySpan<byte> CompositeVertexEntryPoint => "vs_main\0"u8;

        private static ReadOnlySpan<byte> CompositeFragmentEntryPoint => "fs_main\0"u8;

        public object SyncRoot { get; } = new();

        public WebGPU Api { get; }

        public Device* Device { get; }

        public BindGroupLayout* CompositeBindGroupLayout { get; private set; }

        public int CoverageCount => this.coverageCache.Count;

        public bool TryEnsureResources(out string? error)
        {
            if (this.disposed)
            {
                error = "WebGPU device state is disposed.";
                return false;
            }

            if (this.CompositeBindGroupLayout is null || this.compositePipelineLayout is null)
            {
                if (!this.TryCreateCompositeInfrastructure(out error))
                {
                    return false;
                }
            }

            this.coverageRasterizer ??= new WebGPURasterizer(this.Api);
            if (!this.coverageRasterizer.IsInitialized && !this.coverageRasterizer.Initialize(this.Device))
            {
                error = "Failed to initialize WebGPU coverage rasterizer.";
                return false;
            }

            error = null;
            return true;
        }

        public bool TryGetOrCreateCoverageEntry(
            in CompositionCoverageDefinition definition,
            Queue* queue,
            [NotNullWhen(true)] out CoverageEntry? coverageEntry,
            out string? error)
        {
            if (!this.TryEnsureResources(out error))
            {
                coverageEntry = null;
                return false;
            }

            if (this.coverageCache.TryGetValue(definition.DefinitionKey, out CoverageEntry? cached))
            {
                coverageEntry = cached;
                return true;
            }

            RasterizerOptions rasterizerOptions = definition.RasterizerOptions;
            if (this.coverageRasterizer is null ||
                !this.coverageRasterizer.TryCreateCoverageTexture(
                    definition.Path,
                    in rasterizerOptions,
                    this.Device,
                    queue,
                    out Texture* coverageTexture,
                    out TextureView* coverageView))
            {
                coverageEntry = null;
                error = "Failed to rasterize coverage texture.";
                return false;
            }

            Size size = rasterizerOptions.Interest.Size;
            coverageEntry = new CoverageEntry(size.Width, size.Height)
            {
                GPUCoverageTexture = coverageTexture,
                GPUCoverageView = coverageView
            };
            this.coverageCache.Add(definition.DefinitionKey, coverageEntry);
            error = null;
            return true;
        }

        public bool TryGetOrCreateCompositePipeline(TextureFormat textureFormat, out RenderPipeline* pipeline, out string? error)
        {
            if (!this.TryEnsureResources(out error))
            {
                pipeline = null;
                return false;
            }

            if (this.compositePipelines.TryGetValue(textureFormat, out nint existingHandle) && existingHandle != 0)
            {
                pipeline = (RenderPipeline*)existingHandle;
                return true;
            }

            RenderPipeline* created = this.CreateCompositePipelineForFormat(textureFormat);
            if (created is null)
            {
                pipeline = null;
                error = $"Failed to create composite pipeline for format '{textureFormat}'.";
                return false;
            }

            nint createdHandle = (nint)created;
            nint cachedHandle = this.compositePipelines.GetOrAdd(textureFormat, createdHandle);
            if (cachedHandle != createdHandle)
            {
                this.Api.RenderPipelineRelease(created);
            }

            pipeline = (RenderPipeline*)cachedHandle;
            error = null;
            return true;
        }

        public void Dispose()
        {
            if (this.disposed)
            {
                return;
            }

            foreach (CoverageEntry entry in this.coverageCache.Values)
            {
                ReleaseCoverageTexture(this.Api, entry);
            }

            this.coverageCache.Clear();

            this.coverageRasterizer?.Release();
            this.coverageRasterizer = null;

            foreach (KeyValuePair<TextureFormat, nint> entry in this.compositePipelines)
            {
                if (entry.Value != 0)
                {
                    this.Api.RenderPipelineRelease((RenderPipeline*)entry.Value);
                }
            }

            this.compositePipelines.Clear();

            if (this.compositePipelineLayout is not null)
            {
                this.Api.PipelineLayoutRelease(this.compositePipelineLayout);
                this.compositePipelineLayout = null;
            }

            if (this.CompositeBindGroupLayout is not null)
            {
                this.Api.BindGroupLayoutRelease(this.CompositeBindGroupLayout);
                this.CompositeBindGroupLayout = null;
            }

            this.disposed = true;
        }

        private bool TryCreateCompositeInfrastructure(out string? error)
        {
            BindGroupLayoutEntry* layoutEntries = stackalloc BindGroupLayoutEntry[2];
            layoutEntries[0] = new BindGroupLayoutEntry
            {
                Binding = 0,
                Visibility = ShaderStage.Fragment,
                Texture = new TextureBindingLayout
                {
                    SampleType = TextureSampleType.Float,
                    ViewDimension = TextureViewDimension.Dimension2D,
                    Multisampled = false
                }
            };
            layoutEntries[1] = new BindGroupLayoutEntry
            {
                Binding = 1,
                Visibility = ShaderStage.Vertex | ShaderStage.Fragment,
                Buffer = new BufferBindingLayout
                {
                    Type = BufferBindingType.ReadOnlyStorage,
                    HasDynamicOffset = false,
                    MinBindingSize = 0
                }
            };

            BindGroupLayoutDescriptor layoutDescriptor = new()
            {
                EntryCount = 2,
                Entries = layoutEntries
            };

            this.CompositeBindGroupLayout = this.Api.DeviceCreateBindGroupLayout(this.Device, in layoutDescriptor);
            if (this.CompositeBindGroupLayout is null)
            {
                error = "Failed to create composite bind group layout.";
                return false;
            }

            BindGroupLayout** bindGroupLayouts = stackalloc BindGroupLayout*[1];
            bindGroupLayouts[0] = this.CompositeBindGroupLayout;
            PipelineLayoutDescriptor pipelineLayoutDescriptor = new()
            {
                BindGroupLayoutCount = 1,
                BindGroupLayouts = bindGroupLayouts
            };

            this.compositePipelineLayout = this.Api.DeviceCreatePipelineLayout(this.Device, in pipelineLayoutDescriptor);
            if (this.compositePipelineLayout is null)
            {
                error = "Failed to create composite pipeline layout.";
                return false;
            }

            error = null;
            return true;
        }

        private RenderPipeline* CreateCompositePipelineForFormat(TextureFormat textureFormat)
        {
            if (this.compositePipelineLayout is null)
            {
                return null;
            }

            ShaderModule* shaderModule = null;
            try
            {
                ReadOnlySpan<byte> shaderCode = CompositeCoverageShader.Code;
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

                    shaderModule = this.Api.DeviceCreateShaderModule(this.Device, in shaderDescriptor);
                }

                if (shaderModule is null)
                {
                    return null;
                }

                ReadOnlySpan<byte> vertexEntryPoint = CompositeVertexEntryPoint;
                ReadOnlySpan<byte> fragmentEntryPoint = CompositeFragmentEntryPoint;
                fixed (byte* vertexEntryPointPtr = vertexEntryPoint)
                {
                    fixed (byte* fragmentEntryPointPtr = fragmentEntryPoint)
                    {
                        return this.CreateCompositePipeline(shaderModule, vertexEntryPointPtr, fragmentEntryPointPtr, textureFormat);
                    }
                }
            }
            finally
            {
                if (shaderModule is not null)
                {
                    this.Api.ShaderModuleRelease(shaderModule);
                }
            }
        }

        private RenderPipeline* CreateCompositePipeline(
            ShaderModule* shaderModule,
            byte* vertexEntryPointPtr,
            byte* fragmentEntryPointPtr,
            TextureFormat textureFormat)
        {
            VertexState vertexState = new()
            {
                Module = shaderModule,
                EntryPoint = vertexEntryPointPtr,
                BufferCount = 0,
                Buffers = null
            };

            BlendState blendState = new()
            {
                Color = new BlendComponent
                {
                    Operation = BlendOperation.Add,
                    SrcFactor = BlendFactor.One,
                    DstFactor = BlendFactor.OneMinusSrcAlpha
                },
                Alpha = new BlendComponent
                {
                    Operation = BlendOperation.Add,
                    SrcFactor = BlendFactor.One,
                    DstFactor = BlendFactor.OneMinusSrcAlpha
                }
            };

            ColorTargetState* colorTargets = stackalloc ColorTargetState[1];
            colorTargets[0] = new ColorTargetState
            {
                Format = textureFormat,
                Blend = &blendState,
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
                Layout = this.compositePipelineLayout,
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

        private static void ReleaseCoverageTexture(WebGPU api, CoverageEntry entry)
        {
            if (entry.GPUCoverageView is not null)
            {
                api.TextureViewRelease(entry.GPUCoverageView);
                entry.GPUCoverageView = null;
            }

            if (entry.GPUCoverageTexture is not null)
            {
                api.TextureRelease(entry.GPUCoverageTexture);
                entry.GPUCoverageTexture = null;
            }
        }
    }

    internal sealed class CoverageEntry
    {
        public CoverageEntry(int width, int height)
        {
            this.Width = width;
            this.Height = height;
        }

        public int Width { get; }

        public int Height { get; }

        public Texture* GPUCoverageTexture { get; set; }

        public TextureView* GPUCoverageView { get; set; }
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
