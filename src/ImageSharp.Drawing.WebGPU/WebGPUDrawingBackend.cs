// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Silk.NET.WebGPU;
using Silk.NET.WebGPU.Extensions.WGPU;
using SixLabors.ImageSharp.Drawing.Processing.Backends.Brushes;
using SixLabors.ImageSharp.Drawing.Shapes.Rasterization;
using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.PixelFormats;
using WgpuBuffer = Silk.NET.WebGPU.Buffer;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// WebGPU-backed implementation of <see cref="IDrawingBackend"/>.
/// </summary>
internal sealed unsafe partial class WebGPUDrawingBackend : IDrawingBackend, IDisposable
{
    private const uint CompositeVertexCount = 6;
    private const int CompositeComputeWorkgroupSize = 8;
    private const int CompositeDestinationPixelStride = 16;
    private const nuint CompositeInstanceBufferSize = 256 * 1024;
    private const int CallbackTimeoutMilliseconds = 10_000;

    private readonly DefaultDrawingBackend fallbackBackend;
    private bool isDisposed;

    private static readonly Dictionary<Type, CompositePixelRegistration> CompositePixelHandlers = CreateCompositePixelHandlers();

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
        return WebGPUBrushComposerFactory.IsSupportedBrush(brush);
    }

    /// <inheritdoc />
    public void FlushCompositions<TPixel>(
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
                gpuSuccess = this.TryCompositeBatch<TPixel>(
                    flushContext,
                    coverageEntry,
                    compositionBatch.Commands,
                    blitToTarget: !useFlushSession || compositionBatch.IsFinalBatchInFlush,
                    out failure);
                if (gpuSuccess)
                {
                    if (useFlushSession && !compositionBatch.IsFinalBatchInFlush)
                    {
                        // Keep the render pass open for the next batch.
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool AreAllCompositionBrushesSupported(IReadOnlyList<PreparedCompositionCommand> commands)
    {
        for (int i = 0; i < commands.Count; i++)
        {
            if (!WebGPUBrushComposerFactory.IsSupportedBrush(commands[i].Brush))
            {
                return false;
            }
        }

        return true;
    }

    private void FlushCompositionsFallback<TPixel>(
        Configuration configuration,
        ICanvasFrame<TPixel> target,
        CompositionBatch compositionBatch,
        bool hasCpuRegion)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        if (hasCpuRegion)
        {
            this.fallbackBackend.FlushCompositions(configuration, target, compositionBatch);
            return;
        }

        Rectangle targetBounds = target.Bounds;
        using WebGPUFlushContext.FallbackStagingLease<TPixel> stagingLease =
            WebGPUFlushContext.RentFallbackStaging<TPixel>(configuration.MemoryAllocator, in targetBounds);

        Buffer2DRegion<TPixel> stagingRegion = stagingLease.Region;
        ICanvasFrame<TPixel> stagingFrame = new CpuCanvasFrame<TPixel>(stagingRegion);
        this.fallbackBackend.FlushCompositions(configuration, stagingFrame, compositionBatch);

        using WebGPUFlushContext uploadContext = WebGPUFlushContext.CreateUploadContext(target);
        WebGPUFlushContext.UploadTextureFromRegion(
            uploadContext.Api,
            uploadContext.Queue,
            uploadContext.TargetTexture,
            stagingRegion);
    }

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

    private bool TryCompositeBatch<TPixel>(
        WebGPUFlushContext flushContext,
        WebGPUFlushContext.CoverageEntry coverageEntry,
        IReadOnlyList<PreparedCompositionCommand> commands,
        bool blitToTarget,
        out string? error)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        error = null;
        int commandCount = commands.Count;
        if (commandCount == 0)
        {
            return true;
        }

        Rectangle targetLocalBounds = new(0, 0, flushContext.TargetBounds.Width, flushContext.TargetBounds.Height);
        if (targetLocalBounds.Width <= 0 || targetLocalBounds.Height <= 0)
        {
            return true;
        }

        IWebGPUBrushComposer[] composers = new IWebGPUBrushComposer[commandCount];
        bool hasPreviousComposer = false;
        WebGPUBrushComposerCacheKey previousComposerKey = default;
        IWebGPUBrushComposer? previousComposer = null;
        for (int i = 0; i < composers.Length; i++)
        {
            PreparedCompositionCommand command = commands[i];
            WebGPUBrushComposerCacheKey cacheKey = WebGPUBrushComposerFactory.CreateCacheKey(command);
            IWebGPUBrushComposer? composer;
            if (hasPreviousComposer && cacheKey.Equals(previousComposerKey))
            {
                composer = previousComposer!;
            }
            else
            {
                composer = WebGPUBrushComposerFactory.Create<TPixel>(flushContext, command);
            }

            composers[i] = composer!;
            previousComposerKey = cacheKey;
            previousComposer = composer!;
            hasPreviousComposer = true;
        }

        nuint totalInstanceBytes = 0;
        for (int i = 0; i < composers.Length; i++)
        {
            nuint instanceBytes = composers[i].InstanceDataSizeInBytes;
            totalInstanceBytes = checked(totalInstanceBytes + AlignToStorageBufferOffset(instanceBytes));
        }

        nuint instanceOffset = flushContext.InstanceBufferWriteOffset;
        nuint requiredCapacity = checked(instanceOffset + totalInstanceBytes);

        // If the buffer exists but cannot fit at the current offset, flush pending
        // draws and reset so the next batch starts at offset 0.
        if (flushContext.InstanceBuffer is not null &&
            flushContext.InstanceBufferCapacity < requiredCapacity &&
            instanceOffset > 0)
        {
            if (!TrySubmitBatch(flushContext))
            {
                return false;
            }

            instanceOffset = 0;
            requiredCapacity = totalInstanceBytes;
        }

        if (!flushContext.EnsureInstanceBufferCapacity(requiredCapacity, Math.Max(requiredCapacity, CompositeInstanceBufferSize)) ||
            !flushContext.EnsureCommandEncoder())
        {
            error = "Failed to allocate WebGPU composition buffers.";
            return false;
        }

        if (flushContext.TargetTexture is null || flushContext.TargetView is null)
        {
            error = "WebGPU flush context does not expose a target texture/view.";
            return false;
        }

        WgpuBuffer* destinationPixelsBuffer = flushContext.CompositeDestinationPixelsBuffer;
        nuint destinationPixelsByteSize = flushContext.CompositeDestinationPixelsByteSize;
        if (destinationPixelsBuffer is null)
        {
            // Initialize the destination buffer once per flush from the current target texture.
            TextureView* sourceTextureView = flushContext.TargetView;
            if (!flushContext.CanSampleTargetTexture)
            {
                if (!TryCreateCompositionTexture(
                        flushContext,
                        targetLocalBounds.Width,
                        targetLocalBounds.Height,
                        out Texture* sourceTexture,
                        out sourceTextureView,
                        out error))
                {
                    return false;
                }

                CopyTextureRegion(flushContext, flushContext.TargetTexture, sourceTexture, targetLocalBounds);
            }

            if (!TryCreateDestinationPixelsBuffer(
                    flushContext,
                    targetLocalBounds.Width,
                    targetLocalBounds.Height,
                    out destinationPixelsBuffer,
                    out destinationPixelsByteSize,
                    out error) ||
                !TryInitializeDestinationPixels(
                    flushContext,
                    sourceTextureView,
                    destinationPixelsBuffer,
                    targetLocalBounds.Width,
                    targetLocalBounds.Height,
                    destinationPixelsByteSize,
                    out error))
            {
                return false;
            }

            flushContext.CompositeDestinationPixelsBuffer = destinationPixelsBuffer;
            flushContext.CompositeDestinationPixelsByteSize = destinationPixelsByteSize;
        }

        Span<byte> instanceScratch = flushContext.GetCompositionInstanceScratchBuffer(checked((int)totalInstanceBytes));
        nuint localInstanceOffset = 0;
        int destinationBufferWidth = targetLocalBounds.Width;
        int destinationBufferHeight = targetLocalBounds.Height;
        for (int i = 0; i < composers.Length; i++)
        {
            IWebGPUBrushComposer composer = composers[i];
            PreparedCompositionCommand command = commands[i];
            nuint instanceBytes = composer.InstanceDataSizeInBytes;
            int instanceBytesInt = checked((int)instanceBytes);
            int destinationX = command.DestinationRegion.X - flushContext.TargetBounds.X;
            int destinationY = command.DestinationRegion.Y - flushContext.TargetBounds.Y;
            WebGPUCompositeCommonParameters common = new(
                command.SourceOffset.X,
                command.SourceOffset.Y,
                destinationX,
                destinationY,
                command.DestinationRegion.Width,
                command.DestinationRegion.Height,
                destinationBufferWidth,
                destinationBufferHeight,
                0,
                0,
                command.GraphicsOptions.BlendPercentage,
                (int)command.GraphicsOptions.ColorBlendingMode,
                (int)command.GraphicsOptions.AlphaCompositionMode);

            Span<byte> payload = instanceScratch.Slice(checked((int)localInstanceOffset), instanceBytesInt);
            composer.WriteInstanceData(in common, payload);
            localInstanceOffset = checked(localInstanceOffset + AlignToStorageBufferOffset(instanceBytes));
        }

        fixed (byte* payloadPtr = instanceScratch)
        {
            // Upload all instance payloads in one call to minimize queue write overhead.
            flushContext.Api.QueueWriteBuffer(flushContext.Queue, flushContext.InstanceBuffer, instanceOffset, payloadPtr, totalInstanceBytes);
        }

        if (!TryRunCompositeCommandComputePass(
                flushContext,
                coverageEntry.GPUCoverageView,
                destinationPixelsBuffer,
                destinationPixelsByteSize,
                commands,
                composers,
                instanceOffset,
                out nuint finalCommandOffset,
                out error))
        {
            return false;
        }

        if (blitToTarget &&
            !TryBlitDestinationPixelsToTarget(
                flushContext,
                destinationPixelsBuffer,
                destinationPixelsByteSize,
                targetLocalBounds,
                out error))
        {
            return false;
        }

        flushContext.AdvanceInstanceBufferOffset(finalCommandOffset);
        return true;
    }

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

    private static bool TryRunCompositeCommandComputePass(
        WebGPUFlushContext flushContext,
        TextureView* coverageView,
        WgpuBuffer* destinationPixelsBuffer,
        nuint destinationPixelsByteSize,
        IReadOnlyList<PreparedCompositionCommand> commands,
        IWebGPUBrushComposer[] composers,
        nuint instanceOffset,
        out nuint finalCommandOffset,
        out string? error)
    {
        finalCommandOffset = instanceOffset;
        error = null;
        ComputePassDescriptor passDescriptor = default;
        ComputePassEncoder* passEncoder = flushContext.Api.CommandEncoderBeginComputePass(flushContext.CommandEncoder, in passDescriptor);
        if (passEncoder is null)
        {
            error = "Failed to begin WebGPU composition compute pass.";
            return false;
        }

        try
        {
            nuint commandOffset = instanceOffset;
            IWebGPUBrushComposer? previousComposer = null;
            ComputePipeline* previousComposerPipeline = null;
            ComputePipeline* currentBoundPipeline = null;
            for (int i = 0; i < composers.Length; i++)
            {
                IWebGPUBrushComposer composer = composers[i];
                PreparedCompositionCommand command = commands[i];
                nuint instanceBytes = composer.InstanceDataSizeInBytes;
                ComputePipeline* pipeline;
                if (ReferenceEquals(composer, previousComposer))
                {
                    pipeline = previousComposerPipeline;
                }
                else if (!composer.TryGetOrCreatePipeline(flushContext, out pipeline, out string? pipelineError))
                {
                    error = pipelineError ?? "Failed to create composite compute pipeline.";
                    return false;
                }

                BindGroup* bindGroup = composer.CreateBindGroup(
                    flushContext,
                    coverageView,
                    destinationPixelsBuffer,
                    destinationPixelsByteSize,
                    commandOffset,
                    instanceBytes);

                if (pipeline != currentBoundPipeline)
                {
                    flushContext.Api.ComputePassEncoderSetPipeline(passEncoder, pipeline);
                    currentBoundPipeline = pipeline;
                }

                uint dynamicOffset = checked((uint)commandOffset);
                flushContext.Api.ComputePassEncoderSetBindGroup(passEncoder, 0, bindGroup, 1, &dynamicOffset);
                uint dispatchX = DivideRoundUp(command.DestinationRegion.Width, CompositeComputeWorkgroupSize);
                uint dispatchY = DivideRoundUp(command.DestinationRegion.Height, CompositeComputeWorkgroupSize);
                if (dispatchX > 0 && dispatchY > 0)
                {
                    flushContext.Api.ComputePassEncoderDispatchWorkgroups(passEncoder, dispatchX, dispatchY, 1);
                }

                commandOffset = checked(commandOffset + AlignToStorageBufferOffset(instanceBytes));
                previousComposer = composer;
                previousComposerPipeline = pipeline;
            }

            finalCommandOffset = commandOffset;
            return true;
        }
        finally
        {
            flushContext.Api.ComputePassEncoderEnd(passEncoder);
            flushContext.Api.ComputePassEncoderRelease(passEncoder);
        }
    }

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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint DivideRoundUp(int value, int divisor)
        => (uint)((value + divisor - 1) / divisor);

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

    private static bool TrySubmitBatch(WebGPUFlushContext flushContext)
    {
        flushContext.EndRenderPassIfOpen();
        if (!TrySubmit(flushContext))
        {
            return false;
        }

        flushContext.ResetInstanceBufferOffset();
        return true;
    }

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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(this.isDisposed, this);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static nuint AlignToStorageBufferOffset(nuint value)
        => (value + 255) & ~(nuint)255;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsSingleMemory<T>(Buffer2D<T> buffer)
        where T : struct
        => buffer.MemoryGroup.Count == 1;

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
