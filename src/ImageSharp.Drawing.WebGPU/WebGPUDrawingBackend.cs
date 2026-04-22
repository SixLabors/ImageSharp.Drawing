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
/// Public diagnostic properties on this type describe only the most recent flush executed by this backend
/// instance. They are lightweight inspection state for debugging, tests, and benchmarks, and they are
/// overwritten by the next flush.
/// </remarks>
public sealed unsafe partial class WebGPUDrawingBackend : IDrawingBackend, IDisposable
{
    // Number of independently sized scratch buffers tracked by WebGPUSceneBumpSizes.
    // A first-use flush can expose at most one newly visible allocator overflow per
    // failed pass, so the retry budget is expressed in terms of this count. The
    // tracked allocators are Lines, Binning, PathRows, PathTiles, SegCounts,
    // Segments, BlendSpill, and Ptcl.
    private const int ScratchAllocatorCount = 8;

    // A first flush can rerun the staged path while the GPU-reported scratch capacities
    // converge. Earlier scheduling overflows can prevent later stages from reporting
    // their own demand, so one failed pass can be needed per tracked allocator. A
    // Failed-only report can also require one conservative force-growth pass when no
    // individual counter exceeded its current capacity. Add one final pass for the
    // successful render after the last growth.
    private const int MaxDynamicGrowthAttempts = ScratchAllocatorCount + 2;

    private readonly DefaultDrawingBackend fallbackBackend;

    // The staged pipeline keeps the most recently successful scratch capacities so later flushes
    // can start closer to the scene sizes the current device has already proven it needs.
    private WebGPUSceneBumpSizes bumpSizes = WebGPUSceneBumpSizes.Initial();

    // Cached arenas for cross-flush buffer reuse. Rented via Interlocked.Exchange at flush
    // start and returned at flush end so parallel flushes on different threads don't contend.
    private WebGPUSceneSchedulingArena? cachedSchedulingArena;
    private WebGPUSceneResourceArena? cachedResourceArena;
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
    /// Gets the encoded scene path-tag payload size for the most recent staged flush.
    /// </summary>
    internal int TestingLastEncodedScenePathTagByteCount { get; private set; }

