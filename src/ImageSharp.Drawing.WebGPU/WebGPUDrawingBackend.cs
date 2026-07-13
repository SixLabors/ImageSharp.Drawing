// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Runtime.CompilerServices;
using Silk.NET.WebGPU;
using Silk.NET.WebGPU.Extensions.WGPU;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// WebGPU-backed implementation of <see cref="IDrawingBackend"/>.
/// </summary>
/// <remarks>
/// Uses WebGPU when the target surface and pixel format are supported.
/// Diagnostic properties describe only the most recent flush executed by this backend instance and are overwritten
/// by the next flush.
/// </remarks>
public sealed unsafe partial class WebGPUDrawingBackend : IDrawingBackend, IDisposable
{
    // Number of independently sized scratch buffers tracked by WebGPUSceneBumpSizes.
    // A first-use flush can expose at most one newly visible allocator overflow per
    // failed pass, so the retry budget is expressed in terms of this count. The
    // tracked allocators are Lines, Binning, PathRows, PathTiles, SegCounts,
    // Segments, BlendSpill, and Ptcl.
    private const int ScratchAllocatorCount = 8;

    // A first flush can rerun the WebGPU path while the GPU-reported scratch capacities
    // converge. Earlier scheduling overflows can prevent later stages from reporting
    // their own demand, so one failed pass can be needed per tracked allocator. A
    // Failed-only report can also require one conservative force-growth pass when no
    // individual counter exceeded its current capacity. Add one final pass for the
    // successful render after the last growth.
    private const int MaxDynamicGrowthAttempts = ScratchAllocatorCount + 2;

    // The staged pipeline keeps the most recently successful scratch capacities so later flushes
    // can start closer to the scene sizes the current device has already proven it needs.
    private WebGPUSceneBumpSizes bumpSizes = WebGPUSceneBumpSizes.Initial();

    // Cached arenas for short-lived scene reuse. Interlocked.Exchange makes each
    // cache a one-item rent slot: parallel flushes can race for reuse, but only
    // one caller can remove a given arena from the slot.
    private WebGPUSceneSchedulingArena? cachedSchedulingArena;
    private WebGPUSceneResourceArena? cachedResourceArena;

    // Deferred scheduling-status readbacks from recent flushes. The backend is the owner
    // because it outlives the per-frame canvases and scenes: parking a pending map on a
    // per-frame scene would force a blocking resolve at that scene's disposal, putting the
    // CPU-GPU sync right back into the frame. The queue is harvested wait-free at the start
    // of each flush; entries are only force-resolved when the queue exceeds its bound.
    // Offscreen entries carry the context for a corrective re-render on overflow and hold a
    // deferred-render reference that keeps their scene's GPU payload alive until harvested.
    private readonly object pendingStatusSync = new();
    private readonly List<PendingSchedulingStatusEntry> pendingSchedulingStatuses = [];

    // Bounds the deferred-readback queue. The GPU typically completes a frame's map by the
    // next flush, so the queue holds roughly one entry per flush in the current frame; the
    // bound only bites when the GPU falls several frames behind, where a blocking resolve
    // doubles as backpressure.
    private const int MaxPendingSchedulingStatuses = 8;

    // Advisory first-guess state for repeated oversized eager scenes. Parallel renders may
    // race to update it; every hinted chunk is still validated before dispatch, so a stale
    // or cross-scene value can only affect the first shrink attempt, not correctness.
    private int chunkHintBinding;
    private int chunkHintTargetWidth;
    private int chunkHintTargetHeight;
    private int chunkHintTileHeight;

    private WebGPUSceneDispatch.BindingLimitBuffer lastChunkingBindingFailure;
    private bool isDisposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebGPUDrawingBackend"/> class.
    /// </summary>
    public WebGPUDrawingBackend()
    {
    }

    /// <summary>
    /// Gets a value indicating whether the last WebGPU flush used chunked rendering.
    /// </summary>
    /// <remarks>
    /// This value describes only the most recent flush on this backend instance. It is overwritten by the next flush.
    /// </remarks>
    internal bool DiagnosticLastFlushUsedChunking { get; private set; }

    /// <summary>
    /// Gets the binding category that selected chunked rendering for the last WebGPU flush.
    /// </summary>
    /// <remarks>
    /// This value describes only the most recent flush on this backend instance. When the most recent flush did not use
    /// chunked rendering, this property returns <c>None</c>. A corrective re-render triggered by a deferred overflow
    /// readback also updates this value, so it can change after the originating flush call has returned.
    /// </remarks>
    internal string DiagnosticLastChunkingBindingFailure => this.lastChunkingBindingFailure.ToString();

