// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Runtime.CompilerServices;
using Silk.NET.WebGPU;
using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

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
    /// chunked rendering, this property returns <c>None</c>.
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

        WebGPUSceneBumpSizes currentBumpSizes = webGPUScene.BumpSizes;
        WebGPUSceneResourceArena? resourceArena = null;
        WebGPUSceneSchedulingArena? schedulingArena = null;

        try
        {
            WebGPUEncodedScene encodedScene = webGPUScene.EncodedScene;
            if (encodedScene.HasOperations)
            {
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

                this.RenderOperations<TPixel>(
                    configuration,
                    flushContext,
                    rootTarget,
                    encodedScene,
                    encodedScene.Operations,
                    0,
                    encodedScene.Operations.Count,
                    requiredFeature,
                    ref currentBumpSizes,
                    ref resourceArena,
                    ref schedulingArena);

                webGPUScene.UpdateBumpSizes(currentBumpSizes);
                this.bumpSizes = currentBumpSizes;
                return;
            }

            if (encodedScene.FillCount != 0)
            {
                // Scene arenas are rented once for the render. If a concurrent render
                // owns them, the backend cache supplies independent scratch buffers.
                resourceArena ??= webGPUScene.RentResourceArena() ?? this.RentResourceArena();
                schedulingArena ??= webGPUScene.RentSchedulingArena() ?? this.RentSchedulingArena();

                bool renderCompleted = false;

                // Retry loop: scratch allocators start small and the GPU reports actual demand.
                // The retained scene keeps the largest observed size so later renders avoid
                // rediscovering the same growth.
                for (int attempt = 0; attempt < MaxDynamicGrowthAttempts; attempt++)
                {
                    WebGPUStagedScene stagedScene = WebGPUSceneDispatch.CreateStagedScene(
                        configuration,
                        nativeTarget,
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
                            out bool requiresGrowth,
                            out WebGPUSceneBumpSizes grownBumpSizes,
                            out uint successfulChunkTileHeight,
                            out string? error);

                        if (renderSucceeded)
                        {
                            currentBumpSizes = MaxBumpSizes(currentBumpSizes, grownBumpSizes);

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

            webGPUScene.UpdateBumpSizes(currentBumpSizes);
            this.bumpSizes = currentBumpSizes;
        }
        finally
        {
            webGPUScene.ReturnArenas(resourceArena, schedulingArena, this);
        }
    }

    private void RenderOperations<TPixel>(
        Configuration configuration,
        WebGPUFlushContext flushContext,
        WebGPUSceneTarget target,
        WebGPUEncodedScene encodedScene,
        IReadOnlyList<WebGPUSceneOperation> operations,
        int operationStart,
        int operationEnd,
        FeatureName requiredFeature,
        ref WebGPUSceneBumpSizes currentBumpSizes,
        ref WebGPUSceneResourceArena? resourceArena,
        ref WebGPUSceneSchedulingArena? schedulingArena)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        for (int i = operationStart; i < operationEnd; i++)
        {
            WebGPUSceneOperation operation = operations[i];
            switch (operation.Kind)
            {
                case WebGPUSceneOperationKind.RenderRange:
                    this.RenderEncodedRange<TPixel>(
                        configuration,
                        flushContext,
                        target,
                        encodedScene,
                        operation.Range,
                        requiredFeature,
                        externalTextureView: null,
                        ref currentBumpSizes,
                        ref resourceArena,
                        ref schedulingArena);
                    break;

                case WebGPUSceneOperationKind.Apply:
                    this.ExecuteApplyOperation<TPixel>(
                        configuration,
                        flushContext,
                        target,
                        encodedScene,
                        operation.Apply!,
                        requiredFeature,
                        ref currentBumpSizes,
                        ref resourceArena,
                        ref schedulingArena);
                    break;

                case WebGPUSceneOperationKind.ScopedLayer:
                    int childOperationStart = i + 1;

                    this.ExecuteScopedLayerOperation<TPixel>(
                        configuration,
                        flushContext,
                        target,
                        encodedScene,
                        operations,
                        childOperationStart,
                        operation.Layer!,
                        requiredFeature,
                        ref currentBumpSizes,
                        ref resourceArena,
                        ref schedulingArena);

                    i += operation.Layer!.OperationCount;
                    break;
            }
        }
    }

    private void ExecuteScopedLayerOperation<TPixel>(
        Configuration configuration,
        WebGPUFlushContext flushContext,
        WebGPUSceneTarget parentTarget,
        WebGPUEncodedScene encodedScene,
        IReadOnlyList<WebGPUSceneOperation> operations,
        int childOperationStart,
        WebGPUScopedLayerSceneItem layer,
        FeatureName requiredFeature,
        ref WebGPUSceneBumpSizes currentBumpSizes,
        ref WebGPUSceneResourceArena? resourceArena,
        ref WebGPUSceneSchedulingArena? schedulingArena)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        if (!TryCreateCompositionTexture(flushContext, layer.Bounds.Width, layer.Bounds.Height, renderAttachment: true, out Texture* layerTexture, out TextureView* layerTextureView, out string? error))
        {
            throw new InvalidOperationException(error);
        }

        ClearTarget(flushContext, layerTextureView);

        WebGPUSceneTarget layerTarget = new(
            layerTexture,
            layerTextureView,
            layer.Bounds,
            new Point(-layer.Bounds.X, -layer.Bounds.Y));

        this.RenderOperations<TPixel>(
            configuration,
            flushContext,
            layerTarget,
            encodedScene,
            operations,
            childOperationStart,
            childOperationStart + layer.OperationCount,
            requiredFeature,
            ref currentBumpSizes,
            ref resourceArena,
            ref schedulingArena);

        this.RenderEncodedRange<TPixel>(
            configuration,
            flushContext,
            parentTarget,
            encodedScene,
            layer.CompositeRange,
            requiredFeature,
            layerTextureView,
            ref currentBumpSizes,
            ref resourceArena,
            ref schedulingArena);
    }

    private void ExecuteApplyOperation<TPixel>(
        Configuration configuration,
        WebGPUFlushContext flushContext,
        WebGPUSceneTarget target,
        WebGPUEncodedScene encodedScene,
        WebGPUApplySceneItem apply,
        FeatureName requiredFeature,
        ref WebGPUSceneBumpSizes currentBumpSizes,
        ref WebGPUSceneResourceArena? resourceArena,
        ref WebGPUSceneSchedulingArena? schedulingArena)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        if (!TrySubmit(flushContext))
        {
            throw new InvalidOperationException("Failed to submit WebGPU work before Apply readback.");
        }

        using Image<TPixel> sourceImage = new(configuration, apply.SourceRect.Width, apply.SourceRect.Height);
        ReadTextureRegion(flushContext, target, apply.SourceRect, sourceImage.Frames.RootFrame.PixelBuffer.GetRegion());
        sourceImage.Mutate(apply.Operation);

        if (!TryCreateCompositionTexture(flushContext, apply.SourceRect.Width, apply.SourceRect.Height, out Texture* texture, out TextureView* textureView, out string? error))
        {
            throw new InvalidOperationException(error);
        }

        using (WebGPUHandle.HandleReference queueReference = flushContext.QueueHandle.AcquireReference())
        {
            WebGPUFlushContext.UploadTextureFromRegion(
                flushContext.Api,
                (Queue*)queueReference.Handle,
                texture,
                sourceImage.Frames.RootFrame.PixelBuffer.GetRegion(),
                configuration.MemoryAllocator);
        }

        this.RenderEncodedRange<TPixel>(
            configuration,
            flushContext,
            target,
            encodedScene,
            apply.DrawRange,
            requiredFeature,
            textureView,
            ref currentBumpSizes,
            ref resourceArena,
            ref schedulingArena);
    }

    private void RenderEncodedRange<TPixel>(
        Configuration configuration,
        WebGPUFlushContext flushContext,
        WebGPUSceneTarget target,
        WebGPUEncodedScene encodedScene,
        WebGPUSceneRange range,
        FeatureName requiredFeature,
        TextureView* externalTextureView,
        ref WebGPUSceneBumpSizes currentBumpSizes,
        ref WebGPUSceneResourceArena? resourceArena,
        ref WebGPUSceneSchedulingArena? schedulingArena)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        if (range.DrawTagCount == 0)
        {
            return;
        }

        bool renderCompleted = false;
        for (int attempt = 0; attempt < MaxDynamicGrowthAttempts; attempt++)
        {
            WebGPUStagedScene stagedScene = WebGPUSceneDispatch.CreateStagedScene<TPixel>(
                flushContext,
                encodedScene,
                range,
                requiredFeature,
                currentBumpSizes,
                externalTextureView,
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
                    target,
                    ref schedulingArena,
                    this.GetChunkTileHeightHint(stagedScene.BindingLimitFailure.Buffer, target.Bounds.Size),
                    out bool requiresGrowth,
                    out WebGPUSceneBumpSizes grownBumpSizes,
                    out uint successfulChunkTileHeight,
                    out string? error);

                if (renderSucceeded)
                {
                    currentBumpSizes = MaxBumpSizes(currentBumpSizes, grownBumpSizes);

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
    internal WebGPUSceneResourceArena? RentResourceArena()
        => Interlocked.Exchange(ref this.cachedResourceArena, null);

    /// <summary>
    /// Rents the cached scheduling arena for a render, leaving the backend cache empty.
    /// </summary>
    internal WebGPUSceneSchedulingArena? RentSchedulingArena()
        => Interlocked.Exchange(ref this.cachedSchedulingArena, null);

    /// <summary>
    /// Returns reusable arenas to this backend cache.
    /// </summary>
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
    /// Creates one transient composition texture that can be rendered to, sampled from, and copied.
    /// </summary>
    internal static bool TryCreateCompositionTexture(
        WebGPUFlushContext flushContext,
        int width,
        int height,
        out Texture* texture,
        out TextureView* textureView,
        out string? error)
        => TryCreateCompositionTexture(flushContext, width, height, renderAttachment: false, out texture, out textureView, out error);

    /// <summary>
    /// Creates one transient composition texture that can be rendered to, sampled from, and copied.
    /// </summary>
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

        this.DiagnosticLastFlushUsedChunking = false;
        this.lastChunkingBindingFailure = WebGPUSceneDispatch.BindingLimitBuffer.None;
        this.isDisposed = true;

        WebGPUSceneSchedulingArena.Dispose(Interlocked.Exchange(ref this.cachedSchedulingArena, null));
        WebGPUSceneResourceArena.Dispose(Interlocked.Exchange(ref this.cachedResourceArena, null));
    }

    /// <summary>
    /// Throws <see cref="ObjectDisposedException"/> when this backend is disposed.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(this.isDisposed, this);
}