    /// <summary>
    /// Gets the encoded scene clip record count for the most recent staged flush.
    /// </summary>
    internal int TestingLastEncodedSceneClipCount { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the most recent staged flush used the large pathtag scan path.
    /// </summary>
    internal bool TestingLastUsedLargePathScan { get; private set; }

    /// <summary>
    /// Gets the clip-reduce dispatch count for the most recent staged flush.
    /// </summary>
    internal uint TestingLastClipReduceX { get; private set; }

    /// <summary>
    /// Gets the number of staged-dispatch attempts (including the successful one, if any) for the most recent flush.
    /// </summary>
    internal int TestingLastAttemptCount { get; private set; }

    /// <summary>
    /// Gets the per-attempt bump-allocator readbacks for the most recent flush.
    /// </summary>
    internal List<GpuSceneBumpAllocators> TestingLastAttemptBumpAllocators { get; } = [];

    /// <summary>
    /// Gets the per-attempt bump-size budgets for the most recent flush.
    /// </summary>
    internal List<WebGPUSceneBumpSizes> TestingLastAttemptBumpSizes { get; } = [];

    /// <summary>
    /// Gets the per-attempt <c>requiresGrowth</c> decisions for the most recent flush.
    /// </summary>
    internal List<bool> TestingLastAttemptRequiresGrowth { get; } = [];

    /// <summary>
    /// Gets a value indicating whether the most recent flush exhausted its dynamic-growth retry budget
    /// and fell through to the CPU fallback.
    /// </summary>
    internal bool TestingLastExhaustedRetryBudget { get; private set; }

    /// <summary>
    /// Gets the final-attempt coarse pass workgroup counts for the most recent flush (X, Y).
    /// </summary>
    internal (uint X, uint Y) TestingLastCoarseDispatch { get; private set; }

    /// <summary>
    /// Gets the final-attempt fine pass workgroup counts for the most recent flush (X, Y).
    /// </summary>
    internal (uint X, uint Y) TestingLastFineDispatch { get; private set; }

    /// <summary>
    /// Gets the final-attempt chunk window for the most recent flush (TileYStart, TileHeight, TileBufferHeight).
    /// </summary>
    internal (uint Start, uint Height, uint BufferHeight) TestingLastChunkWindow { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the last flush completed on the staged path.
    /// </summary>
    /// <remarks>
    /// This value describes only the most recent call to <see cref="FlushCompositions{TPixel}(Configuration, ICanvasFrame{TPixel}, CompositionScene)"/>
    /// on this backend instance. It is overwritten by the next flush.
    /// </remarks>
    public bool DiagnosticLastFlushUsedGPU => this.TestingLastFlushUsedGPU;

    /// <summary>
    /// Gets the last staged-scene creation or dispatch failure that forced CPU fallback.
    /// </summary>
    /// <remarks>
    /// This value describes only the most recent call to <see cref="FlushCompositions{TPixel}(Configuration, ICanvasFrame{TPixel}, CompositionScene)"/>
    /// on this backend instance. It is reset at the start of each flush and overwritten by the next failure.
    /// A <see langword="null"/> value means no failure reason was recorded for the most recent flush.
    /// </remarks>
    public string? DiagnosticLastSceneFailure => this.TestingLastGPUInitializationFailure;

    /// <summary>
    /// Gets a value indicating whether the last staged flush used the chunked oversized-scene path.
    /// </summary>
    /// <remarks>
    /// This value describes only the most recent call to <see cref="FlushCompositions{TPixel}(Configuration, ICanvasFrame{TPixel}, CompositionScene)"/>
    /// on this backend instance. It is overwritten by the next flush.
    /// </remarks>
    public bool DiagnosticLastFlushUsedChunking => this.TestingLastFlushUsedChunking;

    /// <summary>
    /// Gets the chunkable binding-limit failure that selected the chunked oversized-scene path for the last staged flush.
    /// </summary>
    /// <remarks>
    /// This value describes only the most recent call to <see cref="FlushCompositions{TPixel}(Configuration, ICanvasFrame{TPixel}, CompositionScene)"/>
    /// on this backend instance. When the most recent flush did not use the chunked path, this property returns
    /// the default <c>None</c> value from <see cref="WebGPUSceneDispatch.BindingLimitBuffer"/>.
    /// </remarks>
    public string DiagnosticLastChunkingBindingFailure => this.TestingLastChunkingBindingFailure.ToString();

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
        this.TestingLastEncodedScenePathTagByteCount = 0;
        this.TestingLastEncodedSceneClipCount = 0;
        this.TestingLastUsedLargePathScan = false;
        this.TestingLastClipReduceX = 0;
        this.TestingLastAttemptCount = 0;
        this.TestingLastAttemptBumpAllocators.Clear();
        this.TestingLastAttemptBumpSizes.Clear();
        this.TestingLastAttemptRequiresGrowth.Clear();
        this.TestingLastExhaustedRetryBudget = false;
        WebGPUSceneBumpSizes currentBumpSizes = this.bumpSizes;

        // Rent the cached scheduling arena. Null on first flush or if another thread has it.
        // Returned in the finally block for the next flush to reuse.
        WebGPUSceneSchedulingArena? schedulingArena = Interlocked.Exchange(ref this.cachedSchedulingArena, null);
        WebGPUSceneResourceArena? resourceArena = Interlocked.Exchange(ref this.cachedResourceArena, null);
        try
        {
            // Retry loop: scratch allocators start small and the GPU reports actual demand.
            // Earlier scheduling overflows can hide later-stage demand, so a first-use flush
            // can require several passes before the capacities converge. Successful sizes are
            // persisted in this.bumpSizes, so later flushes usually run without retries.
            for (int attempt = 0; attempt < MaxDynamicGrowthAttempts; attempt++)
            {
                if (!WebGPUSceneDispatch.TryCreateStagedScene(configuration, target, compositionScene, currentBumpSizes, ref resourceArena, out bool exceedsBindingLimit, out WebGPUSceneDispatch.BindingLimitFailure bindingLimitFailure, out WebGPUStagedScene stagedScene, out string? error))
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
                    this.TestingLastEncodedScenePathTagByteCount = stagedScene.EncodedScene.PathTagByteCount;
                    this.TestingLastEncodedSceneClipCount = stagedScene.EncodedScene.ClipCount;
                    this.TestingLastUsedLargePathScan = stagedScene.Config.WorkgroupCounts.UseLargePathScan;
                    this.TestingLastClipReduceX = stagedScene.Config.WorkgroupCounts.ClipReduceX;
                    this.TestingLastCoarseDispatch = (stagedScene.Config.WorkgroupCounts.CoarseX, stagedScene.Config.WorkgroupCounts.CoarseY);
                    this.TestingLastFineDispatch = (stagedScene.Config.WorkgroupCounts.FineX, stagedScene.Config.WorkgroupCounts.FineY);
                    this.TestingLastChunkWindow = (stagedScene.Config.ChunkWindow.TileYStart, stagedScene.Config.ChunkWindow.TileHeight, stagedScene.Config.ChunkWindow.TileBufferHeight);

                    if (stagedScene.EncodedScene.FillCount == 0)
                    {
                        return;
                    }

                    bool renderSucceeded = WebGPUSceneDispatch.TryRenderStagedScene(ref stagedScene, ref schedulingArena, out bool requiresGrowth, out WebGPUSceneBumpSizes grownBumpSizes, out GpuSceneBumpAllocators lastBumpAllocators, out error);
                    this.TestingLastAttemptCount = attempt + 1;
                    this.TestingLastAttemptBumpSizes.Add(currentBumpSizes);
                    this.TestingLastAttemptBumpAllocators.Add(lastBumpAllocators);
                    this.TestingLastAttemptRequiresGrowth.Add(requiresGrowth);

                    if (renderSucceeded)
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
            this.TestingLastExhaustedRetryBudget = true;
            this.FlushCompositionsFallback(configuration, target, compositionScene, compositionBounds: null);
        }
        finally
        {
            // Return arenas for the next flush. If another thread already returned one,
            // dispose the displaced arena (at most one survives in the cache).
            WebGPUSceneSchedulingArena.Dispose(Interlocked.Exchange(ref this.cachedSchedulingArena, schedulingArena));
            WebGPUSceneResourceArena.Dispose(Interlocked.Exchange(ref this.cachedResourceArena, resourceArena));
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
        if (!target.TryGetNativeSurface(out NativeSurface? nativeSurface) ||
            !nativeSurface.TryGetCapability(out WebGPUSurfaceCapability? capability))
        {
            return;
        }

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

        WebGPU api = WebGPURuntime.GetApi();
        using WebGPUHandle.HandleReference queueReference = capability.QueueHandle.AcquireReference();

        Buffer2DRegion<TPixel> uploadRegion = compositionBounds is Rectangle cb && cb.Width > 0 && cb.Height > 0
            ? stagingRegion.GetSubRegion(cb)
            : stagingRegion;

        uint destX = compositionBounds is Rectangle cbx ? (uint)cbx.X : 0;
        uint destY = compositionBounds is Rectangle cby ? (uint)cby.Y : 0;

        using WebGPUHandle.HandleReference textureReference = capability.TargetTextureHandle.AcquireReference();
        WebGPUFlushContext.UploadTextureFromRegion(
            api,
            (Queue*)queueReference.Handle,
            (Texture*)textureReference.Handle,
            uploadRegion,
            configuration.MemoryAllocator,
            destX,
            destY,
            0);
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

        using (WebGPUHandle.HandleReference deviceReference = flushContext.DeviceHandle.AcquireReference())
        {
            texture = flushContext.Api.DeviceCreateTexture((Device*)deviceReference.Handle, in textureDescriptor);
        }

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

            using (WebGPUHandle.HandleReference queueReference = flushContext.QueueHandle.AcquireReference())
            {
                flushContext.Api.QueueSubmit((Queue*)queueReference.Handle, 1, ref commandBuffer);
            }

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
        this.TestingLastFlushUsedChunking = false;
        this.TestingLastChunkingBindingFailure = WebGPUSceneDispatch.BindingLimitBuffer.None;
        this.TestingLastEncodedScenePathTagByteCount = 0;
        this.TestingLastEncodedSceneClipCount = 0;
        this.TestingLastUsedLargePathScan = false;
        this.TestingLastClipReduceX = 0;
        WebGPUSceneSchedulingArena.Dispose(this.cachedSchedulingArena);
        WebGPUSceneResourceArena.Dispose(this.cachedResourceArena);
        this.isDisposed = true;
    }

    /// <summary>
    /// Throws <see cref="ObjectDisposedException"/> when this backend is disposed.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(this.isDisposed, this);
}