    /// <inheritdoc />
    public DrawingBackendScene CreateScene(
        Configuration configuration,
        Rectangle targetBounds,
        DrawingCommandBatch commandBatch,
        IReadOnlyList<IDisposable>? ownedResources = null)
    {
        this.ThrowIfDisposed();

        // Batches containing Apply need the ordered encoder: Apply reads pixels back mid-scene,
        // so the operations before and after it must stay in submission order.
        bool encoded = commandBatch.HasApply
            ? WebGPUSceneEncoder.TryEncodeOrdered(
                commandBatch,
                targetBounds,
                configuration.MemoryAllocator,
                configuration.MaxDegreeOfParallelism,
                out WebGPUEncodedScene encodedScene,
                out string? error)
            : WebGPUSceneEncoder.TryEncode(
                commandBatch,
                targetBounds,
                configuration.MemoryAllocator,
                configuration.MaxDegreeOfParallelism,
                out encodedScene,
                out error);

        if (!encoded)
        {
            throw new InvalidOperationException(error);
        }

        return new WebGPUDrawingBackendScene(
            encodedScene,
            targetBounds,
            this.bumpSizes,
            ownedResources);
    }

    /// <inheritdoc />
    public void RenderScene<TPixel>(
        Configuration configuration,
        ICanvasFrame<TPixel> target,
        DrawingBackendScene scene)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        this.ThrowIfDisposed();

        NativeCanvasFrame<TPixel> nativeTarget = (NativeCanvasFrame<TPixel>)target;

        if (scene is not WebGPUDrawingBackendScene webGPUScene)
        {
            throw new InvalidOperationException("The scene is not compatible with the WebGPU drawing backend.");
        }

        if (!TryGetCompositeTextureFormat<TPixel>(out WebGPUTextureFormat formatId, out FeatureName requiredFeature))
        {
            throw new NotSupportedException($"The WebGPU backend does not support pixel format '{typeof(TPixel).Name}'.");
        }

        // RenderScene only accepts WebGPU native frames on this path, so cast once at the backend
        // boundary and keep staging focused on dispatch data.
        _ = nativeTarget.TryGetNativeSurface(out NativeSurface? nativeSurface);
        WebGPUNativeSurface webGPUTarget = (WebGPUNativeSurface)nativeSurface!;
        TextureFormat textureFormat = WebGPUTextureFormatMapper.ToNative(webGPUTarget.TargetFormat);

        if (webGPUTarget.TargetFormat != formatId)
        {
            throw new InvalidOperationException("The target texture format does not match the WebGPU drawing backend scene pixel format.");
        }

        if (nativeTarget.Bounds != webGPUScene.Bounds)
        {
            throw new InvalidOperationException("The target bounds do not match the WebGPU drawing backend scene bounds.");
        }

        this.DiagnosticLastFlushUsedChunking = false;
        this.lastChunkingBindingFailure = WebGPUSceneDispatch.BindingLimitBuffer.None;

        // Harvest deferred overflow readbacks from earlier presentation flushes before reading
        // capacities: completed maps resolve wait-free, and any observed overflow grows the
        // sizes this render is about to use.
        this.HarvestPendingSchedulingStatuses(webGPUScene, drainAll: false);

        WebGPUSceneBumpSizes currentBumpSizes = webGPUScene.BumpSizes;
        WebGPUSceneResourceArena? resourceArena = null;
        WebGPUSceneSchedulingArena? schedulingArena = null;

