// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Buffers;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Silk.NET.WebGPU;
using Silk.NET.WebGPU.Extensions.WGPU;
using SixLabors.ImageSharp.Drawing.Shapes.Rasterization;
using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.PixelFormats;
using WgpuBuffer = Silk.NET.WebGPU.Buffer;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// WebGPU-backed implementation of <see cref="IDrawingBackend"/>.
/// </summary>
/// <remarks>
/// <para>
/// This backend executes coverage generation and composition on WebGPU where possible and falls back to
/// <see cref="DefaultDrawingBackend"/> when GPU execution is unavailable for a specific command set.
/// </para>
/// <para>
/// High-level flush pipeline:
/// </para>
/// <code>
/// CompositionScene
///   -> CompositionScenePlanner (prepared batches)
///   -> For each batch:
///        1) Resolve pixel-format handler
///        2) Acquire flush context (shared session when possible)
///        3) Prepare/reuse GPU coverage for path definition
///        4) Composite commands via tiled compute shader into destination pixel buffer
///        5) Blit to target and optionally read back to CPU region
///        6) On failure: delegate batch to DefaultDrawingBackend
/// </code>
/// <para>
/// Shared flush sessions allow multiple contiguous GPU-compatible batches to reuse destination initialization
/// and transient GPU resources for one scene flush.
/// </para>
/// <para>
/// See src/ImageSharp.Drawing.WebGPU/WEBGPU_BACKEND_PROCESS.md for a full process walkthrough.
/// </para>
/// </remarks>
internal sealed unsafe partial class WebGPUDrawingBackend : IDrawingBackend, IDisposable
{
    private const uint CompositeVertexCount = 6;
    private const int CompositeComputeWorkgroupSize = 8;
    private const int CompositeDestinationPixelStride = 16;
    private const int CallbackTimeoutMilliseconds = 10_000;

    private readonly DefaultDrawingBackend fallbackBackend;
    private bool isDisposed;

    private static readonly Dictionary<Type, CompositePixelRegistration> CompositePixelHandlers = CreateCompositePixelHandlers();
    private static int nextSceneFlushId;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebGPUDrawingBackend"/> class.
    /// </summary>
    public WebGPUDrawingBackend()
        => this.fallbackBackend = DefaultDrawingBackend.Instance;

    /// <summary>
    /// Gets the testing-only diagnostic counter for total coverage preparation requests.
    /// </summary>
    internal int TestingPrepareCoverageCallCount { get; private set; }

    /// <summary>
    /// Gets the testing-only diagnostic counter for coverage preparations executed on the GPU.
    /// </summary>
    internal int TestingGPUPrepareCoverageCallCount { get; private set; }

    /// <summary>
    /// Gets the testing-only diagnostic counter for coverage preparations delegated to the fallback backend.
    /// </summary>
    internal int TestingFallbackPrepareCoverageCallCount { get; private set; }

    /// <summary>
    /// Gets the testing-only diagnostic counter for total composition requests.
    /// </summary>
    internal int TestingCompositeCoverageCallCount { get; private set; }

    /// <summary>
    /// Gets the testing-only diagnostic counter for compositions executed on the GPU.
    /// </summary>
    internal int TestingGPUCompositeCoverageCallCount { get; private set; }

    /// <summary>
    /// Gets the testing-only diagnostic counter for compositions delegated to the fallback backend.
    /// </summary>
    internal int TestingFallbackCompositeCoverageCallCount { get; private set; }

