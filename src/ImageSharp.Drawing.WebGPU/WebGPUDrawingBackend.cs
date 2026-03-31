// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Runtime.CompilerServices;
using Silk.NET.WebGPU;
using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// WebGPU-backed implementation of <see cref="IDrawingBackend"/>.
/// </summary>
/// <remarks>
/// Scene composition uses a staged WebGPU raster path when the target surface and pixel format are supported,
/// and falls back to <see cref="DefaultDrawingBackend"/> otherwise.
/// </remarks>
public sealed unsafe partial class WebGPUDrawingBackend : IDrawingBackend, IDisposable
{
    private const int CompositeTileWidth = 16;
    private const int CompositeTileHeight = 16;
    private const int UniformBufferOffsetAlignment = 256;

    // A single flush can rerun the staged path a small number of times while the scratch
    // buffers converge on the capacity reported by the scheduling stages.
    // The prepare shader cancels early when any single buffer overflows, so each
    // retry only discovers one new overflow. 8 attempts covers all 7 bump buffers
    // plus the final successful run. Only needed on the first flush; subsequent
    // flushes reuse the persisted GPU-reported sizes and need zero retries.
    private const int MaxDynamicGrowthAttempts = 8;

    private readonly DefaultDrawingBackend fallbackBackend;
    private static bool? isSupported;

    // The staged pipeline keeps the most recently successful scratch capacities so later flushes
    // can start closer to the scene sizes the current device has already proven it needs.
    private WebGPUSceneBumpSizes bumpSizes = WebGPUSceneBumpSizes.Initial();

    // Cached arenas for cross-flush buffer reuse. Rented via Interlocked.Exchange at flush
    // start and returned at flush end so parallel flushes on different threads don't contend.
    private WebGPUSceneSchedulingArena? cachedSchedulingArena;
    private bool isDisposed;

    private static readonly Dictionary<Type, CompositePixelRegistration> CompositePixelHandlers = CreateCompositePixelHandlers();

    /// <summary>
    /// Initializes a new instance of the <see cref="WebGPUDrawingBackend"/> class.
    /// </summary>
    public WebGPUDrawingBackend()
        => this.fallbackBackend = DefaultDrawingBackend.Instance;

    /// <summary>
    /// Gets a value indicating whether the last flush completed on the staged path.
    /// </summary>
    internal bool TestingLastFlushUsedGPU { get; private set; }