        try
        {
            // Ordered Apply scenes execute a flat operation plan with explicit layer transitions;
            // plain scenes take the single staged-dispatch path below.
            WebGPUEncodedScene encodedScene = webGPUScene.EncodedScene;
            if (encodedScene.HasOperations)
            {
                // Ordered ranges share one arena pair across the transaction. Rent the retained
                // high-water buffers here so repeated flushes do not recreate every GPU buffer.
                resourceArena = webGPUScene.RentResourceArena() ?? this.RentResourceArena();
                schedulingArena = webGPUScene.RentSchedulingArena() ?? this.RentSchedulingArena();

                using WebGPUFlushContext flushContext = WebGPUFlushContext.Create(
                    nativeTarget,
                    textureFormat,
                    requiredFeature,
                    configuration.MemoryAllocator);

                using WebGPUHandle.HandleReference targetTextureReference = webGPUTarget.TargetTextureHandle.AcquireReference();
                using WebGPUHandle.HandleReference targetTextureViewReference = webGPUTarget.TargetTextureViewHandle.AcquireReference();

                WebGPUSceneTarget rootTarget = new(
                    (Texture*)targetTextureReference.Handle,
                    (TextureView*)targetTextureViewReference.Handle,
                    nativeTarget.Bounds,
                    webGPUTarget.TextureCoordinateOffset);

                this.RenderOrderedScene<TPixel>(
                    configuration,
                    flushContext,
                    rootTarget,
                    encodedScene,
                    requiredFeature,
                    ref currentBumpSizes,
                    ref resourceArena,
                    ref schedulingArena);

                webGPUScene.UpdateBumpSizes(currentBumpSizes);

                // Max-merge rather than assign: the backend value is a high-water mark shared by
                // every future scene, and a small scene must not shrink it back into rediscovery.
                this.bumpSizes = MaxBumpSizes(this.bumpSizes, currentBumpSizes);
                return;
            }

            if (encodedScene.FillCount != 0)
            {
                // Scene arenas are rented once for the render. If a concurrent render
                // owns them, the backend cache supplies independent scratch buffers.
                resourceArena ??= webGPUScene.RentResourceArena() ?? this.RentResourceArena();
                schedulingArena ??= webGPUScene.RentSchedulingArena() ?? this.RentSchedulingArena();

                this.RenderEncodedSceneWithGrowth<TPixel>(
                    configuration,
                    webGPUTarget,
                    nativeTarget.Bounds,
                    textureFormat,
                    requiredFeature,
                    webGPUScene,
                    deferOverflowCheck: true,
                    ref currentBumpSizes,
                    ref resourceArena,
                    ref schedulingArena);
            }

            webGPUScene.UpdateBumpSizes(currentBumpSizes);

            // Max-merge rather than assign; see the ordered-scene path above.
            this.bumpSizes = MaxBumpSizes(this.bumpSizes, currentBumpSizes);
        }
        finally
        {
            webGPUScene.ReturnArenas(resourceArena, schedulingArena, this);
        }
    }

    /// <summary>
    /// Renders a plain (non-ordered) encoded scene through the staged pipeline with the dynamic
    /// scratch-growth retry loop. All texture targets defer the scratch-overflow readback so the
    /// flush never blocks on the GPU: presentation surfaces are corrected by the next frame's
    /// redraw, while offscreen targets park a corrective re-render that a later harvest invokes
    /// on the rare overflow, so no incomplete content can persist.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format of the flush target.</typeparam>
    /// <param name="configuration">The active processing configuration.</param>
    /// <param name="webGPUTarget">The native surface holding the target texture handles.</param>
    /// <param name="targetBounds">The target bounds for the flush.</param>
    /// <param name="textureFormat">The target texture format.</param>
    /// <param name="requiredFeature">The device feature required by the target format, or <see cref="FeatureName.Undefined"/>.</param>
    /// <param name="webGPUScene">The retained scene being rendered.</param>
    /// <param name="deferOverflowCheck">Whether the overflow readback may be deferred; corrective re-renders pass <see langword="false"/>.</param>
    /// <param name="currentBumpSizes">The scratch capacities carried across attempts.</param>
    /// <param name="resourceArena">The rented scene resource arena.</param>
    /// <param name="schedulingArena">The rented scheduling arena.</param>
    private void RenderEncodedSceneWithGrowth<TPixel>(
        Configuration configuration,
        WebGPUNativeSurface webGPUTarget,
        Rectangle targetBounds,
        TextureFormat textureFormat,
        FeatureName requiredFeature,
        WebGPUDrawingBackendScene webGPUScene,
        bool deferOverflowCheck,
        ref WebGPUSceneBumpSizes currentBumpSizes,
        ref WebGPUSceneResourceArena? resourceArena,
        ref WebGPUSceneSchedulingArena? schedulingArena)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        WebGPUEncodedScene encodedScene = webGPUScene.EncodedScene;
        bool renderCompleted = false;

        // Retry loop: scratch allocators start small and the GPU reports actual demand.
        // The retained scene keeps the largest observed size so later renders avoid
        // rediscovering the same growth.
        for (int attempt = 0; attempt < MaxDynamicGrowthAttempts; attempt++)
        {
            WebGPUStagedScene stagedScene = WebGPUSceneDispatch.CreateStagedScene<TPixel>(
                configuration,
                webGPUTarget,
                targetBounds,
                encodedScene,
                textureFormat,
                requiredFeature,
                currentBumpSizes,
                ref resourceArena);

            try
            {
                if (stagedScene.BindingLimitFailure.Buffer != WebGPUSceneDispatch.BindingLimitBuffer.None)
                {
                    this.DiagnosticLastFlushUsedChunking = true;
                    this.lastChunkingBindingFailure = stagedScene.BindingLimitFailure.Buffer;
                }

                bool renderSucceeded = WebGPUSceneDispatch.TryRenderStagedScene(
                    ref stagedScene,
                    ref schedulingArena,
                    this.GetChunkTileHeightHint(stagedScene.BindingLimitFailure.Buffer, encodedScene.TargetSize),
                    deferOverflowCheck,
                    out bool requiresGrowth,
                    out WebGPUSceneBumpSizes grownBumpSizes,
                    out uint successfulChunkTileHeight,
                    out WebGPUPendingSchedulingStatus? pendingStatus,
                    out string? error);

                if (renderSucceeded)
                {
                    currentBumpSizes = MaxBumpSizes(currentBumpSizes, grownBumpSizes);

                    // A deferred render parks its readback on the backend, which outlives
                    // per-frame scenes; a later flush harvests it and applies any observed
                    // growth. Offscreen targets additionally park a corrective re-render:
                    // their content is not redrawn every frame, so a rare overflow must be
                    // repaired rather than waiting for the next redraw.
                    if (pendingStatus is not null)
                    {
                        Action? correctiveRender = webGPUTarget.IsPresentationSurface
                            ? null
                            : () => this.RenderCorrective<TPixel>(configuration, webGPUTarget, targetBounds, textureFormat, requiredFeature, webGPUScene);
                        this.EnqueuePendingSchedulingStatus(pendingStatus, webGPUScene, correctiveRender);
                    }

                    if (successfulChunkTileHeight != 0)
                    {
                        this.UpdateChunkTileHeightHint(
                            stagedScene.BindingLimitFailure.Buffer,
                            encodedScene.TargetSize,
                            successfulChunkTileHeight);
                    }

                    renderCompleted = true;
                    break;
                }

                if (requiresGrowth)
                {
                    currentBumpSizes = MaxBumpSizes(currentBumpSizes, grownBumpSizes);
                    continue;
                }

                throw new InvalidOperationException(error ?? "The staged WebGPU scene dispatch failed.");
            }
            finally
            {
                stagedScene.Dispose();
            }
        }

        if (!renderCompleted)
        {
            throw new InvalidOperationException("The staged WebGPU scene exceeded the current dynamic growth retry budget.");
        }
    }

    /// <summary>
    /// Clears the given render target to transparent black by opening a render pass with a
    /// clear load action and immediately ending it.
    /// </summary>
    /// <param name="flushContext">The flush-scoped WebGPU device, queue, and encoder state.</param>
    /// <param name="targetView">The texture view to clear.</param>
    private static void ClearTarget(WebGPUFlushContext flushContext, TextureView* targetView)
    {
        if (!flushContext.EnsureCommandEncoder() || !flushContext.BeginRenderPass(targetView, loadExisting: false))
        {
            throw new InvalidOperationException("Failed to clear the WebGPU layer target.");
        }

        flushContext.EndRenderPassIfOpen();
    }

    /// <summary>
    /// Computes the maximum scratch capacities observed across render attempts.
    /// </summary>
    /// <param name="left">The first scratch-capacity set.</param>
    /// <param name="right">The second scratch-capacity set.</param>
    /// <returns>The maximum scratch-capacity set.</returns>
    private static WebGPUSceneBumpSizes MaxBumpSizes(
        WebGPUSceneBumpSizes left,
        WebGPUSceneBumpSizes right)
        => new(
            Math.Max(left.Lines, right.Lines),
            Math.Max(left.Binning, right.Binning),
            Math.Max(left.PathRows, right.PathRows),
            Math.Max(left.PathTiles, right.PathTiles),
            Math.Max(left.SegCounts, right.SegCounts),
            Math.Max(left.Segments, right.Segments),
            Math.Max(left.BlendSpill, right.BlendSpill),
            Math.Max(left.Ptcl, right.Ptcl));

    /// <summary>
    /// Gets the last successful chunk height for a matching oversized render.
    /// </summary>
    /// <param name="binding">The binding category that selected chunked rendering.</param>
    /// <param name="targetSize">The target size being rendered.</param>
    /// <returns>The advisory chunk height, or <c>0</c> when no matching hint exists.</returns>
    private uint GetChunkTileHeightHint(WebGPUSceneDispatch.BindingLimitBuffer binding, Size targetSize)
    {
        int tileHeight = Volatile.Read(ref this.chunkHintTileHeight);
        if (tileHeight <= 0)
        {
            return 0;
        }

        if (Volatile.Read(ref this.chunkHintBinding) != (int)binding ||
            Volatile.Read(ref this.chunkHintTargetWidth) != targetSize.Width ||
            Volatile.Read(ref this.chunkHintTargetHeight) != targetSize.Height)
        {
            return 0;
        }

        return unchecked((uint)tileHeight);
    }

    /// <summary>
    /// Stores the last successful chunk height for later eager renders on this backend.
    /// </summary>
    /// <param name="binding">The binding category that selected chunked rendering.</param>
    /// <param name="targetSize">The target size being rendered.</param>
    /// <param name="tileHeight">The successful chunk height.</param>
    private void UpdateChunkTileHeightHint(
        WebGPUSceneDispatch.BindingLimitBuffer binding,
        Size targetSize,
        uint tileHeight)
    {
        Volatile.Write(ref this.chunkHintBinding, (int)binding);
        Volatile.Write(ref this.chunkHintTargetWidth, targetSize.Width);
        Volatile.Write(ref this.chunkHintTargetHeight, targetSize.Height);

        // Publish the height last so readers never observe a new non-zero hint before
        // its binding and target-size key have been written.
        Volatile.Write(ref this.chunkHintTileHeight, unchecked((int)tileHeight));
    }

    /// <summary>
    /// Rents the cached scene resource arena for a render, leaving the backend cache empty.
    /// </summary>
    /// <returns>The cached arena, or <see langword="null"/> when the cache slot is empty.</returns>
    internal WebGPUSceneResourceArena? RentResourceArena()
        => Interlocked.Exchange(ref this.cachedResourceArena, null);

    /// <summary>
    /// Rents the cached scheduling arena for a render, leaving the backend cache empty.
    /// </summary>
    /// <returns>The cached arena, or <see langword="null"/> when the cache slot is empty.</returns>
    internal WebGPUSceneSchedulingArena? RentSchedulingArena()
        => Interlocked.Exchange(ref this.cachedSchedulingArena, null);

    /// <summary>
    /// Returns reusable arenas to this backend cache.
    /// </summary>
    /// <param name="resourceArena">The scene resource arena to cache, or <see langword="null"/> when none was rented.</param>
    /// <param name="schedulingArena">The scheduling arena to cache, or <see langword="null"/> when none was rented.</param>
    internal void ReturnArenas(
        WebGPUSceneResourceArena? resourceArena,
        WebGPUSceneSchedulingArena? schedulingArena)
    {
        if (this.isDisposed)
        {
            WebGPUSceneSchedulingArena.Dispose(schedulingArena);
            WebGPUSceneResourceArena.Dispose(resourceArena);
            return;
        }

        // Null arenas mean this scene never reached that allocation stage; do not overwrite
        // a warm backend cache with null when disposing an unrendered or empty scene.
        if (schedulingArena is not null)
        {
            // The backend cache intentionally holds at most one arena of each kind.
            // A displaced arena means another thread returned a cache candidate first.
            WebGPUSceneSchedulingArena.Dispose(Interlocked.Exchange(ref this.cachedSchedulingArena, schedulingArena));
        }

        if (resourceArena is not null)
        {
            // The exchanged-out arena is no longer reachable from any render path, so it
            // must be released immediately instead of being left for final backend disposal.
            WebGPUSceneResourceArena.Dispose(Interlocked.Exchange(ref this.cachedResourceArena, resourceArena));
        }
    }

    /// <summary>
    /// Creates one transient composition texture that can be sampled from, storage-bound, and copied.
    /// The texture and view are tracked by the flush context, which owns their release.
    /// </summary>
    /// <param name="flushContext">The flush-scoped WebGPU device, queue, and encoder state.</param>
    /// <param name="width">The texture width in pixels.</param>
    /// <param name="height">The texture height in pixels.</param>
    /// <param name="texture">Receives the created texture on success.</param>
    /// <param name="textureView">Receives the created texture view on success.</param>
    /// <param name="error">Receives the failure reason when creation fails.</param>
    /// <returns><see langword="true"/> when the texture and view were created; otherwise <see langword="false"/>.</returns>
    internal static bool TryCreateCompositionTexture(
        WebGPUFlushContext flushContext,
        int width,
        int height,
        out Texture* texture,
        out TextureView* textureView,
        out string? error)
        => TryCreateCompositionTexture(flushContext, width, height, renderAttachment: false, out texture, out textureView, out error);

    /// <summary>
    /// Creates one transient composition texture that can be sampled from, storage-bound, and copied.
    /// When <paramref name="renderAttachment"/> is <see langword="true"/> the texture can also be used
    /// as a render-pass target, which scoped layers require for clearing. The texture and view are
    /// tracked by the flush context, which owns their release.
    /// </summary>
    /// <param name="flushContext">The flush-scoped WebGPU device, queue, and encoder state.</param>
    /// <param name="width">The texture width in pixels.</param>
    /// <param name="height">The texture height in pixels.</param>
    /// <param name="renderAttachment">Whether the texture also needs render-attachment usage.</param>
    /// <param name="texture">Receives the created texture on success.</param>
    /// <param name="textureView">Receives the created texture view on success.</param>
    /// <param name="error">Receives the failure reason when creation fails.</param>
    /// <returns><see langword="true"/> when the texture and view were created; otherwise <see langword="false"/>.</returns>
    private static bool TryCreateCompositionTexture(
        WebGPUFlushContext flushContext,
        int width,
        int height,
        bool renderAttachment,
        out Texture* texture,
        out TextureView* textureView,
        out string? error)
    {
        textureView = null;
        TextureUsage usage = TextureUsage.TextureBinding | TextureUsage.StorageBinding | TextureUsage.CopySrc | TextureUsage.CopyDst;

        if (renderAttachment)
        {
            usage |= TextureUsage.RenderAttachment;
        }

        TextureDescriptor textureDescriptor = new()
        {
            Usage = usage,
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

        // Ownership transfers to the flush context here; it releases both handles when disposed.
        flushContext.TrackTexture(texture);
        flushContext.TrackTextureView(textureView);
        error = null;
        return true;
    }

    /// <summary>
    /// Records one texture-to-texture region copy on the flush context's current command encoder.
    /// The copy executes when that encoder is next submitted.
    /// </summary>
    /// <param name="flushContext">The flush-scoped WebGPU device, queue, and encoder state.</param>
    /// <param name="sourceTexture">The texture copied from.</param>
    /// <param name="sourceOriginX">The source origin X in texels.</param>
    /// <param name="sourceOriginY">The source origin Y in texels.</param>
    /// <param name="destinationTexture">The texture copied to.</param>
    /// <param name="destinationOriginX">The destination origin X in texels.</param>
    /// <param name="destinationOriginY">The destination origin Y in texels.</param>
    /// <param name="width">The copy width in texels.</param>
    /// <param name="height">The copy height in texels.</param>
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
    /// Finishes and submits the flush context's current command encoder, if any.
    /// </summary>
    /// <param name="flushContext">The flush-scoped WebGPU device, queue, and encoder state.</param>
    /// <returns>
    /// <see langword="true"/> when there was nothing to submit or the submit succeeded; otherwise <see langword="false"/>.
    /// </returns>
    internal static bool TrySubmitPendingCommands(WebGPUFlushContext flushContext)
    {
        if (flushContext.CommandEncoder is null)
        {
            return true;
        }

        return TrySubmitWithIndex(flushContext, out _);
    }

    /// <summary>
    /// Finishes and submits the flush context's current command encoder and returns its submission index.
    /// </summary>
    /// <param name="flushContext">The flush-scoped WebGPU device, queue, and encoder state.</param>
    /// <param name="submissionIndex">Receives the queue submission index for the submitted command buffer.</param>
    /// <returns>
    /// <see langword="true"/> when the command buffer was submitted; otherwise <see langword="false"/>.
    /// </returns>
    internal static bool TrySubmitWithIndex(WebGPUFlushContext flushContext, out WrappedSubmissionIndex submissionIndex)
    {
        CommandEncoder* commandEncoder = flushContext.CommandEncoder;
        submissionIndex = default;

        if (commandEncoder is null)
        {
            return false;
        }

        // An encoder cannot be finished while a pass is still recording.
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
                Queue* queue = (Queue*)queueReference.Handle;
                ulong index = flushContext.WgpuExtension.QueueSubmitForIndex(queue, 1, ref commandBuffer);
                submissionIndex.Queue = queue;
                submissionIndex.SubmissionIndex = index;
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

        this.DiagnosticLastFlushUsedChunking = false;
        this.lastChunkingBindingFailure = WebGPUSceneDispatch.BindingLimitBuffer.None;
        this.isDisposed = true;

        // Retire any deferred readbacks first: their native map callbacks must fire (or time
        // out) before the pinned thunks and readback buffers can be released safely.
        this.HarvestPendingSchedulingStatuses(scene: null, drainAll: true);

        WebGPUSceneSchedulingArena.Dispose(Interlocked.Exchange(ref this.cachedSchedulingArena, null));
        WebGPUSceneResourceArena.Dispose(Interlocked.Exchange(ref this.cachedResourceArena, null));
    }

    /// <summary>
    /// Parks the deferred scheduling-status readback of a just-submitted flush on this backend
    /// for a later flush to harvest. Offscreen targets pass a corrective re-render and hold a
    /// deferred-render reference on their scene so the correction remains possible until the
    /// readback resolves; presentation surfaces are corrected by their next redraw and pass
    /// <see langword="null"/>.
    /// </summary>
    /// <param name="pendingStatus">The pending readback to park.</param>
    /// <param name="scene">The retained scene the flush rendered.</param>
    /// <param name="correctiveRender">The corrective re-render to invoke on overflow, or <see langword="null"/> for presentation flushes.</param>
    private void EnqueuePendingSchedulingStatus(
        WebGPUPendingSchedulingStatus pendingStatus,
        WebGPUDrawingBackendScene scene,
        Action? correctiveRender)
    {
        if (correctiveRender is not null)
        {
            scene.AddDeferredRenderReference();
        }

        PendingSchedulingStatusEntry entry = new(
            pendingStatus,
            correctiveRender is null ? null : scene,
            correctiveRender);

        lock (this.pendingStatusSync)
        {
            this.pendingSchedulingStatuses.Add(entry);
        }
    }

    /// <summary>
    /// Resolves parked deferred readbacks. Completed maps resolve wait-free; unfinished maps
    /// stay parked unless the queue exceeds its bound (or <paramref name="drainAll"/> is set),
    /// where the oldest entries are resolved blocking as GPU backpressure. Observed demand is
    /// merged into the backend capacities and the scene about to render, and offscreen entries
    /// that overflowed are re-rendered with the grown capacities so no incomplete content can
    /// persist in a texture the app may cache.
    /// </summary>
    /// <param name="scene">The scene about to render, or <see langword="null"/> during teardown or readback drains.</param>
    /// <param name="drainAll">Whether to resolve every parked entry regardless of readiness.</param>
    private void HarvestPendingSchedulingStatuses(WebGPUDrawingBackendScene? scene, bool drainAll)
    {
        List<PendingSchedulingStatusEntry>? due = null;
        lock (this.pendingStatusSync)
        {
            if (this.pendingSchedulingStatuses.Count == 0)
            {
                return;
            }

            // Map callbacks only advance while the device is pumped; one non-blocking poll
            // delivers every callback that has already completed on the GPU timeline.
            this.pendingSchedulingStatuses[0].Status.PollDevice();

            int overflow = this.pendingSchedulingStatuses.Count - MaxPendingSchedulingStatuses;
            for (int i = this.pendingSchedulingStatuses.Count - 1; i >= 0; i--)
            {
                PendingSchedulingStatusEntry entry = this.pendingSchedulingStatuses[i];

                // Entries are ordered oldest-first, so indexes below the overflow count are
                // exactly the oldest entries beyond the queue bound.
                if (drainAll || entry.Status.IsReady || i < overflow)
                {
                    (due ??= []).Add(entry);
                    this.pendingSchedulingStatuses.RemoveAt(i);
                }
            }
        }

        if (due is null)
        {
            return;
        }

        foreach (PendingSchedulingStatusEntry entry in due)
        {
            try
            {
                bool overflowed = WebGPUSceneDispatch.ResolveDeferredSchedulingStatus(entry.Status, out WebGPUSceneBumpSizes resolvedBumpSizes);
                scene?.UpdateBumpSizes(resolvedBumpSizes);
                entry.Scene?.UpdateBumpSizes(resolvedBumpSizes);
                this.bumpSizes = MaxBumpSizes(this.bumpSizes, resolvedBumpSizes);

                if (overflowed)
                {
                    entry.CorrectiveRender?.Invoke();
                }
            }
            finally
            {
                entry.Scene?.ReleaseDeferredRenderReference();
            }
        }
    }

    /// <summary>
    /// Re-renders an offscreen flush whose deferred readback reported a scratch overflow.
    /// Runs the staged pipeline synchronously with the grown capacities so the corrected
    /// content replaces the incomplete render before anything can observe it as final. A
    /// target whose owner was disposed in the meantime is skipped: its content is gone too.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format of the flush target.</typeparam>
    /// <param name="configuration">The processing configuration of the original flush.</param>
    /// <param name="webGPUTarget">The native surface holding the target texture handles.</param>
    /// <param name="targetBounds">The target bounds of the original flush.</param>
    /// <param name="textureFormat">The target texture format.</param>
    /// <param name="requiredFeature">The device feature required by the target format, or <see cref="FeatureName.Undefined"/>.</param>
    /// <param name="webGPUScene">The retained scene to replay, kept alive by a deferred-render reference.</param>
    private void RenderCorrective<TPixel>(
        Configuration configuration,
        WebGPUNativeSurface webGPUTarget,
        Rectangle targetBounds,
        TextureFormat textureFormat,
        FeatureName requiredFeature,
        WebGPUDrawingBackendScene webGPUScene)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        WebGPUSceneBumpSizes currentBumpSizes = webGPUScene.BumpSizes;
        WebGPUSceneResourceArena? resourceArena = webGPUScene.RentResourceArena() ?? this.RentResourceArena();
        WebGPUSceneSchedulingArena? schedulingArena = webGPUScene.RentSchedulingArena() ?? this.RentSchedulingArena();

        try
        {
            this.RenderEncodedSceneWithGrowth<TPixel>(
                configuration,
                webGPUTarget,
                targetBounds,
                textureFormat,
                requiredFeature,
                webGPUScene,
                deferOverflowCheck: false,
                ref currentBumpSizes,
                ref resourceArena,
                ref schedulingArena);

            webGPUScene.UpdateBumpSizes(currentBumpSizes);
            this.bumpSizes = MaxBumpSizes(this.bumpSizes, currentBumpSizes);
        }
        catch (ObjectDisposedException)
        {
            // The target texture handles were released between the flush and the harvest;
            // the content the correction would repaint no longer exists.
        }
        catch (InvalidOperationException)
        {
            // Handle acquisition or device validation failed for the retired target; skip.
        }
        finally
        {
            webGPUScene.ReturnArenas(resourceArena, schedulingArena, this);
        }
    }

    /// <summary>
    /// Throws <see cref="ObjectDisposedException"/> when this backend is disposed.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(this.isDisposed, this);

    /// <summary>
    /// One parked deferred readback and the corrective context for its flush.
    /// </summary>
    private readonly struct PendingSchedulingStatusEntry
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="PendingSchedulingStatusEntry"/> struct.
        /// </summary>
        /// <param name="status">The deferred scheduling-status readback.</param>
        /// <param name="scene">The rendered scene, held alive via a deferred-render reference; <see langword="null"/> for presentation flushes.</param>
        /// <param name="correctiveRender">The corrective re-render invoked when the readback reports overflow; <see langword="null"/> for presentation flushes.</param>
        public PendingSchedulingStatusEntry(
            WebGPUPendingSchedulingStatus status,
            WebGPUDrawingBackendScene? scene,
            Action? correctiveRender)
        {
            this.Status = status;
            this.Scene = scene;
            this.CorrectiveRender = correctiveRender;
        }

        /// <summary>
        /// Gets the deferred scheduling-status readback.
        /// </summary>
        public WebGPUPendingSchedulingStatus Status { get; }

        /// <summary>
        /// Gets the rendered scene, held alive via a deferred-render reference;
        /// <see langword="null"/> for presentation flushes.
        /// </summary>
        public WebGPUDrawingBackendScene? Scene { get; }

        /// <summary>
        /// Gets the corrective re-render invoked when the readback reports overflow;
        /// <see langword="null"/> for presentation flushes.
        /// </summary>
        public Action? CorrectiveRender { get; }
    }
}
