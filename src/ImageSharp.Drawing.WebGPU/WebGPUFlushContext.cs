// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SixLabors.ImageSharp.Drawing.Processing.Backends.Native;
using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.PixelFormats;

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
    // Transient GPU objects created during this flush, stored as nint because pointer types
    // cannot be list type arguments. All of them are released in Dispose after the open
    // passes end, so command encoding never references a freed object.
    private readonly List<nint> transientBindGroups = [];
    private readonly List<nint> transientBuffers = [];
    private readonly List<nint> transientSamplers = [];
    private readonly List<nint> transientTextureViews = [];
    private readonly List<nint> transientTextures = [];

    // Flush-scoped source image cache:
    // key = source Image reference, value = uploaded texture view handle.
    // Handles are released when this flush context is disposed.
    private readonly Dictionary<Image, nint> cachedSourceTextureViews = new(ReferenceEqualityComparer.Instance);

    // Device-pooled resources rented for this flush. Unlike the transient lists above these are
    // returned to the device pool at disposal instead of released, so the next flush can reuse
    // the same native objects without recreation or lazy zero-initialization.
    private readonly List<PooledTextureRental> pooledTextures = [];
    private readonly List<PooledBufferRental> pooledUniformBuffers = [];

    // One gradient-ramp texture serves every staged range of the flush's encoded scene. The view
    // is owned through pooledTextures; this cache only avoids duplicate staging.
    private object? cachedGradientScene;
    private nint cachedGradientTextureView;

    private bool disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebGPUFlushContext"/> class.
    /// Use <see cref="Create{TPixel}"/>; this constructor only stores already-validated state.
    /// </summary>
    /// <param name="api">The WebGPU API facade for this flush.</param>
    /// <param name="deviceHandle">The safe handle for the device.</param>
    /// <param name="queueHandle">The safe handle for the queue.</param>
    /// <param name="targetTextureHandle">The safe handle for the target texture.</param>
    /// <param name="targetTextureViewHandle">The safe handle for the target texture view.</param>
    /// <param name="targetBounds">The target bounds for this flush.</param>
    /// <param name="targetTextureOffset">The offset from logical target coordinates to texture coordinates.</param>
    /// <param name="targetDescriptor">The target texture format and alpha representation.</param>
    /// <param name="textureFormat">The native target texture format.</param>
    /// <param name="isPresentationSurface">Whether the target texture belongs to a presentation surface.</param>
    /// <param name="requiresPresentationCopies">Whether the target must be copied through an ImageSharp-owned texture before it can be sampled or storage-bound.</param>
    /// <param name="memoryAllocator">The allocator for temporary CPU staging buffers.</param>
    /// <param name="deviceState">The device-scoped shared caches and reusable resources.</param>
    /// <param name="scratchBufferBindingSizeLimit">The backend-specific upper bound for staged-scene storage bindings.</param>
    private WebGPUFlushContext(
        WebGPU api,
        WebGPUDeviceHandle deviceHandle,
        WebGPUQueueHandle queueHandle,
        WebGPUTextureHandle targetTextureHandle,
        WebGPUTextureViewHandle targetTextureViewHandle,
        in Rectangle targetBounds,
        Point targetTextureOffset,
        WebGPUTargetDescriptor targetDescriptor,
        WGPUTextureFormat textureFormat,
        bool isPresentationSurface,
        bool requiresPresentationCopies,
        MemoryAllocator memoryAllocator,
        WebGPURuntime.DeviceSharedState deviceState,
        nuint scratchBufferBindingSizeLimit)
    {
        this.Api = api;
        this.DeviceHandle = deviceHandle;
        this.QueueHandle = queueHandle;
        this.TargetTextureHandle = targetTextureHandle;
        this.TargetTextureViewHandle = targetTextureViewHandle;
        this.TargetBounds = targetBounds;
        this.TargetTextureOffset = targetTextureOffset;
        this.TargetDescriptor = targetDescriptor;
        this.TextureFormat = textureFormat;
        this.IsPresentationSurface = isPresentationSurface;
        this.RequiresPresentationCopies = requiresPresentationCopies;
        this.MemoryAllocator = memoryAllocator;
        this.DeviceState = deviceState;

        // The device owns the real API limit. A backend instance may only make that limit more
        // restrictive, which lets integration tests exercise chunking without mutating shared state.
        this.ScratchBufferBindingSizeLimit = Math.Min(deviceState.MaxStorageBufferBindingSize, scratchBufferBindingSizeLimit);
    }

    /// <summary>
    /// Gets the WebGPU API facade for this flush.
    /// </summary>
    public WebGPU Api { get; }

    /// <summary>
    /// Gets the safe handle for the device used to create and execute GPU resources.
    /// Acquire a scoped reference with <see cref="WebGPUHandle.AcquireReference"/> for the
    /// duration of any native call that uses the underlying pointer.
    /// </summary>
    public WebGPUDeviceHandle DeviceHandle { get; }

    /// <summary>
    /// Gets the safe handle for the queue used to submit GPU work.
    /// Acquire a scoped reference with <see cref="WebGPUHandle.AcquireReference"/> for the
    /// duration of any native call that uses the underlying pointer.
    /// </summary>
    public WebGPUQueueHandle QueueHandle { get; }

    /// <summary>
    /// Gets the safe handle for the target texture receiving render/composite output.
    /// Acquire a scoped reference with <see cref="WebGPUHandle.AcquireReference"/> for the
    /// duration of any native call that uses the underlying pointer.
    /// </summary>
    public WebGPUTextureHandle TargetTextureHandle { get; }

    /// <summary>
    /// Gets the safe handle for the texture view used when binding the target texture.
    /// Acquire a scoped reference with <see cref="WebGPUHandle.AcquireReference"/> for the
    /// duration of any native call that uses the underlying pointer.
    /// </summary>
    public WebGPUTextureViewHandle TargetTextureViewHandle { get; }

    /// <summary>
    /// Gets the target bounds for this flush context.
    /// </summary>
    public Rectangle TargetBounds { get; }

    /// <summary>
    /// Gets the offset applied when mapping logical target coordinates to target texture coordinates.
    /// </summary>
    public Point TargetTextureOffset { get; }

    /// <summary>
    /// Gets the target texture format and alpha representation for this flush.
    /// </summary>
    public WebGPUTargetDescriptor TargetDescriptor { get; }

    /// <summary>
    /// Gets the target texture format for this flush.
    /// </summary>
    public WGPUTextureFormat TextureFormat { get; }

    /// <summary>
    /// Gets a value indicating whether the target texture belongs to a presentation surface.
    /// </summary>
    public bool IsPresentationSurface { get; }

    /// <summary>
    /// Gets a value indicating whether the target must be copied through an ImageSharp-owned texture before it can be sampled or storage-bound.
    /// </summary>
    public bool RequiresPresentationCopies { get; }

    /// <summary>
    /// Gets the allocator used for temporary CPU staging buffers in this flush context.
    /// </summary>
    public MemoryAllocator MemoryAllocator { get; }

    /// <summary>
    /// Gets device-scoped shared caches and reusable resources.
    /// </summary>
    public WebGPURuntime.DeviceSharedState DeviceState { get; }

    /// <summary>
    /// Gets the effective storage-binding ceiling for staged-scene scratch buffers.
    /// </summary>
    public nuint ScratchBufferBindingSizeLimit { get; }

    /// <summary>
    /// Gets the shared instance-data buffer used for parameter uploads.
    /// </summary>
    public WGPUBufferImpl* InstanceBuffer { get; private set; }

    /// <summary>
    /// Gets the instance buffer capacity in bytes.
    /// </summary>
    public nuint InstanceBufferCapacity { get; private set; }

    /// <summary>
    /// Gets the current write offset into <see cref="InstanceBuffer"/>.
    /// </summary>
    public nuint InstanceBufferWriteOffset { get; private set; }

    /// <summary>
    /// Gets or sets the active command encoder.
    /// </summary>
    public WGPUCommandEncoderImpl* CommandEncoder { get; set; }

    /// <summary>
    /// Gets the currently open render pass encoder, if any.
    /// </summary>
    public WGPURenderPassEncoderImpl* PassEncoder { get; private set; }

    /// <summary>
    /// Gets the currently open compute pass encoder, if any.
    /// </summary>
    public WGPUComputePassEncoderImpl* ComputePassEncoder { get; private set; }

    /// <summary>
    /// Creates a flush context for a native WebGPU surface.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format of the target frame.</typeparam>
    /// <param name="frame">The target frame.</param>
    /// <param name="expectedTargetDescriptor">The expected GPU texture format and alpha representation.</param>
    /// <param name="requiredFeature">
    /// A device feature required by the pixel type for storage binding, or
    /// the default value when no special feature is needed.
    /// </param>
    /// <param name="memoryAllocator">The memory allocator for staging buffers.</param>
    /// <param name="scratchBufferBindingSizeLimit">The backend-specific upper bound for staged-scene storage bindings.</param>
    /// <returns>The flush context.</returns>
    public static WebGPUFlushContext Create<TPixel>(
        NativeCanvasFrame<TPixel> frame,
        WebGPUTargetDescriptor expectedTargetDescriptor,
        WGPUFeatureName requiredFeature,
        MemoryAllocator memoryAllocator,
        nuint scratchBufferBindingSizeLimit)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        // The native-frame overload is used after WebGPU target selection has already succeeded,
        // so this casts once at the backend boundary and keeps the concrete target data together.
        _ = frame.TryGetNativeSurface(out NativeSurface? nativeSurface);
        return Create(
            (WebGPUNativeSurface)nativeSurface!,
            frame.Bounds,
            expectedTargetDescriptor,
            requiredFeature,
            memoryAllocator,
            scratchBufferBindingSizeLimit);
    }

    /// <summary>
    /// Creates a flush context directly over a native WebGPU surface and target bounds. Used by
    /// corrective re-renders, which outlive the per-flush canvas frame and therefore cannot go
    /// through the frame overload.
    /// </summary>
    /// <param name="nativeTarget">The native surface holding the target texture handles.</param>
    /// <param name="bounds">The target bounds for the flush.</param>
    /// <param name="expectedTargetDescriptor">The expected GPU texture format and alpha representation.</param>
    /// <param name="requiredFeature">
    /// A device feature required by the pixel type for storage binding, or
    /// the default value when no special feature is needed.
    /// </param>
    /// <param name="memoryAllocator">The memory allocator for staging buffers.</param>
    /// <param name="scratchBufferBindingSizeLimit">The backend-specific upper bound for staged-scene storage bindings.</param>
    /// <returns>The flush context.</returns>
    public static WebGPUFlushContext Create(
        WebGPUNativeSurface nativeTarget,
        Rectangle bounds,
        WebGPUTargetDescriptor expectedTargetDescriptor,
        WGPUFeatureName requiredFeature,
        MemoryAllocator memoryAllocator,
        nuint scratchBufferBindingSizeLimit)
    {
        WebGPU api = WebGPURuntime.GetApi();
        WebGPUDrawingBackend.GetCompositeTextureFormatInfo(nativeTarget.TargetDescriptor.Format, out WGPUTextureFormat textureFormat, out _);
        Rectangle nativeBounds = new(0, 0, nativeTarget.Width, nativeTarget.Height);
        Point targetTextureOffset = nativeTarget.TextureCoordinateOffset;
        Rectangle textureBounds = new(
            bounds.X + targetTextureOffset.X,
            bounds.Y + targetTextureOffset.Y,
            bounds.Width,
            bounds.Height);

        WebGPURuntime.DeviceSharedState deviceState = WebGPURuntime.GetOrCreateDeviceState(api, nativeTarget.DeviceHandle);

        if (nativeTarget.DeviceHandle.IsInvalid ||
            nativeTarget.QueueHandle.IsInvalid ||
            nativeTarget.TargetTextureViewHandle.IsInvalid ||
            nativeTarget.TargetDescriptor != expectedTargetDescriptor)
        {
            throw new InvalidOperationException("The native WebGPU target does not match the flush context requirements.");
        }

        if (requiredFeature != default && !deviceState.HasFeature(requiredFeature))
        {
            throw new NotSupportedException($"The WebGPU device does not support required feature '{requiredFeature}'.");
        }

        // Region frames expose bounds relative to their parent target. The flush context must preserve
        // that absolute slice so later scene encoding, dispatch planning, and texture copies address
        // the correct sub-rectangle of the native surface instead of silently expanding back to full-frame.
        if (!nativeBounds.Contains(textureBounds))
        {
            throw new InvalidOperationException("The native WebGPU target bounds do not contain the flush bounds.");
        }

        return new WebGPUFlushContext(
            api,
            nativeTarget.DeviceHandle,
            nativeTarget.QueueHandle,
            nativeTarget.TargetTextureHandle,
            nativeTarget.TargetTextureViewHandle,
            in bounds,
            targetTextureOffset,
            nativeTarget.TargetDescriptor,
            textureFormat,
            nativeTarget.IsPresentationSurface,
            nativeTarget.RequiresPresentationCopies,
            memoryAllocator,
            deviceState,
            scratchBufferBindingSizeLimit);
    }

    /// <summary>
    /// Ensures that the instance buffer exists and can hold at least the requested number of bytes.
    /// </summary>
    /// <param name="requiredBytes">The required number of bytes for the current flush.</param>
    /// <param name="minimumCapacityBytes">The minimum allocation size to enforce when creating a new buffer.</param>
    /// <remarks>
    /// Growing replaces the buffer; previous contents are discarded, so callers must re-upload
    /// any data they still need.
    /// </remarks>
    public void EnsureInstanceBufferCapacity(nuint requiredBytes, nuint minimumCapacityBytes)
    {
        if (this.InstanceBuffer is not null && this.InstanceBufferCapacity >= requiredBytes)
        {
            return;
        }

        if (this.InstanceBuffer is not null)
        {
            this.Api.BufferRelease(this.InstanceBuffer);
            this.InstanceBuffer = null;
            this.InstanceBufferCapacity = 0;
        }

        nuint targetSize = requiredBytes > minimumCapacityBytes ? requiredBytes : minimumCapacityBytes;
        WGPUBufferDescriptor descriptor = new()
        {
            usage = (ulong)(BufferUsage.Storage | BufferUsage.CopyDst),
            size = targetSize
        };

        using WebGPUHandle.HandleReference deviceReference = this.DeviceHandle.AcquireReference();
        this.InstanceBuffer = this.Api.DeviceCreateBuffer((WGPUDeviceImpl*)deviceReference.Handle, in descriptor);
        this.InstanceBufferCapacity = targetSize;
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

        WGPUCommandEncoderDescriptor descriptor = default;
        using WebGPUHandle.HandleReference deviceReference = this.DeviceHandle.AcquireReference();
        this.CommandEncoder = this.Api.DeviceCreateCommandEncoder((WGPUDeviceImpl*)deviceReference.Handle, in descriptor);
        return this.CommandEncoder is not null;
    }

    /// <summary>
    /// Begins a render pass that targets the specified texture view, clearing existing contents.
    /// </summary>
    /// <param name="targetView">The texture view that receives the pass output.</param>
    /// <returns><see langword="true"/> if a render pass is open; otherwise <see langword="false"/>.</returns>
    public bool BeginRenderPass(WGPUTextureViewImpl* targetView)
        => this.BeginRenderPass(targetView, loadExisting: false);

    /// <summary>
    /// Begins a render pass that targets the specified texture view, optionally preserving existing contents.
    /// </summary>
    /// <param name="targetView">The texture view that receives the pass output.</param>
    /// <param name="loadExisting"><see langword="true"/> to load the existing texture contents; <see langword="false"/> to clear.</param>
    /// <returns><see langword="true"/> if a render pass is open; otherwise <see langword="false"/>.</returns>
    /// <remarks>
    /// When a render pass is already open it is reused as-is; <paramref name="targetView"/> and
    /// <paramref name="loadExisting"/> are ignored in that case. Fails when no command encoder
    /// exists or a compute pass is open, because passes cannot be nested on one encoder.
    /// </remarks>
    public bool BeginRenderPass(WGPUTextureViewImpl* targetView, bool loadExisting)
        => this.BeginRenderPass(targetView, this.TargetDescriptor, loadExisting);

    /// <summary>
    /// Begins a render pass using the physical encoding of the supplied target representation.
    /// </summary>
    /// <param name="targetView">The texture view that receives the pass output.</param>
    /// <param name="targetDescriptor">The representation stored by <paramref name="targetView"/>.</param>
    /// <param name="loadExisting"><see langword="true"/> to load existing contents; <see langword="false"/> to clear.</param>
    /// <returns><see langword="true"/> if a render pass is open; otherwise <see langword="false"/>.</returns>
    public bool BeginRenderPass(WGPUTextureViewImpl* targetView, WebGPUTargetDescriptor targetDescriptor, bool loadExisting)
    {
        if (this.PassEncoder is not null)
        {
            return true;
        }

        if (this.CommandEncoder is null || targetView is null || this.ComputePassEncoder is not null)
        {
            return false;
        }

        WGPURenderPassColorAttachment colorAttachment = new()
        {
            view = targetView,

            // A 2D attachment must use WebGPU's undefined sentinel; zero selects slice 0 and is valid only for 3D views.
            depthSlice = uint.MaxValue,
            resolveTarget = null,
            loadOp = loadExisting ? WGPULoadOp.Load : WGPULoadOp.Clear,
            storeOp = WGPUStoreOp.Store,

            // WebGPU clear values use the attachment's physical encoding. Physical -1 is the
            // logical transparent zero for ImageSharp formats mapped through a signed-unit target.
            clearValue = targetDescriptor.NumericEncoding == WebGPUTargetNumericEncoding.SignedUnit
                ? new WGPUColor { r = -1D, g = -1D, b = -1D, a = -1D }
                : default
        };

        WGPURenderPassDescriptor renderPassDescriptor = new()
        {
            colorAttachmentCount = 1,
            colorAttachments = &colorAttachment
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
    /// Begins a compute pass on the current command encoder.
    /// </summary>
    /// <returns><see langword="true"/> if a compute pass is available; otherwise <see langword="false"/>.</returns>
    public bool BeginComputePass()
    {
        if (this.ComputePassEncoder is not null)
        {
            return true;
        }

        if (this.CommandEncoder is null || this.PassEncoder is not null)
        {
            return false;
        }

        WGPUComputePassDescriptor descriptor = default;
        this.ComputePassEncoder = this.Api.CommandEncoderBeginComputePass(this.CommandEncoder, in descriptor);
        return this.ComputePassEncoder is not null;
    }

    /// <summary>
    /// Ends and releases the current compute pass if one is active.
    /// </summary>
    public void EndComputePassIfOpen()
    {
        if (this.ComputePassEncoder is null)
        {
            return;
        }

        this.Api.ComputePassEncoderEnd(this.ComputePassEncoder);
        this.Api.ComputePassEncoderRelease(this.ComputePassEncoder);
        this.ComputePassEncoder = null;
    }

    /// <summary>
    /// Tracks a transient bind group allocated during this flush.
    /// </summary>
    /// <param name="bindGroup">The bind group to track.</param>
    public void TrackBindGroup(WGPUBindGroupImpl* bindGroup)
    {
        if (bindGroup is not null)
        {
            this.transientBindGroups.Add((nint)bindGroup);
        }
    }

    /// <summary>
    /// Tracks a transient buffer allocated during this flush.
    /// </summary>
    /// <param name="buffer">The buffer to track.</param>
    public void TrackBuffer(WGPUBufferImpl* buffer)
    {
        if (buffer is not null)
        {
            this.transientBuffers.Add((nint)buffer);
        }
    }

    /// <summary>
    /// Tracks a transient sampler allocated during this flush.
    /// </summary>
    /// <param name="sampler">The sampler to track.</param>
    public void TrackSampler(WGPUSamplerImpl* sampler)
    {
        if (sampler is not null)
        {
            this.transientSamplers.Add((nint)sampler);
        }
    }

    /// <summary>
    /// Tracks a transient texture view allocated during this flush.
    /// </summary>
    /// <param name="textureView">The texture view to track.</param>
    public void TrackTextureView(WGPUTextureViewImpl* textureView)
    {
        if (textureView is not null)
        {
            this.transientTextureViews.Add((nint)textureView);
        }
    }

    /// <summary>
    /// Tracks a transient texture allocated during this flush.
    /// </summary>
    /// <param name="texture">The texture to track.</param>
    public void TrackTexture(WGPUTextureImpl* texture)
    {
        if (texture is not null)
        {
            this.transientTextures.Add((nint)texture);
        }
    }

    /// <summary>
    /// Tracks a device-pooled texture rented for this flush. It is returned to the device pool,
    /// not released, when this flush context is disposed.
    /// </summary>
    /// <param name="texture">The rented texture.</param>
    /// <param name="textureView">The full view rented or created with <paramref name="texture"/>.</param>
    /// <param name="format">The format the texture was created with.</param>
    /// <param name="usage">The exact usage bits the texture was created with.</param>
    /// <param name="width">The created width in texels.</param>
    /// <param name="height">The created height in texels.</param>
    public void TrackPooledTexture(
        WGPUTextureImpl* texture,
        WGPUTextureViewImpl* textureView,
        WGPUTextureFormat format,
        ulong usage,
        uint width,
        uint height)
    {
        if (texture is not null)
        {
            this.pooledTextures.Add(new PooledTextureRental((nint)texture, (nint)textureView, format, usage, width, height));
        }
    }

    /// <summary>
    /// Tracks a device-pooled layer-effect uniform buffer rented for this flush. It is returned
    /// to the device pool, not released, when this flush context is disposed.
    /// </summary>
    /// <param name="buffer">The rented buffer.</param>
    /// <param name="byteLength">The byte capacity of <paramref name="buffer"/>.</param>
    public void TrackPooledUniformBuffer(WGPUBufferImpl* buffer, nuint byteLength)
    {
        if (buffer is not null)
        {
            this.pooledUniformBuffers.Add(new PooledBufferRental((nint)buffer, byteLength));
        }
    }

    /// <summary>
    /// Tries to resolve a cached source texture view for an input image.
    /// </summary>
    /// <param name="image">The source image key.</param>
    /// <param name="textureView">When this method returns <see langword="true"/>, contains the cached texture view.</param>
    /// <returns><see langword="true"/> if a cached texture view exists; otherwise <see langword="false"/>.</returns>
    public bool TryGetCachedSourceTextureView(Image image, out WGPUTextureViewImpl* textureView)
    {
        if (this.cachedSourceTextureViews.TryGetValue(image, out nint handle) && handle != 0)
        {
            textureView = (WGPUTextureViewImpl*)handle;
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
    public void CacheSourceTextureView(Image image, WGPUTextureViewImpl* textureView)
        => this.cachedSourceTextureViews[image] = (nint)textureView;

    /// <summary>
    /// Tries to resolve the uploaded gradient-ramp texture view for the flush's encoded scene.
    /// </summary>
    /// <param name="scene">The encoded scene whose gradient rows were uploaded.</param>
    /// <param name="textureView">When this method returns <see langword="true"/>, contains the cached view.</param>
    /// <returns><see langword="true"/> when the scene's gradient texture was already staged this flush.</returns>
    public bool TryGetCachedGradientTextureView(object scene, out WGPUTextureViewImpl* textureView)
    {
        if (ReferenceEquals(this.cachedGradientScene, scene) && this.cachedGradientTextureView != 0)
        {
            textureView = (WGPUTextureViewImpl*)this.cachedGradientTextureView;
            return true;
        }

        textureView = null;
        return false;
    }

    /// <summary>
    /// Caches the uploaded gradient-ramp texture view so later ranges of the same scene bind it
    /// without creating and uploading another texture.
    /// </summary>
    /// <param name="scene">The encoded scene whose gradient rows were uploaded.</param>
    /// <param name="textureView">The staged gradient texture view.</param>
    public void CacheGradientTextureView(object scene, WGPUTextureViewImpl* textureView)
    {
        this.cachedGradientScene = scene;
        this.cachedGradientTextureView = (nint)textureView;
    }

    /// <summary>
    /// Releases transient GPU resources owned by this flush context.
    /// </summary>
    public void Dispose()
    {
        if (this.disposed)
        {
            return;
        }

        // The flag is set before any release so a throw mid-teardown cannot leave a
        // re-runnable context: a second Dispose over the still-populated lists would
        // release every tracked object twice and double-return pooled rentals.
        this.disposed = true;

        // Ordering: end any open passes before releasing the encoder they were recorded on,
        // then release the encoder before the transient objects it may still reference.
        this.EndComputePassIfOpen();
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

        for (int i = 0; i < this.transientBindGroups.Count; i++)
        {
            this.Api.BindGroupRelease((WGPUBindGroupImpl*)this.transientBindGroups[i]);
        }

        for (int i = 0; i < this.transientBuffers.Count; i++)
        {
            this.Api.BufferRelease((WGPUBufferImpl*)this.transientBuffers[i]);
        }

        for (int i = 0; i < this.transientSamplers.Count; i++)
        {
            this.Api.SamplerRelease((WGPUSamplerImpl*)this.transientSamplers[i]);
        }

        for (int i = 0; i < this.transientTextureViews.Count; i++)
        {
            this.Api.TextureViewRelease((WGPUTextureViewImpl*)this.transientTextureViews[i]);
        }

        for (int i = 0; i < this.transientTextures.Count; i++)
        {
            this.Api.TextureRelease((WGPUTextureImpl*)this.transientTextures[i]);
        }

        this.transientBindGroups.Clear();
        this.transientBuffers.Clear();
        this.transientSamplers.Clear();
        this.transientTextureViews.Clear();
        this.transientTextures.Clear();

        // Cache entries point to transient texture views that are released above.
        this.cachedSourceTextureViews.Clear();

        // Rented pooled resources go back to the device pool after the commands that used them
        // were submitted; queue ordering makes reuse by a later flush safe, and the pool itself
        // releases entries once its retention bound is reached.
        for (int i = 0; i < this.pooledTextures.Count; i++)
        {
            PooledTextureRental rental = this.pooledTextures[i];
            this.DeviceState.ReturnPooledTexture(
                (WGPUTextureImpl*)rental.Texture,
                (WGPUTextureViewImpl*)rental.View,
                rental.Format,
                rental.Usage,
                rental.Width,
                rental.Height);
        }

        for (int i = 0; i < this.pooledUniformBuffers.Count; i++)
        {
            PooledBufferRental rental = this.pooledUniformBuffers[i];
            this.DeviceState.ReturnEffectUniformBuffer((WGPUBufferImpl*)rental.Buffer, rental.ByteLength);
        }

        this.pooledTextures.Clear();
        this.pooledUniformBuffers.Clear();
    }

    /// <summary>
    /// Uploads a source region into the destination texture starting at the origin.
    /// </summary>
    /// <typeparam name="TPixel">The pixel type stored in the source region.</typeparam>
    /// <param name="api">The WebGPU API used for the upload.</param>
    /// <param name="queue">The queue that receives the upload commands.</param>
    /// <param name="destinationTexture">The destination texture.</param>
    /// <param name="sourceRegion">The CPU-side source region to upload.</param>
    /// <param name="memoryAllocator">The allocator used when a packed staging copy is required.</param>
    public static void UploadTextureFromRegion<TPixel>(
        WebGPU api,
        WGPUQueueImpl* queue,
        WGPUTextureImpl* destinationTexture,
        Buffer2DRegion<TPixel> sourceRegion,
        MemoryAllocator memoryAllocator)
        where TPixel : unmanaged
        => UploadTextureFromRegion(api, queue, destinationTexture, sourceRegion, memoryAllocator, 0, 0, 0);

    /// <summary>
    /// Uploads a source region into a destination texture subregion.
    /// </summary>
    /// <typeparam name="TPixel">The pixel type stored in the source region.</typeparam>
    /// <param name="api">The WebGPU API used for the upload.</param>
    /// <param name="queue">The queue that receives the upload commands.</param>
    /// <param name="destinationTexture">The destination texture.</param>
    /// <param name="sourceRegion">The CPU-side source region to upload.</param>
    /// <param name="memoryAllocator">The allocator used when a packed staging copy is required.</param>
    /// <param name="destinationX">The destination X coordinate in the texture.</param>
    /// <param name="destinationY">The destination Y coordinate in the texture.</param>
    /// <param name="destinationLayer">The destination array layer.</param>
    public static void UploadTextureFromRegion<TPixel>(
        WebGPU api,
        WGPUQueueImpl* queue,
        WGPUTextureImpl* destinationTexture,
        Buffer2DRegion<TPixel> sourceRegion,
        MemoryAllocator memoryAllocator,
        uint destinationX,
        uint destinationY,
        uint destinationLayer)
        where TPixel : unmanaged
    {
        int pixelSizeInBytes = Unsafe.SizeOf<TPixel>();
        WGPUTexelCopyTextureInfo destination = new()
        {
            texture = destinationTexture,
            mipLevel = 0,
            origin = new WGPUOrigin3D(destinationX, destinationY, destinationLayer),
            aspect = WGPUTextureAspect.All
        };

        WGPUExtent3D writeSize = new((uint)sourceRegion.Width, (uint)sourceRegion.Height, 1);
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
                int startPixelIndex = checked((sourceRegion.Bounds.Y * sourceRegion.Buffer.RowStride) + sourceRegion.Bounds.X);
                int startByteOffset = checked(startPixelIndex * pixelSizeInBytes);
                int uploadByteCount = checked((int)directByteCount);
                nuint uploadByteCountNuint = checked((nuint)uploadByteCount);

                WGPUTexelCopyBufferLayout layout = new()
                {
                    offset = 0,
                    bytesPerRow = (uint)sourceStrideBytes,
                    rowsPerImage = (uint)sourceRegion.Height
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

        WGPUTexelCopyBufferLayout packedLayout = new()
        {
            offset = 0,
            bytesPerRow = alignedRowBytes,
            rowsPerImage = (uint)sourceRegion.Height
        };

        fixed (byte* uploadPtr = packedData)
        {
            api.QueueWriteTexture(queue, in destination, uploadPtr, (nuint)packedByteCount, in packedLayout, in writeSize);
        }
    }

    /// <summary>
    /// Aligns a byte count to WebGPU's 256-byte row-upload requirement.
    /// </summary>
    /// <param name="value">The byte count to align.</param>
    /// <returns>The aligned byte count.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint AlignTo256(uint value) => (value + 255U) & ~255U;

    /// <summary>
    /// Stores one rented device-pooled texture with the creation parameters its return requires.
    /// </summary>
    private readonly struct PooledTextureRental
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="PooledTextureRental"/> struct.
        /// </summary>
        /// <param name="texture">The native texture pointer stored as an integer.</param>
        /// <param name="view">The native full-texture view pointer stored as an integer.</param>
        /// <param name="format">The format the texture was created with.</param>
        /// <param name="usage">The exact usage bits the texture was created with.</param>
        /// <param name="width">The created width in texels.</param>
        /// <param name="height">The created height in texels.</param>
        public PooledTextureRental(nint texture, nint view, WGPUTextureFormat format, ulong usage, uint width, uint height)
        {
            this.Texture = texture;
            this.View = view;
            this.Format = format;
            this.Usage = usage;
            this.Width = width;
            this.Height = height;
        }

        /// <summary>
        /// Gets the native texture pointer stored as an integer.
        /// </summary>
        public nint Texture { get; }

        /// <summary>
        /// Gets the native full-texture view pointer stored as an integer.
        /// </summary>
        public nint View { get; }

        /// <summary>
        /// Gets the format the texture was created with.
        /// </summary>
        public WGPUTextureFormat Format { get; }

        /// <summary>
        /// Gets the exact usage bits the texture was created with.
        /// </summary>
        public ulong Usage { get; }

        /// <summary>
        /// Gets the created width in texels.
        /// </summary>
        public uint Width { get; }

        /// <summary>
        /// Gets the created height in texels.
        /// </summary>
        public uint Height { get; }
    }

    /// <summary>
    /// Stores one rented device-pooled buffer with its capacity.
    /// </summary>
    private readonly struct PooledBufferRental
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="PooledBufferRental"/> struct.
        /// </summary>
        /// <param name="buffer">The native buffer pointer stored as an integer.</param>
        /// <param name="byteLength">The byte capacity of <paramref name="buffer"/>.</param>
        public PooledBufferRental(nint buffer, nuint byteLength)
        {
            this.Buffer = buffer;
            this.ByteLength = byteLength;
        }

        /// <summary>
        /// Gets the native buffer pointer stored as an integer.
        /// </summary>
        public nint Buffer { get; }

        /// <summary>
        /// Gets the byte capacity of <see cref="Buffer"/>.
        /// </summary>
        public nuint ByteLength { get; }
    }
}
