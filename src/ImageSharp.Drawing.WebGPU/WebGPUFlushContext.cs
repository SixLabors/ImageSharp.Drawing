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
    private static readonly ConcurrentDictionary<nint, DeviceSharedState> DeviceStateCache = new();
    private bool disposed;
    private bool ownsTargetTexture;
    private bool ownsTargetView;
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
    /// Creates a flush context for a native WebGPU surface.
    /// Returns <see langword="null"/> when the frame does not expose a native surface
    /// or the device lacks the required feature.
    /// </summary>
    /// <param name="frame">The target frame.</param>
    /// <param name="expectedTextureFormat">The expected GPU texture format.</param>
    /// <param name="requiredFeature">
    /// A device feature required by the pixel type for storage binding, or
    /// <see cref="FeatureName.Undefined"/> when no special feature is needed.
    /// </param>
    /// <param name="memoryAllocator">The memory allocator for staging buffers.</param>
    /// <returns>The flush context, or <see langword="null"/> when GPU execution is unavailable.</returns>
    public static WebGPUFlushContext? Create<TPixel>(
        ICanvasFrame<TPixel> frame,
        TextureFormat expectedTextureFormat,
        FeatureName requiredFeature,
        MemoryAllocator memoryAllocator)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        WebGPUSurfaceCapability? nativeCapability = TryGetNativeSurfaceCapability(frame, expectedTextureFormat);
        if (nativeCapability is null)
        {
            return null;
        }

        WebGPURuntime.Lease lease = WebGPURuntime.Acquire();
        try
        {
            Device* device = (Device*)nativeCapability.Device;
            Queue* queue = (Queue*)nativeCapability.Queue;
            TextureFormat textureFormat = WebGPUTextureFormatMapper.ToSilk(nativeCapability.TargetFormat);
            Rectangle bounds = new(0, 0, nativeCapability.Width, nativeCapability.Height);
            DeviceSharedState deviceState = GetOrCreateDeviceState(lease.Api, device);

            if (requiredFeature != FeatureName.Undefined && !deviceState.HasFeature(requiredFeature))
            {
                lease.Dispose();
                return null;
            }

            WebGPUFlushContext context = new(lease, device, queue, in bounds, textureFormat, memoryAllocator, deviceState);
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

        this.TargetView = null;
        this.TargetTexture = null;
        this.ownsTargetView = false;
        this.ownsTargetTexture = false;

        this.RuntimeLease.Dispose();
        this.disposed = true;
    }

    internal static DeviceSharedState GetOrCreateDeviceState(WebGPU api, Device* device)
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

    private void InitializeNativeTarget(WebGPUSurfaceCapability capability)
    {
        this.TargetTexture = (Texture*)capability.TargetTexture;
        this.TargetView = (TextureView*)capability.TargetTextureView;
        this.ownsTargetTexture = false;
        this.ownsTargetView = false;
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

        if (sourceRegion.Buffer.DangerousTryGetSingleMemory(out Memory<TPixel> sourceMemory))
        {
            int sourceStrideBytes = checked(sourceRegion.Buffer.RowStride * pixelSizeInBytes);
            long directByteCount = ((long)sourceStrideBytes * (sourceRegion.Height - 1)) + rowBytes;
            long packedByteCountEstimate = alignedRowBytes * sourceRegion.Height;

            // Only use the direct path when the stride satisfies WebGPU's alignment requirement.
            if ((uint)sourceStrideBytes == alignedRowBytes && directByteCount <= packedByteCountEstimate * 2)
            {
                int startPixelIndex = checked((sourceRegion.Rectangle.Y * sourceRegion.Buffer.RowStride) + sourceRegion.Rectangle.X);
                int startByteOffset = checked(startPixelIndex * pixelSizeInBytes);
                int uploadByteCount = checked((int)directByteCount);
                nuint uploadByteCountNuint = checked((nuint)uploadByteCount);

                TextureDataLayout layout = new()
                {
                    Offset = 0,
                    BytesPerRow = (uint)sourceStrideBytes,
                    RowsPerImage = (uint)sourceRegion.Height
                };

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
        using IMemoryOwner<byte> packedOwner = memoryAllocator.Allocate<byte>(packedByteCount, AllocationOptions.Clean);
        Span<byte> packedData = packedOwner.Memory.Span;
        for (int y = 0; y < sourceRegion.Height; y++)
        {
            ReadOnlySpan<TPixel> sourceRow = sourceRegion.DangerousGetRowSpan(y);
            MemoryMarshal.AsBytes(sourceRow)[..rowBytes].CopyTo(packedData.Slice(y * alignedRowBytesInt, rowBytes));
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
        private readonly ConcurrentDictionary<string, CompositePipelineInfrastructure> compositePipelines = new(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, CompositeComputePipelineInfrastructure> compositeComputePipelines = new(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, SharedBufferInfrastructure> sharedBuffers = new(StringComparer.Ordinal);
        private readonly HashSet<FeatureName> deviceFeatures;
        private bool disposed;

        internal DeviceSharedState(WebGPU api, Device* device)
        {
            this.Api = api;
            this.Device = device;
            this.deviceFeatures = EnumerateDeviceFeatures(api, device);
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
        /// Returns whether the device has the specified feature.
        /// </summary>
        /// <param name="feature">The feature to check.</param>
        /// <returns><see langword="true"/> when the device has the feature; otherwise <see langword="false"/>.</returns>
        public bool HasFeature(FeatureName feature)
            => this.deviceFeatures.Contains(feature);

        private static HashSet<FeatureName> EnumerateDeviceFeatures(WebGPU api, Device* device)
        {
            if (device is null)
            {
                return [];
            }

            int count = (int)api.DeviceEnumerateFeatures(device, (FeatureName*)null);
            if (count <= 0)
            {
                return [];
            }

            FeatureName* features = stackalloc FeatureName[count];
            api.DeviceEnumerateFeatures(device, features);

            HashSet<FeatureName> result = new(count);
            for (int i = 0; i < count; i++)
            {
                result.Add(features[i]);
            }

            return result;
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
}