    /// <summary>
    /// Gets the testing-only diagnostic containing the last GPU initialization failure reason, if any.
    /// </summary>
    internal string? TestingLastGPUInitializationFailure { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the last staged flush used chunked oversized-scene dispatch.
    /// </summary>
    internal bool TestingLastFlushUsedChunking { get; private set; }

    /// <summary>
    /// Gets the chunkable binding-limit failure that selected the chunked path for the last staged flush.
    /// </summary>
    internal WebGPUSceneDispatch.BindingLimitBuffer TestingLastChunkingBindingFailure { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the last flush completed on the staged path.
    /// </summary>
    public bool DiagnosticLastFlushUsedGPU => this.TestingLastFlushUsedGPU;

    /// <summary>
    /// Gets the last staged-scene creation or dispatch failure that forced CPU fallback.
    /// </summary>
    public string? DiagnosticLastSceneFailure => this.TestingLastGPUInitializationFailure;

    /// <summary>
    /// Gets a value indicating whether the last staged flush used the chunked oversized-scene path.
    /// </summary>
    public bool DiagnosticLastFlushUsedChunking => this.TestingLastFlushUsedChunking;

    /// <summary>
    /// Gets the chunkable binding-limit failure that selected the chunked oversized-scene path for the last staged flush.
    /// </summary>
    public string DiagnosticLastChunkingBindingFailure => this.TestingLastChunkingBindingFailure.ToString();

    /// <summary>
    /// Gets a value indicating whether WebGPU is available on the current system.
    /// This probes the runtime by attempting to acquire an adapter and device.
    /// The result is cached after the first probe.
    /// </summary>
    public bool IsSupported => isSupported ??= ProbeFullSupport();

    /// <summary>
    /// Probes whether WebGPU compute is fully supported on the current system.
    /// First checks adapter/device availability in-process. If that succeeds,
    /// spawns a child process via <see cref="RemoteExecutor"/> to test compute
    /// pipeline creation, which can crash with an unrecoverable access violation
    /// on some systems. If the remote executor is not available, falls back to
    /// the device-only check.
    /// </summary>
    /// <returns>Returns <see langword="true"/> if WebGPU compute support is available; otherwise, <see langword="false"/>.</returns>
    private static bool ProbeFullSupport()
    {
        if (!ProbeSupport())
        {
            return false;
        }

        if (!RemoteExecutor.IsSupported)
        {
            return true;
        }

        return RemoteExecutor.Invoke(ProbeComputePipelineSupport) == 0;
    }

    /// <summary>
    /// Determines whether WebGPU adapter and device are available on the current system.
    /// </summary>
    /// <remarks>This method only checks adapter and device availability. It does not attempt
    /// compute pipeline creation. Use <see cref="ProbeFullSupport"/> for a complete check.</remarks>
    /// <returns>Returns <see langword="true"/> if a WebGPU device is available; otherwise, <see langword="false"/>.</returns>
    public static bool ProbeSupport()
    {
        try
        {
            using WebGPURuntime.Lease lease = WebGPURuntime.Acquire();
            return WebGPURuntime.TryGetOrCreateDevice(out _, out _, out _);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Probes full WebGPU compute pipeline support by compiling a trivial shader and
    /// creating a compute pipeline. This method may crash with an access violation on
    /// systems with broken WebGPU compute support - callers should run it in a child
    /// process (for example via <c>RemoteExecutor</c>) to isolate the crash.
    /// </summary>
    /// <returns>Exit code: 0 on success, 1 on failure.</returns>
    public static int ProbeComputePipelineSupport()
    {
        try
        {
            using WebGPURuntime.Lease lease = WebGPURuntime.Acquire();
            if (!WebGPURuntime.TryGetOrCreateDevice(out Device* device, out _, out _))
            {
                return 1;
            }

            WebGPU api = lease.Api;

            ReadOnlySpan<byte> probeShader = "@compute @workgroup_size(1) fn main() {}\0"u8;
            fixed (byte* shaderCodePtr = probeShader)
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

                ShaderModule* shaderModule = api.DeviceCreateShaderModule(device, in shaderDescriptor);
                if (shaderModule is null)
                {
                    return 1;
                }

                try
                {
                    ReadOnlySpan<byte> entryPoint = "main\0"u8;
                    fixed (byte* entryPointPtr = entryPoint)
                    {
                        ProgrammableStageDescriptor computeStage = new()
                        {
                            Module = shaderModule,
                            EntryPoint = entryPointPtr
                        };

                        PipelineLayoutDescriptor layoutDescriptor = new()
                        {
                            BindGroupLayoutCount = 0,
                            BindGroupLayouts = null
                        };

                        PipelineLayout* pipelineLayout = api.DeviceCreatePipelineLayout(device, in layoutDescriptor);
                        if (pipelineLayout is null)
                        {
                            return 1;
                        }

                        try
                        {
                            ComputePipelineDescriptor pipelineDescriptor = new()
                            {
                                Layout = pipelineLayout,
                                Compute = computeStage
                            };

                            ComputePipeline* pipeline = api.DeviceCreateComputePipeline(device, in pipelineDescriptor);
                            if (pipeline is null)
                            {
                                return 1;
                            }

                            api.ComputePipelineRelease(pipeline);
                            return 0;
                        }
                        finally
                        {
                            api.PipelineLayoutRelease(pipelineLayout);
                        }
                    }
                }
                finally
                {
                    api.ShaderModuleRelease(shaderModule);
                }
            }
        }
        catch
        {
            return 1;
        }
    }

    /// <inheritdoc />
    public void FlushCompositions<TPixel>(
        Configuration configuration,
        ICanvasFrame<TPixel> target,
        CompositionScene compositionScene)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        this.ThrowIfDisposed();
        if (compositionScene.CommandCount == 0)
        {
            return;
        }

        this.TestingLastFlushUsedGPU = false;
        this.TestingLastGPUInitializationFailure = null;
        this.TestingLastFlushUsedChunking = false;
        this.TestingLastChunkingBindingFailure = WebGPUSceneDispatch.BindingLimitBuffer.None;
        WebGPUSceneBumpSizes currentBumpSizes = this.bumpSizes;

        // Rent the cached scheduling arena. Null on first flush or if another thread has it.
        // Returned in the finally block for the next flush to reuse.
        WebGPUSceneSchedulingArena? schedulingArena = Interlocked.Exchange(ref this.cachedSchedulingArena, null);
        try
        {
            // Retry loop: bump allocators start small (Vello defaults) and the GPU discovers
            // the actual sizes needed. Each overflow grows the failing buffers. The prepare
            // shader does not cancel on overflow so all stages report true demand per pass,
            // but data dependencies mean later stages report zero when earlier ones overflow.
            // Typically converges in 3-5 attempts on first use, then zero retries thereafter
            // because successful sizes are persisted in this.bumpSizes.
            for (int attempt = 0; attempt < MaxDynamicGrowthAttempts; attempt++)
            {
                if (!WebGPUSceneDispatch.TryCreateStagedScene(configuration, target, compositionScene, currentBumpSizes, out bool exceedsBindingLimit, out WebGPUSceneDispatch.BindingLimitFailure bindingLimitFailure, out WebGPUStagedScene stagedScene, out string? error))
                {
                    this.TestingLastGPUInitializationFailure = exceedsBindingLimit
                        ? error ?? "The staged WebGPU scene exceeded the current binding limits."
                        : error ?? "Failed to create the staged WebGPU scene.";
                    this.FlushCompositionsFallback(configuration, target, compositionScene, compositionBounds: null);
                    return;
                }

                try
                {
                    this.TestingLastFlushUsedGPU = true;
                    this.TestingLastFlushUsedChunking = stagedScene.BindingLimitFailure.Buffer != WebGPUSceneDispatch.BindingLimitBuffer.None;
                    this.TestingLastChunkingBindingFailure = stagedScene.BindingLimitFailure.Buffer;

                    if (stagedScene.EncodedScene.FillCount == 0)
                    {
                        return;
                    }

                    if (WebGPUSceneDispatch.TryRenderStagedScene(ref stagedScene, ref schedulingArena, out bool requiresGrowth, out WebGPUSceneBumpSizes grownBumpSizes, out error))
                    {
                        // Persist GPU-reported actual usage for next flush.
                        this.bumpSizes = grownBumpSizes;
                        return;
                    }

                    this.TestingLastFlushUsedGPU = false;
                    if (requiresGrowth)
                    {
                        // Bump overflow — retry with GPU-reported sizes.
                        currentBumpSizes = grownBumpSizes;
                        continue;
                    }

                    this.TestingLastGPUInitializationFailure = error ?? "The staged WebGPU scene dispatch failed.";
                }
                finally
                {
                    stagedScene.Dispose();
                }

                this.FlushCompositionsFallback(configuration, target, compositionScene, compositionBounds: null);
                return;
            }

            this.TestingLastGPUInitializationFailure = "The staged WebGPU scene exceeded the current dynamic growth retry budget.";
            this.FlushCompositionsFallback(configuration, target, compositionScene, compositionBounds: null);
        }
        finally
        {
            // Return the arena for the next flush. If another thread already returned one,
            // dispose the displaced arena (at most one survives in the cache).
            WebGPUSceneSchedulingArena? prev = Interlocked.Exchange(ref this.cachedSchedulingArena, schedulingArena);
            WebGPUSceneDispatch.DisposeSchedulingArena(prev);
        }
    }

    /// <summary>
    /// Executes the scene on the CPU fallback backend, then uploads the result
    /// to the native GPU surface.
    /// </summary>
    private void FlushCompositionsFallback<TPixel>(
        Configuration configuration,
        ICanvasFrame<TPixel> target,
        CompositionScene compositionScene,
        Rectangle? compositionBounds)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        _ = target.TryGetNativeSurface(out NativeSurface? nativeSurface);
        _ = nativeSurface!.TryGetCapability(out WebGPUSurfaceCapability? capability);

        Rectangle targetBounds = target.Bounds;
        using Buffer2D<TPixel> stagingBuffer =
            configuration.MemoryAllocator.Allocate2D<TPixel>(targetBounds.Right, targetBounds.Bottom, AllocationOptions.Clean);

        Buffer2DRegion<TPixel> stagingRegion = new(stagingBuffer, targetBounds);
        ICanvasFrame<TPixel> stagingFrame = new MemoryCanvasFrame<TPixel>(stagingRegion);

        if (!this.TryReadRegion(
                configuration,
                target,
                new Rectangle(0, 0, targetBounds.Width, targetBounds.Height),
                stagingRegion))
        {
            return;
        }

        this.fallbackBackend.FlushCompositions(configuration, stagingFrame, compositionScene);

        using WebGPURuntime.Lease lease = WebGPURuntime.Acquire();
        Buffer2DRegion<TPixel> uploadRegion = compositionBounds is Rectangle cb && cb.Width > 0 && cb.Height > 0
            ? stagingRegion.GetSubRegion(cb)
            : stagingRegion;

        uint destX = compositionBounds is Rectangle cbx ? (uint)cbx.X : 0;
        uint destY = compositionBounds is Rectangle cby ? (uint)cby.Y : 0;

        WebGPUFlushContext.UploadTextureFromRegion(
            lease.Api,
            (Queue*)capability!.Queue,
            (Texture*)capability.TargetTexture,
            uploadRegion,
            configuration.MemoryAllocator,
            destX,
            destY,
            0);
    }

    /// <summary>
    /// CPU fallback for layer compositing when the GPU path is unavailable but the
    /// destination is a native GPU surface.
    /// </summary>
    private void ComposeLayerFallback<TPixel>(
        Configuration configuration,
        ICanvasFrame<TPixel> source,
        ICanvasFrame<TPixel> destination,
        Point destinationOffset,
        GraphicsOptions options)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        _ = destination.TryGetNativeSurface(out NativeSurface? destSurface);
        _ = destSurface!.TryGetCapability(out WebGPUSurfaceCapability? destCapability);

        MemoryAllocator allocator = configuration.MemoryAllocator;

        using Buffer2D<TPixel> destBuffer = allocator.Allocate2D<TPixel>(destination.Bounds.Width, destination.Bounds.Height);
        Buffer2DRegion<TPixel> destRegion = new(destBuffer);
        if (!this.TryReadRegion(configuration, destination, destination.Bounds, destRegion))
        {
            return;
        }

        using Buffer2D<TPixel> srcBuffer = allocator.Allocate2D<TPixel>(source.Bounds.Width, source.Bounds.Height);
        Buffer2DRegion<TPixel> srcRegion = new(srcBuffer);
        if (!this.TryReadRegion(configuration, source, source.Bounds, srcRegion))
        {
            return;
        }

        ICanvasFrame<TPixel> destFrame = new MemoryCanvasFrame<TPixel>(destRegion);
        ICanvasFrame<TPixel> srcFrame = new MemoryCanvasFrame<TPixel>(srcRegion);

        DefaultDrawingBackend.ComposeLayer(configuration, srcFrame, destFrame, destinationOffset, options);

        using WebGPURuntime.Lease lease = WebGPURuntime.Acquire();
        WebGPUFlushContext.UploadTextureFromRegion(
            lease.Api,
            (Queue*)destCapability!.Queue,
            (Texture*)destCapability.TargetTexture,
            destRegion,
            configuration.MemoryAllocator);
    }