    /// <summary>
    /// Gets the testing-only diagnostic counter for completed prepared-coverage uses.
    /// </summary>
    internal int TestingReleaseCoverageCallCount { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the testing-only diagnostic indicates the backend completed GPU initialization.
    /// </summary>
    internal bool TestingIsGPUReady { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the testing-only diagnostic indicates GPU initialization has been attempted.
    /// </summary>
    internal bool TestingGPUInitializationAttempted { get; private set; }

    /// <summary>
    /// Gets the testing-only diagnostic containing the last GPU initialization failure reason, if any.
    /// </summary>
    internal string? TestingLastGPUInitializationFailure { get; private set; }

    /// <summary>
    /// Gets the testing-only diagnostic counter for live prepared coverage handles currently in use.
    /// </summary>
    internal int TestingLiveCoverageCount { get; private set; }

    /// <summary>
    /// Gets the testing-only diagnostic counter for composition batches that used
    /// the compute composition path.
    /// </summary>
    internal int TestingComputePathBatchCount { get; private set; }

    /// <summary>
    /// Attempts to expose native WebGPU device and queue handles for interop.
    /// </summary>
    /// <param name="deviceHandle">Receives the device pointer when available.</param>
    /// <param name="queueHandle">Receives the queue pointer when available.</param>
    /// <returns><see langword="true"/> when both handles are available; otherwise <see langword="false"/>.</returns>
    internal bool TryGetInteropHandles(out nint deviceHandle, out nint queueHandle)
    {
        this.ThrowIfDisposed();
        if (WebGPUFlushContext.TryGetInteropHandles(out deviceHandle, out queueHandle, out string? error))
        {
            this.TestingGPUInitializationAttempted = true;
            this.TestingIsGPUReady = true;
            this.TestingLastGPUInitializationFailure = null;
            return true;
        }

        this.TestingGPUInitializationAttempted = true;
        this.TestingIsGPUReady = false;
        this.TestingLastGPUInitializationFailure = error;
        return false;
    }

    /// <inheritdoc />
    public void FillPath<TPixel>(
        ICanvasFrame<TPixel> target,
        IPath path,
        Brush brush,
        GraphicsOptions graphicsOptions,
        in RasterizerOptions rasterizerOptions,
        DrawingCanvasBatcher<TPixel> batcher)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        this.ThrowIfDisposed();
        batcher.AddComposition(
            CompositionCommand.Create(
                path,
                brush,
                graphicsOptions,
                rasterizerOptions,
                target.Bounds.Location));
    }

    /// <inheritdoc />
    public bool IsCompositionBrushSupported<TPixel>(Brush brush)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        this.ThrowIfDisposed();
        return IsSupportedCompositionBrush(brush);
    }

    /// <inheritdoc />
    public void FlushCompositions<TPixel>(
        Configuration configuration,
        ICanvasFrame<TPixel> target,
        CompositionScene compositionScene)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        this.ThrowIfDisposed();
        if (compositionScene.Commands.Count == 0)
        {
            return;
        }

        List<CompositionBatch> preparedBatches = CompositionScenePlanner.CreatePreparedBatches(
            compositionScene.Commands,
            target.Bounds);
        if (preparedBatches.Count == 0)
        {
            return;
        }

        // Shared flush sessions are used only when every command brush is directly supported by GPU composition.
        bool supportsSharedFlush = true;
        for (int i = 0; i < compositionScene.Commands.Count; i++)
        {
            if (!IsSupportedCompositionBrush(compositionScene.Commands[i].Brush))
            {
                supportsSharedFlush = false;
                break;
            }
        }

