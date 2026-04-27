// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Runtime.CompilerServices;
using Silk.NET.WebGPU;
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

    // Cached arenas for cross-flush buffer reuse. Rented via Interlocked.Exchange at flush
    // start and returned at flush end so parallel flushes on different threads don't contend.
    private WebGPUSceneSchedulingArena? cachedSchedulingArena;
    private WebGPUSceneResourceArena? cachedResourceArena;
    private WebGPUSceneDispatch.BindingLimitBuffer lastChunkingBindingFailure;
    private bool isDisposed;

    private static readonly Dictionary<Type, CompositePixelRegistration> CompositePixelHandlers = CreateCompositePixelHandlers();

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
    public bool DiagnosticLastFlushUsedChunking { get; private set; }

    /// <summary>
    /// Gets the binding category that selected chunked rendering for the last WebGPU flush.
    /// </summary>
    /// <remarks>
    /// This value describes only the most recent flush on this backend instance. When the most recent flush did not use
    /// chunked rendering, this property returns <c>None</c>.
    /// </remarks>
    public string DiagnosticLastChunkingBindingFailure => this.lastChunkingBindingFailure.ToString();

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

        this.DiagnosticLastFlushUsedChunking = false;
        this.lastChunkingBindingFailure = WebGPUSceneDispatch.BindingLimitBuffer.None;
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
                if (!WebGPUSceneDispatch.TryCreateStagedScene(
                        configuration,
                        target,
                        compositionScene,
                        currentBumpSizes,
                        ref resourceArena,
                        out bool exceedsBindingLimit,
                        out _,
                        out WebGPUStagedScene stagedScene,
                        out string? error))
                {
                    throw new InvalidOperationException(
                        exceedsBindingLimit
                            ? error ?? "The staged WebGPU scene exceeded the current binding limits."
                            : error ?? "Failed to create the staged WebGPU scene.");
                }

                try
                {
                    this.DiagnosticLastFlushUsedChunking = stagedScene.BindingLimitFailure.Buffer != WebGPUSceneDispatch.BindingLimitBuffer.None;
                    this.lastChunkingBindingFailure = stagedScene.BindingLimitFailure.Buffer;

                    if (stagedScene.EncodedScene.FillCount == 0)
                    {
                        return;
                    }

                    bool renderSucceeded = WebGPUSceneDispatch.TryRenderStagedScene(
                        ref stagedScene,
                        ref schedulingArena,
                        out bool requiresGrowth,
                        out WebGPUSceneBumpSizes grownBumpSizes,
                        out error);

                    if (renderSucceeded)
                    {
                        // Persist GPU-reported actual usage for next flush.
                        this.bumpSizes = grownBumpSizes;
                        return;
                    }

                    if (requiresGrowth)
                    {
                        // Bump overflow; retry with GPU-reported sizes.
                        currentBumpSizes = grownBumpSizes;
                        continue;
                    }

                    throw new InvalidOperationException(error ?? "The staged WebGPU scene dispatch failed.");
                }
                finally
                {
                    stagedScene.Dispose();
                }
            }

            throw new InvalidOperationException("The staged WebGPU scene exceeded the current dynamic growth retry budget.");
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

        this.DiagnosticLastFlushUsedChunking = false;
        this.lastChunkingBindingFailure = WebGPUSceneDispatch.BindingLimitBuffer.None;
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