    /// <summary>
    /// Creates one transient composition texture that can be rendered to, sampled from, and copied.
    /// </summary>
    internal static bool TryCreateCompositionTexture(
        WebGPUFlushContext flushContext,
        int width,
        int height,
        out Texture* texture,
        out TextureView* textureView,
        out string? error)
    {
        textureView = null;

        TextureDescriptor textureDescriptor = new()
        {
            Usage = TextureUsage.TextureBinding | TextureUsage.StorageBinding | TextureUsage.CopySrc | TextureUsage.CopyDst,
            Dimension = TextureDimension.Dimension2D,
            Size = new Extent3D((uint)width, (uint)height, 1),
            Format = flushContext.TextureFormat,
            MipLevelCount = 1,
            SampleCount = 1
        };

        texture = flushContext.Api.DeviceCreateTexture(flushContext.Device, in textureDescriptor);
        if (texture is null)
        {
            error = "Failed to create WebGPU composition texture.";
            return false;
        }

        TextureViewDescriptor textureViewDescriptor = new()
        {
            Format = flushContext.TextureFormat,
            Dimension = TextureViewDimension.Dimension2D,
            BaseMipLevel = 0,
            MipLevelCount = 1,
            BaseArrayLayer = 0,
            ArrayLayerCount = 1,
            Aspect = TextureAspect.All
        };

        textureView = flushContext.Api.TextureCreateView(texture, in textureViewDescriptor);
        if (textureView is null)
        {
            flushContext.Api.TextureRelease(texture);
            texture = null;
            error = "Failed to create WebGPU composition texture view.";
            return false;
        }

        flushContext.TrackTexture(texture);
        flushContext.TrackTextureView(textureView);
        error = null;
        return true;
    }