        int flushId = supportsSharedFlush ? Interlocked.Increment(ref nextSceneFlushId) : 0;
        for (int i = 0; i < preparedBatches.Count; i++)
        {
            CompositionBatch batch = preparedBatches[i];
            this.FlushPreparedBatch(
                configuration,
                target,
                new CompositionBatch(
                    batch.Definition,
                    batch.Commands,
                    flushId,
                    isFinalBatchInFlush: i == preparedBatches.Count - 1));
        }
    }

    /// <summary>
    /// Executes one prepared composition batch, preferring GPU execution and falling back to CPU when required.
    /// </summary>
    /// <typeparam name="TPixel">The destination pixel format.</typeparam>
    /// <param name="configuration">The active processing configuration.</param>
    /// <param name="target">The destination frame.</param>
    /// <param name="compositionBatch">The prepared batch to execute.</param>
    private void FlushPreparedBatch<TPixel>(
        Configuration configuration,
        ICanvasFrame<TPixel> target,
        CompositionBatch compositionBatch)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        this.ThrowIfDisposed();
        if (compositionBatch.Commands.Count == 0)
        {
            return;
        }

        int commandCount = compositionBatch.Commands.Count;
        this.TestingPrepareCoverageCallCount++;
        this.TestingReleaseCoverageCallCount++;
        this.TestingCompositeCoverageCallCount += commandCount;

        bool hasCpuRegion = target.TryGetCpuRegion(out Buffer2DRegion<TPixel> cpuRegion);
        if (compositionBatch.FlushId == 0 && !AreAllCompositionBrushesSupported(compositionBatch.Commands))
        {
            this.TestingFallbackPrepareCoverageCallCount++;
            this.TestingFallbackCompositeCoverageCallCount += commandCount;
            this.FlushCompositionsFallback(configuration, target, compositionBatch, hasCpuRegion);
            return;
        }

        if (!CompositePixelHandlers.TryGetValue(typeof(TPixel), out CompositePixelRegistration pixelHandler))
        {
            this.TestingFallbackPrepareCoverageCallCount++;
            this.TestingFallbackCompositeCoverageCallCount += commandCount;
            this.FlushCompositionsFallback(configuration, target, compositionBatch, hasCpuRegion);
            return;
        }

        // Flush sessions keep destination state alive across batch boundaries for one scene flush.
        bool useFlushSession = compositionBatch.FlushId != 0;
        bool gpuSuccess = false;
        bool gpuReady = false;
        string? failure = null;
        bool hadExistingSession = false;
        WebGPUFlushContext? flushContext = null;

        try
        {
            flushContext = useFlushSession
                ? WebGPUFlushContext.GetOrCreateFlushSessionContext(
                    compositionBatch.FlushId,
                    target,
                    pixelHandler.TextureFormat,
                    pixelHandler.PixelSizeInBytes,
                    out hadExistingSession)
                : WebGPUFlushContext.Create(target, pixelHandler.TextureFormat, pixelHandler.PixelSizeInBytes);

            CompositionCoverageDefinition definition = compositionBatch.Definition;
            if (TryPrepareGpuCoverage(
                    flushContext,
                    in definition,
                    out WebGPUFlushContext.CoverageEntry? coverageEntry,
                    out failure))
            {
                gpuReady = true;
                gpuSuccess = this.TryCompositeBatchTiled<TPixel>(
                    flushContext,
                    coverageEntry.GPUCoverageView,
                    compositionBatch.Commands,
                    blitToTarget: !useFlushSession || compositionBatch.IsFinalBatchInFlush,
                    out failure);
                if (gpuSuccess)
                {
                    if (useFlushSession && !compositionBatch.IsFinalBatchInFlush)
                    {
                        // Intermediate session batches defer final submit/readback until the last batch.
                    }
                    else
                    {
                        gpuSuccess = this.TryFinalizeFlush(flushContext, cpuRegion);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            failure = ex.Message;
            gpuSuccess = false;
        }
        finally
        {
            if (!useFlushSession)
            {
                flushContext?.Dispose();
            }
        }

        this.TestingGPUInitializationAttempted = true;
        this.TestingIsGPUReady = gpuReady;
        this.TestingLastGPUInitializationFailure = gpuSuccess ? null : failure;
        this.TestingLiveCoverageCount = 0;

        if (useFlushSession)
        {
            if (gpuSuccess)
            {
                this.TestingGPUPrepareCoverageCallCount++;
                this.TestingGPUCompositeCoverageCallCount += commandCount;
                if (compositionBatch.IsFinalBatchInFlush)
                {
                    WebGPUFlushContext.CompleteFlushSession(compositionBatch.FlushId);
                }

                return;
            }

            WebGPUFlushContext.CompleteFlushSession(compositionBatch.FlushId);
            if (hadExistingSession)
            {
                throw new InvalidOperationException($"WebGPU flush session failed after prior GPU batches. Reason: {failure ?? "Unknown error"}");
            }

            this.TestingFallbackPrepareCoverageCallCount++;
            this.TestingFallbackCompositeCoverageCallCount += commandCount;
            this.FlushCompositionsFallback(configuration, target, compositionBatch, hasCpuRegion);
            return;
        }

        if (gpuSuccess)
        {
            this.TestingGPUPrepareCoverageCallCount++;
            this.TestingGPUCompositeCoverageCallCount += commandCount;
            return;
        }

        this.TestingFallbackPrepareCoverageCallCount++;
        this.TestingFallbackCompositeCoverageCallCount += commandCount;
        this.FlushCompositionsFallback(configuration, target, compositionBatch, hasCpuRegion);
    }

    /// <summary>
    /// Checks whether all prepared commands in the batch are directly composable by WebGPU.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool AreAllCompositionBrushesSupported(IReadOnlyList<PreparedCompositionCommand> commands)
    {
        for (int i = 0; i < commands.Count; i++)
        {
            if (!IsSupportedCompositionBrush(commands[i].Brush))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Checks whether the brush type is supported by the WebGPU composition path.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsSupportedCompositionBrush(Brush brush) => brush is SolidBrush or ImageBrush;

    /// <summary>
    /// Executes one prepared batch on the CPU fallback backend.
    /// </summary>
    /// <typeparam name="TPixel">The destination pixel format.</typeparam>
    /// <param name="configuration">The active processing configuration.</param>
    /// <param name="target">The original destination frame.</param>
    /// <param name="compositionBatch">The prepared batch to execute.</param>
    /// <param name="hasCpuRegion">
    /// Indicates whether <paramref name="target"/> exposes CPU pixels directly. When <see langword="false"/>,
    /// a temporary staging frame is composed and uploaded to the native surface.
    /// </param>
    private void FlushCompositionsFallback<TPixel>(
        Configuration configuration,
        ICanvasFrame<TPixel> target,
        CompositionBatch compositionBatch,
        bool hasCpuRegion)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        if (hasCpuRegion)
        {
            this.fallbackBackend.FlushPreparedBatch(configuration, target, compositionBatch);
            return;
        }

        Rectangle targetBounds = target.Bounds;
        using WebGPUFlushContext.FallbackStagingLease<TPixel> stagingLease =
            WebGPUFlushContext.RentFallbackStaging<TPixel>(configuration.MemoryAllocator, in targetBounds);

        Buffer2DRegion<TPixel> stagingRegion = stagingLease.Region;
        ICanvasFrame<TPixel> stagingFrame = new CpuCanvasFrame<TPixel>(stagingRegion);
        this.fallbackBackend.FlushPreparedBatch(configuration, stagingFrame, compositionBatch);

        using WebGPUFlushContext uploadContext = WebGPUFlushContext.CreateUploadContext(target);
        WebGPUFlushContext.UploadTextureFromRegion(
            uploadContext.Api,
            uploadContext.Queue,
            uploadContext.TargetTexture,
            stagingRegion);
    }

    /// <summary>
    /// Resolves (or creates) cached GPU coverage for the batch definition.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryPrepareGpuCoverage(
        WebGPUFlushContext flushContext,
        in CompositionCoverageDefinition definition,
        [NotNullWhen(true)] out WebGPUFlushContext.CoverageEntry? coverageEntry,
        out string? error)
    {
        lock (flushContext.DeviceState.SyncRoot)
        {
            return flushContext.DeviceState.TryGetOrCreateCoverageEntry(
                in definition,
                flushContext.Queue,
                out coverageEntry,
                out error);
        }
    }

    /// <summary>
    /// Allocates destination storage used by compute composition.
    /// </summary>
    private static bool TryCreateDestinationPixelsBuffer(
        WebGPUFlushContext flushContext,
        int width,
        int height,
        out WgpuBuffer* destinationPixelsBuffer,
        out nuint destinationPixelsByteSize,
        out string? error)
    {
        destinationPixelsByteSize = checked((nuint)width * (nuint)height * CompositeDestinationPixelStride);
        BufferDescriptor descriptor = new()
        {
            Usage = BufferUsage.Storage,
            Size = destinationPixelsByteSize
        };

        destinationPixelsBuffer = flushContext.Api.DeviceCreateBuffer(flushContext.Device, in descriptor);
        if (destinationPixelsBuffer is null)
        {
            error = "Failed to create destination pixel storage buffer.";
            return false;
        }

        flushContext.TrackBuffer(destinationPixelsBuffer);
        error = null;
        return true;
    }

    /// <summary>
    /// Initializes destination storage from the current destination texture contents.
    /// </summary>
    private static bool TryInitializeDestinationPixels(
        WebGPUFlushContext flushContext,
        TextureView* sourceTextureView,
        WgpuBuffer* destinationPixelsBuffer,
        int destinationWidth,
        int destinationHeight,
        nuint destinationPixelsByteSize,
        out string? error)
    {
        if (!flushContext.DeviceState.TryGetOrCreateCompositeComputePipeline(
                "composite-destination-init",
                CompositeDestinationInitShader.Code,
                TryCreateDestinationInitBindGroupLayout,
                out BindGroupLayout* bindGroupLayout,
                out ComputePipeline* pipeline,
                out error))
        {
            return false;
        }

        BindGroupEntry* bindGroupEntries = stackalloc BindGroupEntry[2];
        bindGroupEntries[0] = new BindGroupEntry
        {
            Binding = 0,
            TextureView = sourceTextureView
        };
        bindGroupEntries[1] = new BindGroupEntry
        {
            Binding = 1,
            Buffer = destinationPixelsBuffer,
            Offset = 0,
            Size = destinationPixelsByteSize
        };

        BindGroupDescriptor bindGroupDescriptor = new()
        {
            Layout = bindGroupLayout,
            EntryCount = 2,
            Entries = bindGroupEntries
        };

        BindGroup* bindGroup = flushContext.Api.DeviceCreateBindGroup(flushContext.Device, in bindGroupDescriptor);
        if (bindGroup is null)
        {
            error = "Failed to create destination initialization bind group.";
            return false;
        }

        flushContext.TrackBindGroup(bindGroup);
        ComputePassDescriptor passDescriptor = default;
        ComputePassEncoder* passEncoder = flushContext.Api.CommandEncoderBeginComputePass(flushContext.CommandEncoder, in passDescriptor);
        if (passEncoder is null)
        {
            error = "Failed to begin destination initialization compute pass.";
            return false;
        }

        try
        {
            flushContext.Api.ComputePassEncoderSetPipeline(passEncoder, pipeline);
            flushContext.Api.ComputePassEncoderSetBindGroup(passEncoder, 0, bindGroup, 0, null);
            uint dispatchX = DivideRoundUp(destinationWidth, CompositeComputeWorkgroupSize);
            uint dispatchY = DivideRoundUp(destinationHeight, CompositeComputeWorkgroupSize);
            flushContext.Api.ComputePassEncoderDispatchWorkgroups(passEncoder, dispatchX, dispatchY, 1);
        }
        finally
        {
            flushContext.Api.ComputePassEncoderEnd(passEncoder);
            flushContext.Api.ComputePassEncoderRelease(passEncoder);
        }

        error = null;
        return true;
    }

    /// <summary>
    /// Writes composed destination storage back to the render target through a fullscreen blit.
    /// </summary>
    private static bool TryBlitDestinationPixelsToTarget(
        WebGPUFlushContext flushContext,
        WgpuBuffer* destinationPixelsBuffer,
        nuint destinationPixelsByteSize,
        in Rectangle destinationBounds,
        out string? error)
    {
        if (!flushContext.DeviceState.TryGetOrCreateCompositePipeline(
                "composite-destination-blit",
                CompositeDestinationBlitShader.Code,
                TryCreateDestinationBlitBindGroupLayout,
                flushContext.TextureFormat,
                CompositePipelineBlendMode.None,
                out BindGroupLayout* bindGroupLayout,
                out RenderPipeline* pipeline,
                out error))
        {
            return false;
        }

        BufferDescriptor paramsDescriptor = new()
        {
            Usage = BufferUsage.Uniform | BufferUsage.CopyDst,
            Size = 16
        };
        WgpuBuffer* paramsBuffer = flushContext.Api.DeviceCreateBuffer(flushContext.Device, in paramsDescriptor);
        if (paramsBuffer is null)
        {
            error = "Failed to create destination blit parameter buffer.";
            return false;
        }

        flushContext.TrackBuffer(paramsBuffer);
        CompositeDestinationBlitParameters parameters = new(
            destinationBounds.Width,
            destinationBounds.Height,
            destinationBounds.X,
            destinationBounds.Y);
        flushContext.Api.QueueWriteBuffer(
            flushContext.Queue,
            paramsBuffer,
            0,
            &parameters,
            (nuint)Unsafe.SizeOf<CompositeDestinationBlitParameters>());

        BindGroupEntry* bindGroupEntries = stackalloc BindGroupEntry[2];
        bindGroupEntries[0] = new BindGroupEntry
        {
            Binding = 0,
            Buffer = destinationPixelsBuffer,
            Offset = 0,
            Size = destinationPixelsByteSize
        };
        bindGroupEntries[1] = new BindGroupEntry
        {
            Binding = 1,
            Buffer = paramsBuffer,
            Offset = 0,
            Size = (nuint)Unsafe.SizeOf<CompositeDestinationBlitParameters>()
        };

        BindGroupDescriptor bindGroupDescriptor = new()
        {
            Layout = bindGroupLayout,
            EntryCount = 2,
            Entries = bindGroupEntries
        };

        BindGroup* bindGroup = flushContext.Api.DeviceCreateBindGroup(flushContext.Device, in bindGroupDescriptor);
        if (bindGroup is null)
        {
            error = "Failed to create destination blit bind group.";
            return false;
        }

        flushContext.TrackBindGroup(bindGroup);
        if (!flushContext.BeginRenderPass(flushContext.TargetView))
        {
            error = "Failed to begin destination blit render pass.";
            return false;
        }

        flushContext.Api.RenderPassEncoderSetPipeline(flushContext.PassEncoder, pipeline);
        flushContext.Api.RenderPassEncoderSetBindGroup(flushContext.PassEncoder, 0, bindGroup, 0, null);
        flushContext.Api.RenderPassEncoderSetScissorRect(
            flushContext.PassEncoder,
            (uint)destinationBounds.X,
            (uint)destinationBounds.Y,
            (uint)destinationBounds.Width,
            (uint)destinationBounds.Height);
        flushContext.Api.RenderPassEncoderDraw(flushContext.PassEncoder, CompositeVertexCount, 1, 0, 0);
        flushContext.EndRenderPassIfOpen();
        error = null;
        return true;
    }

    /// <summary>
    /// Creates the bind-group layout used by destination initialization compute shader.
    /// </summary>
    private static bool TryCreateDestinationInitBindGroupLayout(
        WebGPU api,
        Device* device,
        out BindGroupLayout* layout,
        out string? error)
    {
        BindGroupLayoutEntry* entries = stackalloc BindGroupLayoutEntry[2];
        entries[0] = new BindGroupLayoutEntry
        {
            Binding = 0,
            Visibility = ShaderStage.Compute,
            Texture = new TextureBindingLayout
            {
                SampleType = TextureSampleType.Float,
                ViewDimension = TextureViewDimension.Dimension2D,
                Multisampled = false
            }
        };
        entries[1] = new BindGroupLayoutEntry
        {
            Binding = 1,
            Visibility = ShaderStage.Compute,
            Buffer = new BufferBindingLayout
            {
                Type = BufferBindingType.Storage,
                HasDynamicOffset = false,
                MinBindingSize = 0
            }
        };

        BindGroupLayoutDescriptor descriptor = new()
        {
            EntryCount = 2,
            Entries = entries
        };

        layout = api.DeviceCreateBindGroupLayout(device, in descriptor);
        if (layout is null)
        {
            error = "Failed to create destination init bind group layout.";
            return false;
        }

        error = null;
        return true;
    }

    /// <summary>
    /// Creates the bind-group layout used by destination blit render shader.
    /// </summary>
    private static bool TryCreateDestinationBlitBindGroupLayout(
        WebGPU api,
        Device* device,
        out BindGroupLayout* layout,
        out string? error)
    {
        BindGroupLayoutEntry* entries = stackalloc BindGroupLayoutEntry[2];
        entries[0] = new BindGroupLayoutEntry
        {
            Binding = 0,
            Visibility = ShaderStage.Fragment,
            Buffer = new BufferBindingLayout
            {
                Type = BufferBindingType.ReadOnlyStorage,
                HasDynamicOffset = false,
                MinBindingSize = 0
            }
        };
        entries[1] = new BindGroupLayoutEntry
        {
            Binding = 1,
            Visibility = ShaderStage.Fragment,
            Buffer = new BufferBindingLayout
            {
                Type = BufferBindingType.Uniform,
                HasDynamicOffset = false,
                MinBindingSize = (nuint)Unsafe.SizeOf<CompositeDestinationBlitParameters>()
            }
        };

        BindGroupLayoutDescriptor descriptor = new()
        {
            EntryCount = 2,
            Entries = entries
        };

        layout = api.DeviceCreateBindGroupLayout(device, in descriptor);
        if (layout is null)
        {
            error = "Failed to create destination blit bind group layout.";
            return false;
        }

        error = null;
        return true;
    }

    /// <summary>
    /// Creates one transient composition texture that can be rendered to, sampled from, and copied.
    /// </summary>
    private static bool TryCreateCompositionTexture(
        WebGPUFlushContext flushContext,
        int width,
        int height,
        out Texture* texture,
        out TextureView* textureView,
        out string? error)
    {
        texture = null;
        textureView = null;

        TextureDescriptor textureDescriptor = new()
        {
            Usage = TextureUsage.RenderAttachment | TextureUsage.TextureBinding | TextureUsage.CopySrc | TextureUsage.CopyDst,
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
    private static void CopyTextureRegion(
        WebGPUFlushContext flushContext,
        Texture* sourceTexture,
        Texture* destinationTexture,
        in Rectangle sourceRegion)
    {
        ImageCopyTexture source = new()
        {
            Texture = sourceTexture,
            MipLevel = 0,
            Origin = new Origin3D((uint)sourceRegion.X, (uint)sourceRegion.Y, 0),
            Aspect = TextureAspect.All
        };

        ImageCopyTexture destination = new()
        {
            Texture = destinationTexture,
            MipLevel = 0,
            Origin = new Origin3D(0, 0, 0),
            Aspect = TextureAspect.All
        };

        Extent3D copySize = new((uint)sourceRegion.Width, (uint)sourceRegion.Height, 1);
        flushContext.Api.CommandEncoderCopyTextureToTexture(flushContext.CommandEncoder, in source, in destination, in copySize);
    }

    /// <summary>
    /// Divides <paramref name="value"/> by <paramref name="divisor"/> and rounds up.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint DivideRoundUp(int value, int divisor)
        => (uint)((value + divisor - 1) / divisor);

    /// <summary>
    /// Finalizes one flush by submitting command buffers and optionally reading results back to CPU memory.
    /// </summary>
    private bool TryFinalizeFlush<TPixel>(
        WebGPUFlushContext flushContext,
        Buffer2DRegion<TPixel> cpuRegion)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        flushContext.EndRenderPassIfOpen();
        if (flushContext.RequiresReadback)
        {
            return this.TryReadBackToCpuRegion(flushContext, cpuRegion);
        }

        return TrySubmit(flushContext);
    }

    /// <summary>
    /// Submits the current command encoder, if any.
    /// </summary>
    private static bool TrySubmit(WebGPUFlushContext flushContext)
    {
        CommandEncoder* commandEncoder = flushContext.CommandEncoder;
        if (commandEncoder is null)
        {
            return true;
        }

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
    /// Copies target texture contents to the readback buffer and transfers bytes into destination CPU pixels.
    /// </summary>
    private bool TryReadBackToCpuRegion<TPixel>(WebGPUFlushContext flushContext, Buffer2DRegion<TPixel> destinationRegion)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        if (flushContext.TargetTexture is null ||
            flushContext.ReadbackBuffer is null ||
            flushContext.ReadbackByteCount == 0 ||
            flushContext.ReadbackBytesPerRow == 0)
        {
            return false;
        }

        if (!flushContext.EnsureCommandEncoder())
        {
            return false;
        }

        ImageCopyTexture source = new()
        {
            Texture = flushContext.TargetTexture,
            MipLevel = 0,
            Origin = new Origin3D(0, 0, 0),
            Aspect = TextureAspect.All
        };

        ImageCopyBuffer destination = new()
        {
            Buffer = flushContext.ReadbackBuffer,
            Layout = new TextureDataLayout
            {
                Offset = 0,
                BytesPerRow = flushContext.ReadbackBytesPerRow,
                RowsPerImage = (uint)destinationRegion.Height
            }
        };

        Extent3D copySize = new((uint)destinationRegion.Width, (uint)destinationRegion.Height, 1);
        flushContext.Api.CommandEncoderCopyTextureToBuffer(flushContext.CommandEncoder, in source, in destination, in copySize);

        if (!TrySubmit(flushContext))
        {
            return false;
        }

        return this.TryReadBackBufferToRegion(
            flushContext,
            flushContext.ReadbackBuffer,
            checked((int)flushContext.ReadbackBytesPerRow),
            destinationRegion);
    }

    /// <summary>
    /// Maps the readback buffer and copies pixel data into the destination region.
    /// </summary>
    private bool TryReadBackBufferToRegion<TPixel>(
        WebGPUFlushContext flushContext,
        WgpuBuffer* readbackBuffer,
        int sourceRowBytes,
        Buffer2DRegion<TPixel> destinationRegion)
        where TPixel : unmanaged
    {
        int destinationRowBytes = checked(destinationRegion.Width * Unsafe.SizeOf<TPixel>());
        int readbackByteCount = checked(sourceRowBytes * destinationRegion.Height);
        if (!this.TryMapReadBuffer(flushContext, readbackBuffer, (nuint)readbackByteCount, out byte* mappedData))
        {
            return false;
        }

        try
        {
            ReadOnlySpan<byte> sourceData = new(mappedData, readbackByteCount);
            int destinationStrideBytes = checked(destinationRegion.Buffer.Width * Unsafe.SizeOf<TPixel>());

            // Fast path for contiguous full-width rows.
            if (destinationRegion.Rectangle.X == 0 &&
                sourceRowBytes == destinationStrideBytes &&
                TryGetSingleMemory(destinationRegion.Buffer, out Memory<TPixel> contiguousDestination))
            {
                Span<byte> destinationBytes = MemoryMarshal.AsBytes(contiguousDestination.Span);
                int destinationStart = checked(destinationRegion.Rectangle.Y * destinationStrideBytes);
                int copyByteCount = checked(destinationStrideBytes * destinationRegion.Height);
                sourceData[..copyByteCount].CopyTo(destinationBytes.Slice(destinationStart, copyByteCount));
                return true;
            }

            for (int y = 0; y < destinationRegion.Height; y++)
            {
                ReadOnlySpan<byte> sourceRow = sourceData.Slice(y * sourceRowBytes, destinationRowBytes);
                MemoryMarshal.Cast<byte, TPixel>(sourceRow).CopyTo(destinationRegion.DangerousGetRowSpan(y));
            }

            return true;
        }
        finally
        {
            flushContext.Api.BufferUnmap(readbackBuffer);
        }
    }

    /// <summary>
    /// Maps a readback buffer for CPU access and returns the mapped pointer.
    /// </summary>
    private bool TryMapReadBuffer(
        WebGPUFlushContext flushContext,
        WgpuBuffer* readbackBuffer,
        nuint byteCount,
        out byte* mappedData)
    {
        mappedData = null;
        BufferMapAsyncStatus mapStatus = BufferMapAsyncStatus.Unknown;
        using ManualResetEventSlim callbackReady = new(false);
        void Callback(BufferMapAsyncStatus status, void* userData)
        {
            mapStatus = status;
            callbackReady.Set();
        }

        using PfnBufferMapCallback callbackPtr = PfnBufferMapCallback.From(Callback);
        flushContext.Api.BufferMapAsync(readbackBuffer, MapMode.Read, 0, byteCount, callbackPtr, null);

        if (!WaitForSignal(flushContext, callbackReady) || mapStatus != BufferMapAsyncStatus.Success)
        {
            return false;
        }

        void* mapped = flushContext.Api.BufferGetConstMappedRange(readbackBuffer, 0, byteCount);
        if (mapped is null)
        {
            flushContext.Api.BufferUnmap(readbackBuffer);
            return false;
        }

        mappedData = (byte*)mapped;
        return true;
    }

    /// <summary>
    /// Releases all cached shared WebGPU resources and fallback staging resources.
    /// </summary>
    public void Dispose()
    {
        if (this.isDisposed)
        {
            return;
        }

        WebGPUFlushContext.ClearDeviceStateCache();
        WebGPUFlushContext.ClearFallbackStagingCache();

        this.TestingLiveCoverageCount = 0;
        this.TestingIsGPUReady = false;
        this.TestingGPUInitializationAttempted = false;
        this.isDisposed = true;
    }

    /// <summary>
    /// Throws <see cref="ObjectDisposedException"/> when this backend is disposed.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(this.isDisposed, this);

    /// <summary>
    /// Returns whether the 2D buffer is backed by a single contiguous memory segment.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsSingleMemory<T>(Buffer2D<T> buffer)
        where T : struct
        => buffer.MemoryGroup.Count == 1;

    /// <summary>
    /// Returns the single contiguous memory segment of the provided buffer when available.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryGetSingleMemory<T>(Buffer2D<T> buffer, out Memory<T> memory)
        where T : struct
    {
        if (!IsSingleMemory(buffer))
        {
            memory = default;
            return false;
        }

        memory = buffer.MemoryGroup[0];
        return true;
    }

    /// <summary>
    /// Waits for a GPU callback signal, polling the device when the WGPU extension is available.
    /// </summary>
    private static bool WaitForSignal(WebGPUFlushContext flushContext, ManualResetEventSlim signal)
    {
        Wgpu? extension = flushContext.RuntimeLease.WgpuExtension;
        if (extension is null)
        {
            return signal.Wait(CallbackTimeoutMilliseconds);
        }

        long start = Stopwatch.GetTimestamp();
        while (!signal.IsSet && Stopwatch.GetElapsedTime(start).TotalMilliseconds < CallbackTimeoutMilliseconds)
        {
            _ = extension.DevicePoll(flushContext.Device, true, (WrappedSubmissionIndex*)null);
            if (!signal.IsSet)
            {
                _ = Thread.Yield();
            }
        }

        return signal.IsSet;
    }

    /// <summary>
    /// Destination blit parameters consumed by <see cref="CompositeDestinationBlitShader"/>.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private readonly struct CompositeDestinationBlitParameters(
        int batchWidth,
        int batchHeight,
        int targetOriginX,
        int targetOriginY)
    {
        public readonly int BatchWidth = batchWidth;

        public readonly int BatchHeight = batchHeight;

        public readonly int TargetOriginX = targetOriginX;

        public readonly int TargetOriginY = targetOriginY;
    }
}
