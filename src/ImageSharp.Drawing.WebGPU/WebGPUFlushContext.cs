// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Buffers;
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
        WebGPURuntime.DeviceSharedState deviceState)
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
    public WebGPURuntime.DeviceSharedState DeviceState { get; }

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
    /// Gets the currently open compute pass encoder, if any.
    /// </summary>
    public ComputePassEncoder* ComputePassEncoder { get; private set; }

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
            WebGPURuntime.DeviceSharedState deviceState = WebGPURuntime.GetOrCreateDeviceState(lease.Api, device);

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

        if (this.CommandEncoder is null || targetView is null || this.ComputePassEncoder is not null)
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

        ComputePassDescriptor descriptor = default;
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

    /// <summary>
    /// Adopts the texture and texture view provided by a native WebGPU surface capability.
    /// </summary>
    /// <param name="capability">The native surface capability describing the externally owned target.</param>
    private void InitializeNativeTarget(WebGPUSurfaceCapability capability)
    {
        this.TargetTexture = (Texture*)capability.TargetTexture;
        this.TargetView = (TextureView*)capability.TargetTextureView;
        this.ownsTargetTexture = false;
        this.ownsTargetView = false;
    }

    /// <summary>
    /// Tries to obtain a native WebGPU surface capability from the canvas frame.
    /// </summary>
    /// <typeparam name="TPixel">The pixel type of the canvas frame.</typeparam>
    /// <param name="frame">The frame being flushed.</param>
    /// <param name="expectedTextureFormat">The texture format required by the current WebGPU path.</param>
    /// <returns>The compatible surface capability, or <see langword="null"/> when the frame cannot expose one.</returns>
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

    /// <summary>
    /// Uploads a source region into the destination texture starting at the origin.
    /// </summary>
    /// <typeparam name="TPixel">The pixel type stored in the source region.</typeparam>
    /// <param name="api">The WebGPU API used for the upload.</param>
    /// <param name="queue">The queue that receives the upload commands.</param>
    /// <param name="destinationTexture">The destination texture.</param>
    /// <param name="sourceRegion">The CPU-side source region to upload.</param>
    /// <param name="memoryAllocator">The allocator used when a packed staging copy is required.</param>
    internal static void UploadTextureFromRegion<TPixel>(
        WebGPU api,
        Queue* queue,
        Texture* destinationTexture,
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

    /// <summary>
    /// Aligns a byte count to WebGPU's 256-byte row-upload requirement.
    /// </summary>
    /// <param name="value">The byte count to align.</param>
    /// <returns>The aligned byte count.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint AlignTo256(uint value) => (value + 255U) & ~255U;
}