    /// <summary>
    /// Copies one texture region from source to destination texture.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void CopyTextureRegion(
        WebGPUFlushContext flushContext,
        Texture* sourceTexture,
        int sourceOriginX,
        int sourceOriginY,
        Texture* destinationTexture,
        int destinationOriginX,
        int destinationOriginY,
        int width,
        int height)
    {
        ImageCopyTexture source = new()
        {
            Texture = sourceTexture,
            MipLevel = 0,
            Origin = new Origin3D((uint)sourceOriginX, (uint)sourceOriginY, 0),
            Aspect = TextureAspect.All
        };

        ImageCopyTexture destination = new()
        {
            Texture = destinationTexture,
            MipLevel = 0,
            Origin = new Origin3D((uint)destinationOriginX, (uint)destinationOriginY, 0),
            Aspect = TextureAspect.All
        };

        Extent3D copySize = new((uint)width, (uint)height, 1);
        flushContext.Api.CommandEncoderCopyTextureToTexture(flushContext.CommandEncoder, in source, in destination, in copySize);
    }

    /// <summary>
    /// Divides <paramref name="value"/> by <paramref name="divisor"/> and rounds up.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint DivideRoundUp(int value, int divisor)
        => (uint)((value + divisor - 1) / divisor);

    /// <summary>
    /// Reinterprets a single-precision float as its raw unsigned 32-bit bit pattern.
    /// </summary>
    /// <param name="value">The value to reinterpret.</param>
    /// <returns>The raw IEEE 754 bit pattern for <paramref name="value"/>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint FloatToUInt32Bits(float value)
        => unchecked((uint)BitConverter.SingleToInt32Bits(value));

    /// <summary>
    /// Submits the current command encoder, if any.
    /// </summary>
    internal static bool TrySubmit(WebGPUFlushContext flushContext)
    {
        CommandEncoder* commandEncoder = flushContext.CommandEncoder;
        if (commandEncoder is null)
        {
            return true;
        }

        flushContext.EndComputePassIfOpen();
        flushContext.EndRenderPassIfOpen();

        CommandBuffer* commandBuffer = null;
        try
        {
            CommandBufferDescriptor descriptor = default;
            commandBuffer = flushContext.Api.CommandEncoderFinish(commandEncoder, in descriptor);
            if (commandBuffer is null)
            {
                return false;
            }

            flushContext.Api.QueueSubmit(flushContext.Queue, 1, ref commandBuffer);
            flushContext.Api.CommandBufferRelease(commandBuffer);
            commandBuffer = null;
            flushContext.Api.CommandEncoderRelease(commandEncoder);
            flushContext.CommandEncoder = null;
            return true;
        }
        finally
        {
            if (commandBuffer is not null)
            {
                flushContext.Api.CommandBufferRelease(commandBuffer);
            }
        }
    }

    /// <summary>
    /// Releases all cached shared WebGPU resources.
    /// </summary>
    public void Dispose()
    {
        if (this.isDisposed)
        {
            return;
        }

        this.TestingLastFlushUsedGPU = false;
        this.TestingLastGPUInitializationFailure = null;
        WebGPUSceneDispatch.DisposeSchedulingArena(this.cachedSchedulingArena);
        this.isDisposed = true;
    }

    /// <summary>
    /// Throws <see cref="ObjectDisposedException"/> when this backend is disposed.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(this.isDisposed, this);
}
