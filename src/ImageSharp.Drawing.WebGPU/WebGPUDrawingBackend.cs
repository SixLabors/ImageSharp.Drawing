// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Buffers;
using System.Diagnostics;
using System.Numerics;
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
internal sealed unsafe partial class WebGPUDrawingBackend : IDrawingBackend, IDisposable
{
    private const uint CompositeVertexCount = 6;
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
        Configuration configuration,
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

        if (!CompositePixelHandlers.TryGetValue(typeof(TPixel), out CompositePixelRegistration pixelHandler))
        {
            this.TestingFallbackPrepareCoverageCallCount++;
            this.TestingFallbackCompositeCoverageCallCount += commandCount;
            this.FlushCompositionsFallback(configuration, target, compositionBatch);
            return;
        }

        bool hasCpuRegion = target.TryGetCpuRegion(out Buffer2DRegion<TPixel> cpuRegion);
        bool hasNativeSurface = target.TryGetNativeSurface(out _);
        bool useCpuReadbackFlushSession = hasCpuRegion && !hasNativeSurface && compositionBatch.FlushId != 0;
        bool gpuSuccess = false;
        bool gpuReady = false;
        string? failure = null;
        bool hadExistingCpuSession = false;
        WebGPUFlushContext? flushContext = null;

        try
        {
            flushContext = useCpuReadbackFlushSession
                ? WebGPUFlushContext.GetOrCreateCpuReadbackFlushContext(
                    compositionBatch.FlushId,
                    target,
                    pixelHandler.TextureFormat,
                    pixelHandler.PixelSizeInBytes,
                    out hadExistingCpuSession)
                : WebGPUFlushContext.Create(target, pixelHandler.TextureFormat, pixelHandler.PixelSizeInBytes);

            lock (flushContext.DeviceState.SyncRoot)
            {
                CompositionCoverageDefinition definition = compositionBatch.Definition;
                if (!flushContext.DeviceState.TryGetOrCreateCompositePipeline(flushContext.TextureFormat, out RenderPipeline* pipeline, out failure))
                {
                    gpuSuccess = false;
                }
                else if (!flushContext.DeviceState.TryGetOrCreateCoverageEntry(
                             in definition,
                             flushContext.Queue,
                             out WebGPUFlushContext.CoverageEntry? coverageEntry,
                             out failure) || coverageEntry is null)
                {
                    gpuSuccess = false;
                }
                else
                {
                    gpuReady = true;
                    gpuSuccess = this.TryCompositeBatch(flushContext, pipeline, coverageEntry, target.Bounds, compositionBatch.Commands);
                    if (gpuSuccess)
                    {
                        if (useCpuReadbackFlushSession)
                        {
                            gpuSuccess = compositionBatch.IsFinalBatchInFlush
                                ? this.TryFinalizeFlush(flushContext, hasCpuRegion, cpuRegion)
                                : TrySubmitBatch(flushContext);
                        }
                        else
                        {
                            gpuSuccess = this.TryFinalizeFlush(flushContext, hasCpuRegion, cpuRegion);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            failure = ex.Message;
            gpuSuccess = false;
        }

        this.TestingGPUInitializationAttempted = true;
        this.TestingIsGPUReady = gpuReady;
        this.TestingLastGPUInitializationFailure = gpuSuccess ? null : failure;
        this.TestingLiveCoverageCount = 0;

        if (useCpuReadbackFlushSession)
        {
            if (gpuSuccess)
            {
                this.TestingGPUPrepareCoverageCallCount++;
                this.TestingGPUCompositeCoverageCallCount += commandCount;
                if (compositionBatch.IsFinalBatchInFlush)
                {
                    WebGPUFlushContext.CompleteCpuReadbackFlushContext(compositionBatch.FlushId);
                }

                return;
            }

            WebGPUFlushContext.CompleteCpuReadbackFlushContext(compositionBatch.FlushId);
            if (hadExistingCpuSession)
            {
                throw new InvalidOperationException($"WebGPU CPURegion flush session failed after prior GPU batches. Reason: {failure ?? "Unknown error"}");
            }

            this.TestingFallbackPrepareCoverageCallCount++;
            this.TestingFallbackCompositeCoverageCallCount += commandCount;
            this.FlushCompositionsFallback(configuration, target, compositionBatch);
            return;
        }

        flushContext?.Dispose();
        if (gpuSuccess)
        {
            this.TestingGPUPrepareCoverageCallCount++;
            this.TestingGPUCompositeCoverageCallCount += commandCount;
            return;
        }

        this.TestingFallbackPrepareCoverageCallCount++;
        this.TestingFallbackCompositeCoverageCallCount += commandCount;
        this.FlushCompositionsFallback(configuration, target, compositionBatch);
    }

    private void FlushCompositionsFallback<TPixel>(
        Configuration configuration,
        ICanvasFrame<TPixel> target,
        CompositionBatch compositionBatch)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        if (target.TryGetCpuRegion(out _))
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
        lock (uploadContext.DeviceState.SyncRoot)
        {
            if (!this.QueueWriteTextureFromRegion(
                    uploadContext.Api,
                    uploadContext.Queue,
                    uploadContext.TargetTexture,
                    stagingRegion))
            {
                throw new NotSupportedException("Fallback upload to native WebGPU target failed.");
            }
        }
    }

    private bool TryCompositeBatch(
        WebGPUFlushContext flushContext,
        RenderPipeline* pipeline,
        WebGPUFlushContext.CoverageEntry coverageEntry,
        in Rectangle destinationBounds,
        IReadOnlyList<PreparedCompositionCommand> commands)
    {
        int commandCount = commands.Count;
        if (commandCount == 0)
        {
            return true;
        }

        nuint instanceBytes = checked((nuint)commandCount * (nuint)Unsafe.SizeOf<CompositeInstanceData>());
        if (!flushContext.EnsureInstanceBufferCapacity(instanceBytes, CompositeInstanceBufferSize) ||
            !flushContext.EnsureCommandEncoder() ||
            !flushContext.BeginRenderPass())
        {
            return false;
        }

        CompositeInstanceData[] rented = ArrayPool<CompositeInstanceData>.Shared.Rent(commandCount);
        try
        {
            Span<CompositeInstanceData> instances = rented.AsSpan(0, commandCount);
            int targetWidth = flushContext.TargetBounds.Width;
            int targetHeight = flushContext.TargetBounds.Height;
            for (int i = 0; i < commandCount; i++)
            {
                PreparedCompositionCommand command = commands[i];
                if (!WebGPUBrushData.TryCreate(command.Brush, command.BrushBounds, out WebGPUBrushData brushData))
                {
                    return false;
                }

                int destinationX = destinationBounds.X + command.DestinationRegion.X - flushContext.TargetBounds.X;
                int destinationY = destinationBounds.Y + command.DestinationRegion.Y - flushContext.TargetBounds.Y;

                instances[i] = new CompositeInstanceData
                {
                    SourceOffsetX = (uint)command.SourceOffset.X,
                    SourceOffsetY = (uint)command.SourceOffset.Y,
                    DestinationX = (uint)destinationX,
                    DestinationY = (uint)destinationY,
                    DestinationWidth = (uint)command.DestinationRegion.Width,
                    DestinationHeight = (uint)command.DestinationRegion.Height,
                    TargetWidth = (uint)targetWidth,
                    TargetHeight = (uint)targetHeight,
                    BrushKind = (uint)brushData.Kind,
                    SolidBrushColor = brushData.SolidColor,
                    BlendPercentage = command.GraphicsOptions.BlendPercentage
                };
            }

            fixed (CompositeInstanceData* instancesPtr = instances)
            {
                flushContext.Api.QueueWriteBuffer(flushContext.Queue, flushContext.InstanceBuffer, 0, instancesPtr, instanceBytes);
            }

            BindGroup* bindGroup = this.CreateCoverageBindGroup(flushContext, coverageEntry, instanceBytes);
            if (bindGroup is null)
            {
                return false;
            }

            flushContext.TrackBindGroup(bindGroup);
            flushContext.Api.RenderPassEncoderSetPipeline(flushContext.PassEncoder, pipeline);
            flushContext.Api.RenderPassEncoderSetBindGroup(flushContext.PassEncoder, 0, bindGroup, 0, null);
            flushContext.Api.RenderPassEncoderDraw(flushContext.PassEncoder, CompositeVertexCount, (uint)commandCount, 0, 0);

            return true;
        }
        finally
        {
            ArrayPool<CompositeInstanceData>.Shared.Return(rented);
        }
    }

    private BindGroup* CreateCoverageBindGroup(
        WebGPUFlushContext flushContext,
        WebGPUFlushContext.CoverageEntry coverageEntry,
        nuint instanceBytes)
    {
        if (flushContext.DeviceState.CompositeBindGroupLayout is null ||
            coverageEntry.GPUCoverageView is null ||
            flushContext.InstanceBuffer is null)
        {
            return null;
        }

        BindGroupEntry* bindGroupEntries = stackalloc BindGroupEntry[2];
        bindGroupEntries[0] = new BindGroupEntry
        {
            Binding = 0,
            TextureView = coverageEntry.GPUCoverageView
        };
        bindGroupEntries[1] = new BindGroupEntry
        {
            Binding = 1,
            Buffer = flushContext.InstanceBuffer,
            Offset = 0,
            Size = instanceBytes
        };

        BindGroupDescriptor bindGroupDescriptor = new()
        {
            Layout = flushContext.DeviceState.CompositeBindGroupLayout,
            EntryCount = 2,
            Entries = bindGroupEntries
        };

        return flushContext.Api.DeviceCreateBindGroup(flushContext.Device, in bindGroupDescriptor);
    }

    private bool TryFinalizeFlush<TPixel>(
        WebGPUFlushContext flushContext,
        bool hasCpuRegion,
        Buffer2DRegion<TPixel> cpuRegion)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        flushContext.EndRenderPassIfOpen();
        return flushContext.RequiresReadback
            ? hasCpuRegion && this.TryReadBackToCpuRegion(flushContext, cpuRegion)
            : TrySubmit(flushContext);
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
        return TrySubmit(flushContext);
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

    private bool QueueWriteTextureFromRegion<TPixel>(
        WebGPU api,
        Queue* queue,
        Texture* destinationTexture,
        Buffer2DRegion<TPixel> sourceRegion)
        where TPixel : unmanaged
    {
        int pixelSizeInBytes = Unsafe.SizeOf<TPixel>();
        ImageCopyTexture destination = new()
        {
            Texture = destinationTexture,
            MipLevel = 0,
            Origin = new Origin3D(0, 0, 0),
            Aspect = TextureAspect.All
        };

        Extent3D writeSize = new((uint)sourceRegion.Width, (uint)sourceRegion.Height, 1);

        if (IsSingleMemory(sourceRegion.Buffer) &&
            sourceRegion.Rectangle.X == 0 &&
            sourceRegion.Width == sourceRegion.Buffer.Width)
        {
            int sourceStrideBytes = checked(sourceRegion.Buffer.Width * pixelSizeInBytes);
            int sourceRowBytes = checked(sourceRegion.Width * pixelSizeInBytes);
            nuint sourceByteCount = checked((nuint)(((long)sourceStrideBytes * (sourceRegion.Height - 1)) + sourceRowBytes));

            TextureDataLayout layout = new()
            {
                Offset = 0,
                BytesPerRow = (uint)sourceStrideBytes,
                RowsPerImage = (uint)sourceRegion.Height
            };

            Span<TPixel> firstRow = sourceRegion.DangerousGetRowSpan(0);
            fixed (TPixel* uploadPtr = firstRow)
            {
                api.QueueWriteTexture(queue, in destination, uploadPtr, sourceByteCount, in layout, in writeSize);
            }

            return true;
        }

        int packedRowBytes = checked(sourceRegion.Width * pixelSizeInBytes);
        int packedByteCount = checked(packedRowBytes * sourceRegion.Height);
        byte[] rented = ArrayPool<byte>.Shared.Rent(packedByteCount);
        try
        {
            Span<byte> packedData = rented.AsSpan(0, packedByteCount);
            for (int y = 0; y < sourceRegion.Height; y++)
            {
                ReadOnlySpan<TPixel> sourceRow = sourceRegion.DangerousGetRowSpan(y);
                MemoryMarshal.AsBytes(sourceRow).CopyTo(packedData.Slice(y * packedRowBytes, packedRowBytes));
            }

            TextureDataLayout layout = new()
            {
                Offset = 0,
                BytesPerRow = (uint)packedRowBytes,
                RowsPerImage = (uint)sourceRegion.Height
            };

            fixed (byte* uploadPtr = packedData)
            {
                api.QueueWriteTexture(queue, in destination, uploadPtr, (nuint)packedByteCount, in layout, in writeSize);
            }

            return true;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool WaitForSignal(WebGPUFlushContext flushContext, ManualResetEventSlim signal)
    {
        Wgpu? extension = flushContext.RuntimeLease.WgpuExtension;
        if (extension is null)
        {
            return signal.Wait(CallbackTimeoutMilliseconds);
        }

        Stopwatch stopwatch = Stopwatch.StartNew();
        while (!signal.IsSet && stopwatch.ElapsedMilliseconds < CallbackTimeoutMilliseconds)
        {
            _ = extension.DevicePoll(flushContext.Device, true, (WrappedSubmissionIndex*)null);
        }

        return signal.IsSet;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct CompositeInstanceData
    {
        public uint SourceOffsetX;
        public uint SourceOffsetY;
        public uint DestinationX;
        public uint DestinationY;
        public uint DestinationWidth;
        public uint DestinationHeight;
        public uint TargetWidth;
        public uint TargetHeight;
        public uint BrushKind;
        public uint Padding0;
        public uint Padding1;
        public uint Padding2;
        public Vector4 SolidBrushColor;
        public float BlendPercentage;
        public float Padding3;
        public float Padding4;
        public float Padding5;
    }
}
