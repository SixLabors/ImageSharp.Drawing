// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Buffers;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Silk.NET.WebGPU;
using Silk.NET.WebGPU.Extensions.WGPU;
using SixLabors.ImageSharp;
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
/// This backend executes scene composition on WebGPU where possible and falls back to
/// <see cref="DefaultDrawingBackend"/> when GPU execution is unavailable for a specific command set.
/// </para>
/// <para>
/// High-level flush pipeline:
/// </para>
/// <code>
/// CompositionScene
///   -> Encoded scene stream (draw tags + draw-data stream)
///   -> Acquire flush context
///   -> Execute one tiled scene pass (binning -> coarse -> fine)
///   -> Blit once and optionally read back to CPU region
///   -> On failure: delegate scene to DefaultDrawingBackend
/// </code>
/// <para>
/// See src/ImageSharp.Drawing.WebGPU/WEBGPU_BACKEND_PROCESS.md for a full process walkthrough.
/// </para>
/// </remarks>
internal sealed unsafe partial class WebGPUDrawingBackend : IDrawingBackend, IDisposable
{
    private const uint CompositeVertexCount = 6;
    private const int CompositeComputeWorkgroupSize = 8;
    private const int CompositeTileWidth = 16;
    private const int CompositeTileHeight = 16;
    private const int CompositeTileCommandWorkgroupSize = 64;
    private const int CompositeDestinationPixelStride = 16;
    private const uint PreparedBrushTypeSolid = 0;
    private const uint PreparedBrushTypeImage = 1;
    private const string PreparedCompositeParamsBufferKey = "prepared-composite/params";
    private const string PreparedCompositeTileCountsBufferKey = "prepared-composite/tile-counts";
    private const string PreparedCompositeTileStartsBufferKey = "prepared-composite/tile-starts";
    private const string PreparedCompositeTileWriteOffsetsBufferKey = "prepared-composite/tile-write-offsets";
    private const string PreparedCompositeTileIndicesBufferKey = "prepared-composite/tile-indices";
    private const string PreparedCompositeDispatchConfigBufferKey = "prepared-composite/dispatch-config";
    private const int CallbackTimeoutMilliseconds = 10_000;

    private readonly DefaultDrawingBackend fallbackBackend;
    private bool isDisposed;

    private static readonly Dictionary<Type, CompositePixelRegistration> CompositePixelHandlers = CreateCompositePixelHandlers();

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

        if (!TryGetCompositeTextureFormat<TPixel>(out WebGPUTextureFormatId formatId) ||
            !AreAllCompositionBrushesSupported<TPixel>(compositionScene.Commands))
        {
            int fallbackCommandCount = compositionScene.Commands.Count;
            this.TestingFallbackPrepareCoverageCallCount += fallbackCommandCount;
            this.TestingFallbackCompositeCoverageCallCount += fallbackCommandCount;
            this.FlushCompositionsFallback(
                configuration,
                target,
                compositionScene,
                target.TryGetCpuRegion(out Buffer2DRegion<TPixel> _),
                compositionBounds: null);
            return;
        }

        List<CompositionBatch> preparedBatches = CompositionScenePlanner.CreatePreparedBatches(
            compositionScene.Commands,
            target.Bounds);
        if (preparedBatches.Count == 0)
        {
            return;
        }

        Rectangle targetExtent = new(0, 0, target.Bounds.Width, target.Bounds.Height);
        int commandCount = 0;
        Rectangle? compositionBounds = null;
        for (int batchIndex = 0; batchIndex < preparedBatches.Count; batchIndex++)
        {
            CompositionBatch batch = preparedBatches[batchIndex];
            IReadOnlyList<PreparedCompositionCommand> commands = batch.Commands;
            for (int i = 0; i < commands.Count; i++)
            {
                Rectangle destination = Rectangle.Intersect(commands[i].DestinationRegion, targetExtent);
                if (destination.Width <= 0 || destination.Height <= 0)
                {
                    continue;
                }

                compositionBounds = compositionBounds.HasValue
                    ? Rectangle.Union(compositionBounds.Value, destination)
                    : destination;

                commandCount++;
            }
        }

        if (commandCount == 0)
        {
            return;
        }

        if (compositionBounds is null)
        {
            return;
        }

        this.TestingCompositeCoverageCallCount += commandCount;

        bool hasCpuRegion = target.TryGetCpuRegion(out Buffer2DRegion<TPixel> cpuRegion);
        compositionBounds = Rectangle.Intersect(
            compositionBounds.Value,
            new Rectangle(0, 0, target.Bounds.Width, target.Bounds.Height));
        if (compositionBounds.Value.Width <= 0 || compositionBounds.Value.Height <= 0)
        {
            return;
        }

        bool gpuSuccess = false;
        bool gpuReady = false;
        string? failure = null;
        TextureFormat textureFormat = WebGPUTextureFormatMapper.ToSilk(formatId);
        int pixelSizeInBytes = Unsafe.SizeOf<TPixel>();
        using WebGPUFlushContext flushContext = WebGPUFlushContext.Create(
            target,
            textureFormat,
            pixelSizeInBytes,
            configuration.MemoryAllocator,
            compositionBounds);

        try
        {
            gpuReady = true;
            this.TestingPrepareCoverageCallCount += commandCount;
            this.TestingReleaseCoverageCallCount += commandCount;

            gpuSuccess = this.TryRenderPreparedFlush<TPixel>(
                flushContext,
                preparedBatches,
                configuration,
                target.Bounds,
                compositionBounds.Value,
                out failure) &&
                this.TryFinalizeFlush(flushContext, cpuRegion, compositionBounds);
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

        if (gpuSuccess)
        {
            this.TestingGPUPrepareCoverageCallCount += commandCount;
            this.TestingGPUCompositeCoverageCallCount += commandCount;
            return;
        }

        this.TestingFallbackPrepareCoverageCallCount += commandCount;
        this.TestingFallbackCompositeCoverageCallCount += commandCount;
        this.FlushCompositionsFallback(
            configuration,
            target,
            compositionScene,
            hasCpuRegion,
            compositionBounds);
    }

    /// <summary>
    /// Checks whether all scene commands are directly composable by WebGPU.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool AreAllCompositionBrushesSupported<TPixel>(IReadOnlyList<CompositionCommand> commands)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        for (int i = 0; i < commands.Count; i++)
        {
            Brush brush = commands[i].Brush;
            if (!IsSupportedCompositionBrush(brush))
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
    /// Executes the scene on the CPU fallback backend.
    /// </summary>
    /// <typeparam name="TPixel">The destination pixel format.</typeparam>
    /// <param name="configuration">The active processing configuration.</param>
    /// <param name="target">The original destination frame.</param>
    /// <param name="compositionScene">The scene to execute.</param>
    /// <param name="hasCpuRegion">
    /// Indicates whether <paramref name="target"/> exposes CPU pixels directly. When <see langword="false"/>,
    /// a temporary staging frame is composed and uploaded to the native surface.
    /// </param>
    /// <param name="compositionBounds">The destination-local bounds touched by the scene when known.</param>
    private void FlushCompositionsFallback<TPixel>(
        Configuration configuration,
        ICanvasFrame<TPixel> target,
        CompositionScene compositionScene,
        bool hasCpuRegion,
        Rectangle? compositionBounds)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        if (hasCpuRegion)
        {
            this.fallbackBackend.FlushCompositions(configuration, target, compositionScene);
            return;
        }

        Rectangle targetBounds = target.Bounds;
        using WebGPUFlushContext.FallbackStagingLease<TPixel> stagingLease =
            WebGPUFlushContext.RentFallbackStaging<TPixel>(configuration.MemoryAllocator, in targetBounds);

        Buffer2DRegion<TPixel> stagingRegion = stagingLease.Region;
        ICanvasFrame<TPixel> stagingFrame = new CpuCanvasFrame<TPixel>(stagingRegion);
        this.fallbackBackend.FlushCompositions(configuration, stagingFrame, compositionScene);

        using WebGPUFlushContext uploadContext = WebGPUFlushContext.CreateUploadContext(target, configuration.MemoryAllocator);
        if (compositionBounds is Rectangle uploadBounds &&
            uploadBounds.Width > 0 &&
            uploadBounds.Height > 0)
        {
            Buffer2DRegion<TPixel> uploadRegion = stagingRegion.GetSubRegion(uploadBounds);
            WebGPUFlushContext.UploadTextureFromRegion(
                uploadContext.Api,
                uploadContext.Queue,
                uploadContext.TargetTexture,
                uploadRegion,
                configuration.MemoryAllocator,
                (uint)uploadBounds.X,
                (uint)uploadBounds.Y,
                0);
        }
        else
        {
            WebGPUFlushContext.UploadTextureFromRegion(
                uploadContext.Api,
                uploadContext.Queue,
                uploadContext.TargetTexture,
                stagingRegion,
                configuration.MemoryAllocator);
        }
    }

    private bool TryRenderPreparedFlush<TPixel>(
        WebGPUFlushContext flushContext,
        List<CompositionBatch> preparedBatches,
        Configuration configuration,
        Rectangle targetBounds,
        Rectangle compositionBounds,
        out string? error)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        Rectangle targetLocalBounds = Rectangle.Intersect(
            new Rectangle(0, 0, flushContext.TargetBounds.Width, flushContext.TargetBounds.Height),
            compositionBounds);
        if (targetLocalBounds.Width <= 0 || targetLocalBounds.Height <= 0)
        {
            error = null;
            return true;
        }

        if (!flushContext.EnsureCommandEncoder())
        {
            error = "Failed to create WebGPU command encoder.";
            return false;
        }

        if (flushContext.TargetTexture is null || flushContext.TargetView is null)
        {
            error = "WebGPU flush context does not expose required target resources.";
            return false;
        }

        WgpuBuffer* destinationPixelsBuffer = flushContext.CompositeDestinationPixelsBuffer;
        nuint destinationPixelsByteSize = flushContext.CompositeDestinationPixelsByteSize;
        bool hasValidDestinationBuffer =
            destinationPixelsBuffer is not null &&
            destinationPixelsByteSize != 0 &&
            flushContext.CompositeDestinationWidth == targetLocalBounds.Width &&
            flushContext.CompositeDestinationHeight == targetLocalBounds.Height;

        TextureView* sourceTextureView = flushContext.TargetView;
        int sourceOriginX = targetLocalBounds.X;
        int sourceOriginY = targetLocalBounds.Y;
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
            sourceOriginX = 0;
            sourceOriginY = 0;
        }

        if (!hasValidDestinationBuffer)
        {
            if (!TryCreateDestinationPixelsBuffer(
                    flushContext,
                    targetLocalBounds.Width,
                    targetLocalBounds.Height,
                    out destinationPixelsBuffer,
                    out destinationPixelsByteSize,
                    out error))
            {
                return false;
            }

            flushContext.CompositeDestinationPixelsBuffer = destinationPixelsBuffer;
            flushContext.CompositeDestinationPixelsByteSize = destinationPixelsByteSize;
            flushContext.CompositeDestinationWidth = targetLocalBounds.Width;
            flushContext.CompositeDestinationHeight = targetLocalBounds.Height;
        }

        if (!TryInitializeDestinationPixels(
                flushContext,
                sourceTextureView,
                destinationPixelsBuffer,
                targetLocalBounds,
                sourceOriginX,
                sourceOriginY,
                destinationPixelsByteSize,
                out error))
        {
            return false;
        }

        List<CompositionCoverageDefinition> coverageDefinitions = new();
        Dictionary<int, int> coverageDefinitionIndexByKey = new();
        List<PreparedCompositePendingCommand> pendingCommands = new();
        for (int i = 0; i < preparedBatches.Count; i++)
        {
            CompositionBatch batch = preparedBatches[i];
            if (batch.Commands.Count == 0)
            {
                continue;
            }

            IReadOnlyList<PreparedCompositionCommand> commands = batch.Commands;
            bool sawVisibleCommand = false;
            int coverageDefinitionIndex = -1;
            for (int commandIndex = 0; commandIndex < commands.Count; commandIndex++)
            {
                PreparedCompositionCommand command = commands[commandIndex];
                Rectangle clippedDestination = Rectangle.Intersect(command.DestinationRegion, targetLocalBounds);
                if (clippedDestination.Width <= 0 || clippedDestination.Height <= 0)
                {
                    continue;
                }

                if (!sawVisibleCommand)
                {
                    int definitionKey = batch.Definition.DefinitionKey;
                    if (!coverageDefinitionIndexByKey.TryGetValue(definitionKey, out coverageDefinitionIndex))
                    {
                        coverageDefinitionIndex = coverageDefinitions.Count;
                        coverageDefinitions.Add(batch.Definition);
                        coverageDefinitionIndexByKey.Add(definitionKey, coverageDefinitionIndex);
                    }

                    sawVisibleCommand = true;
                }

                pendingCommands.Add(new PreparedCompositePendingCommand(coverageDefinitionIndex, command));
            }

            if (sawVisibleCommand)
            {
                this.TestingComputePathBatchCount++;
            }
        }

        if (pendingCommands.Count == 0)
        {
            error = null;
            return true;
        }

        if (!this.TryCreateCoverageTextureFromFlattened<TPixel>(
                flushContext,
                coverageDefinitions,
                configuration,
                out TextureView* coverageView,
                out CoveragePlacement[] coveragePlacements,
                out error))
        {
            return false;
        }

        if (!this.TryDispatchPreparedCompositeCommands<TPixel>(
                flushContext,
                sourceTextureView,
                destinationPixelsBuffer,
                destinationPixelsByteSize,
                targetBounds,
                targetLocalBounds,
                pendingCommands,
                coveragePlacements,
                coverageView,
                out error))
        {
            return false;
        }

        if (!TryBlitDestinationPixelsToTarget(
                flushContext,
                destinationPixelsBuffer,
                destinationPixelsByteSize,
                targetLocalBounds,
                out error))
        {
            return false;
        }

        error = null;
        return true;
    }

    private bool TryDispatchPreparedCompositeCommands<TPixel>(
        WebGPUFlushContext flushContext,
        TextureView* defaultBrushTextureView,
        WgpuBuffer* destinationPixelsBuffer,
        nuint destinationPixelsByteSize,
        Rectangle targetBounds,
        Rectangle targetLocalBounds,
        IReadOnlyList<PreparedCompositePendingCommand> flushCommands,
        CoveragePlacement[] coveragePlacements,
        TextureView* coverageTextureView,
        out string? error)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        error = null;
        if (flushCommands.Count == 0)
        {
            return true;
        }

        if (!flushContext.DeviceState.TryGetOrCreateCompositeComputePipeline(
                "prepared-composite",
                PreparedCompositeComputeShader.Code,
                TryCreatePreparedCompositeBindGroupLayout,
                out BindGroupLayout* bindGroupLayout,
                out ComputePipeline* pipeline,
                out error))
        {
            return false;
        }

        int tileCountX = checked((int)DivideRoundUp(targetLocalBounds.Width, CompositeTileWidth));
        int tileCountY = checked((int)DivideRoundUp(targetLocalBounds.Height, CompositeTileHeight));
        int tileCount = checked(tileCountX * tileCountY);
        if (tileCount == 0)
        {
            return true;
        }

        uint parameterSize = (uint)Unsafe.SizeOf<PreparedCompositeParameters>();
        IMemoryOwner<PreparedCompositeParameters> parametersOwner =
            flushContext.MemoryAllocator.Allocate<PreparedCompositeParameters>(flushCommands.Count);
        try
        {
            Span<PreparedCompositeParameters> parameters = parametersOwner.Memory.Span[..flushCommands.Count];
            TextureView* sourceTextureView = defaultBrushTextureView;
            nint sourceTextureViewHandle = (nint)defaultBrushTextureView;
            bool hasImageTexture = false;
            uint validCommandCount = 0;

            for (int i = 0; i < flushCommands.Count; i++)
            {
                PreparedCompositePendingCommand pendingCommand = flushCommands[i];
                PreparedCompositionCommand command = pendingCommand.Command;
                if (command.DestinationRegion.Width <= 0 || command.DestinationRegion.Height <= 0)
                {
                    continue;
                }

                uint brushType;
                int brushOriginX = 0;
                int brushOriginY = 0;
                int brushRegionX = 0;
                int brushRegionY = 0;
                int brushRegionWidth = 1;
                int brushRegionHeight = 1;
                Vector4 solidColor = default;

                if (command.Brush is SolidBrush solidBrush)
                {
                    brushType = PreparedBrushTypeSolid;
                    solidColor = solidBrush.Color.ToScaledVector4();
                }
                else if (command.Brush is ImageBrush imageBrush)
                {
                    brushType = PreparedBrushTypeImage;
                    Image<TPixel> image = (Image<TPixel>)imageBrush.SourceImage;

                    if (!TryGetOrCreateImageTextureView(
                            flushContext,
                            image,
                            flushContext.TextureFormat,
                            out TextureView* brushTextureView,
                            out error))
                    {
                        return false;
                    }

                    if (!hasImageTexture)
                    {
                        sourceTextureView = brushTextureView;
                        sourceTextureViewHandle = (nint)brushTextureView;
                        hasImageTexture = true;
                    }
                    else if (sourceTextureViewHandle != (nint)brushTextureView)
                    {
                        error = "Prepared composite flush currently supports one image brush texture per dispatch.";
                        return false;
                    }

                    Rectangle sourceRegion = Rectangle.Intersect(image.Bounds, (Rectangle)imageBrush.SourceRegion);
                    brushRegionX = sourceRegion.X;
                    brushRegionY = sourceRegion.Y;
                    brushRegionWidth = sourceRegion.Width;
                    brushRegionHeight = sourceRegion.Height;
                    brushOriginX = command.BrushBounds.X + imageBrush.Offset.X - targetBounds.X - targetLocalBounds.X;
                    brushOriginY = command.BrushBounds.Y + imageBrush.Offset.Y - targetBounds.Y - targetLocalBounds.Y;
                }
                else
                {
                    error = "Unsupported brush type.";
                    return false;
                }

                CoveragePlacement coveragePlacement = coveragePlacements[pendingCommand.CoverageDefinitionIndex];

                int destinationX = command.DestinationRegion.X - targetLocalBounds.X;
                int destinationY = command.DestinationRegion.Y - targetLocalBounds.Y;
                PreparedCompositeParameters commandParameters = new(
                    destinationX,
                    destinationY,
                    command.DestinationRegion.Width,
                    command.DestinationRegion.Height,
                    command.SourceOffset.X + coveragePlacement.OriginX,
                    command.SourceOffset.Y + coveragePlacement.OriginY,
                    targetLocalBounds.Width,
                    brushType,
                    brushOriginX,
                    brushOriginY,
                    brushRegionX,
                    brushRegionY,
                    brushRegionWidth,
                    brushRegionHeight,
                    (uint)command.GraphicsOptions.ColorBlendingMode,
                    (uint)command.GraphicsOptions.AlphaCompositionMode,
                    command.GraphicsOptions.BlendPercentage,
                    solidColor);

                uint parameterIndex = validCommandCount;
                parameters[(int)parameterIndex] = commandParameters;

                validCommandCount++;
            }

            if (validCommandCount == 0)
            {
                error = null;
                return true;
            }

            int usedParameterByteCount = checked((int)(validCommandCount * parameterSize));
            if (!flushContext.DeviceState.TryGetOrCreateSharedBuffer(
                    PreparedCompositeParamsBufferKey,
                    BufferUsage.Storage | BufferUsage.CopyDst,
                    (nuint)usedParameterByteCount,
                    out WgpuBuffer* paramsBuffer,
                    out _,
                    out error))
            {
                return false;
            }

            Span<PreparedCompositeParameters> usedParameters = parameters[..(int)validCommandCount];
            fixed (PreparedCompositeParameters* usedParametersPtr = usedParameters)
            {
                flushContext.Api.QueueWriteBuffer(
                    flushContext.Queue,
                    paramsBuffer,
                    0,
                    usedParametersPtr,
                    (nuint)usedParameterByteCount);
            }

            int tileCountsByteCount = checked(tileCount * sizeof(uint));
            if (!flushContext.DeviceState.TryGetOrCreateSharedBuffer(
                    PreparedCompositeTileCountsBufferKey,
                    BufferUsage.Storage | BufferUsage.CopyDst,
                    (nuint)tileCountsByteCount,
                    out WgpuBuffer* tileCountsBuffer,
                    out _,
                    out error))
            {
                return false;
            }

            flushContext.Api.CommandEncoderClearBuffer(
                flushContext.CommandEncoder,
                tileCountsBuffer,
                0,
                (nuint)tileCountsByteCount);

            int tileStartsByteCount = checked(tileCount * sizeof(uint));
            if (!flushContext.DeviceState.TryGetOrCreateSharedBuffer(
                    PreparedCompositeTileStartsBufferKey,
                    BufferUsage.Storage | BufferUsage.CopyDst,
                    (nuint)tileStartsByteCount,
                    out WgpuBuffer* tileStartsBuffer,
                    out _,
                    out error))
            {
                return false;
            }

            if (!flushContext.DeviceState.TryGetOrCreateSharedBuffer(
                    PreparedCompositeTileWriteOffsetsBufferKey,
                    BufferUsage.Storage | BufferUsage.CopyDst,
                    (nuint)tileStartsByteCount,
                    out WgpuBuffer* tileWriteOffsetsBuffer,
                    out _,
                    out error))
            {
                return false;
            }

            nuint maxTileCommandIndices = checked((nuint)validCommandCount * (nuint)tileCount);
            if (maxTileCommandIndices == 0)
            {
                error = null;
                return true;
            }

            nuint tileCommandIndicesByteCount = checked(maxTileCommandIndices * (nuint)sizeof(uint));
            if (!flushContext.DeviceState.TryGetOrCreateSharedBuffer(
                    PreparedCompositeTileIndicesBufferKey,
                    BufferUsage.Storage | BufferUsage.CopyDst,
                    tileCommandIndicesByteCount,
                    out WgpuBuffer* tileCommandIndicesBuffer,
                    out _,
                    out error))
            {
                return false;
            }

            nuint dispatchConfigSize = (nuint)Unsafe.SizeOf<PreparedCompositeDispatchConfig>();
            if (!flushContext.DeviceState.TryGetOrCreateSharedBuffer(
                    PreparedCompositeDispatchConfigBufferKey,
                    BufferUsage.Uniform | BufferUsage.CopyDst,
                    dispatchConfigSize,
                    out WgpuBuffer* dispatchConfigBuffer,
                    out _,
                    out error))
            {
                return false;
            }

            PreparedCompositeDispatchConfig dispatchConfig = new(
                (uint)targetLocalBounds.Width,
                (uint)targetLocalBounds.Height,
                (uint)tileCountX,
                (uint)tileCountY,
                (uint)tileCount,
                validCommandCount);
            flushContext.Api.QueueWriteBuffer(
                flushContext.Queue,
                dispatchConfigBuffer,
                0,
                &dispatchConfig,
                dispatchConfigSize);

            if (!this.DispatchPreparedCompositeTileCount(
                    flushContext,
                    paramsBuffer,
                    tileCountsBuffer,
                    dispatchConfigBuffer,
                    validCommandCount,
                    out error))
            {
                return false;
            }

            if (!this.DispatchPreparedCompositeTilePrefix(
                    flushContext,
                    tileCountsBuffer,
                    tileStartsBuffer,
                    dispatchConfigBuffer,
                    out error))
            {
                return false;
            }

            flushContext.Api.CommandEncoderClearBuffer(
                flushContext.CommandEncoder,
                tileWriteOffsetsBuffer,
                0,
                (nuint)tileStartsByteCount);

            if (!this.DispatchPreparedCompositeTileScatter(
                    flushContext,
                    paramsBuffer,
                    tileStartsBuffer,
                    tileWriteOffsetsBuffer,
                    tileCommandIndicesBuffer,
                    dispatchConfigBuffer,
                    validCommandCount,
                    out error))
            {
                return false;
            }

            BindGroupEntry* bindGroupEntries = stackalloc BindGroupEntry[8];
            bindGroupEntries[0] = new BindGroupEntry
            {
                Binding = 0,
                TextureView = coverageTextureView
            };
            bindGroupEntries[1] = new BindGroupEntry
            {
                Binding = 1,
                TextureView = sourceTextureView
            };
            bindGroupEntries[2] = new BindGroupEntry
            {
                Binding = 2,
                Buffer = destinationPixelsBuffer,
                Offset = 0,
                Size = destinationPixelsByteSize
            };
            bindGroupEntries[3] = new BindGroupEntry
            {
                Binding = 3,
                Buffer = paramsBuffer,
                Offset = 0,
                Size = (nuint)usedParameterByteCount
            };
            bindGroupEntries[4] = new BindGroupEntry
            {
                Binding = 4,
                Buffer = tileStartsBuffer,
                Offset = 0,
                Size = (nuint)tileStartsByteCount
            };
            bindGroupEntries[5] = new BindGroupEntry
            {
                Binding = 5,
                Buffer = tileCountsBuffer,
                Offset = 0,
                Size = (nuint)tileCountsByteCount
            };
            bindGroupEntries[6] = new BindGroupEntry
            {
                Binding = 6,
                Buffer = tileCommandIndicesBuffer,
                Offset = 0,
                Size = tileCommandIndicesByteCount
            };
            bindGroupEntries[7] = new BindGroupEntry
            {
                Binding = 7,
                Buffer = dispatchConfigBuffer,
                Offset = 0,
                Size = dispatchConfigSize
            };

            BindGroupDescriptor bindGroupDescriptor = new()
            {
                Layout = bindGroupLayout,
                EntryCount = 8,
                Entries = bindGroupEntries
            };

            BindGroup* bindGroup = flushContext.Api.DeviceCreateBindGroup(flushContext.Device, in bindGroupDescriptor);
            if (bindGroup is null)
            {
                error = "Failed to create prepared composite bind group.";
                return false;
            }

            flushContext.TrackBindGroup(bindGroup);
            ComputePassDescriptor passDescriptor = default;
            ComputePassEncoder* passEncoder = flushContext.Api.CommandEncoderBeginComputePass(flushContext.CommandEncoder, in passDescriptor);
            if (passEncoder is null)
            {
                error = "Failed to begin prepared composite compute pass.";
                return false;
            }

            try
            {
                flushContext.Api.ComputePassEncoderSetPipeline(passEncoder, pipeline);
                flushContext.Api.ComputePassEncoderSetBindGroup(passEncoder, 0, bindGroup, 0, null);
                flushContext.Api.ComputePassEncoderDispatchWorkgroups(
                    passEncoder,
                    DivideRoundUp(targetLocalBounds.Width, CompositeComputeWorkgroupSize),
                    DivideRoundUp(targetLocalBounds.Height, CompositeComputeWorkgroupSize),
                    1);
            }
            finally
            {
                flushContext.Api.ComputePassEncoderEnd(passEncoder);
                flushContext.Api.ComputePassEncoderRelease(passEncoder);
            }
        }
        finally
        {
            parametersOwner.Dispose();
        }

        error = null;
        return true;
    }

    private bool DispatchPreparedCompositeTileCount(
        WebGPUFlushContext flushContext,
        WgpuBuffer* paramsBuffer,
        WgpuBuffer* tileCountsBuffer,
        WgpuBuffer* dispatchConfigBuffer,
        uint commandCount,
        out string? error)
        => this.DispatchComputePass(
            flushContext,
            "prepared-composite-tile-count",
            PreparedCompositeTileCountComputeShader.Code,
            TryCreatePreparedCompositeTileCountBindGroupLayout,
            (entries) =>
            {
                entries[0] = new BindGroupEntry { Binding = 0, Buffer = paramsBuffer, Offset = 0, Size = nuint.MaxValue };
                entries[1] = new BindGroupEntry { Binding = 1, Buffer = tileCountsBuffer, Offset = 0, Size = nuint.MaxValue };
                entries[2] = new BindGroupEntry { Binding = 2, Buffer = dispatchConfigBuffer, Offset = 0, Size = (nuint)Unsafe.SizeOf<PreparedCompositeDispatchConfig>() };
                return 3;
            },
            (pass) => flushContext.Api.ComputePassEncoderDispatchWorkgroups(
                pass,
                DivideRoundUp(checked((int)commandCount), CompositeTileCommandWorkgroupSize),
                1,
                1),
            out error);

    private bool DispatchPreparedCompositeTilePrefix(
        WebGPUFlushContext flushContext,
        WgpuBuffer* tileCountsBuffer,
        WgpuBuffer* tileStartsBuffer,
        WgpuBuffer* dispatchConfigBuffer,
        out string? error)
        => this.DispatchComputePass(
            flushContext,
            "prepared-composite-tile-prefix",
            PreparedCompositeTilePrefixComputeShader.Code,
            TryCreatePreparedCompositeTilePrefixBindGroupLayout,
            (entries) =>
            {
                entries[0] = new BindGroupEntry { Binding = 0, Buffer = tileCountsBuffer, Offset = 0, Size = nuint.MaxValue };
                entries[1] = new BindGroupEntry { Binding = 1, Buffer = tileStartsBuffer, Offset = 0, Size = nuint.MaxValue };
                entries[2] = new BindGroupEntry { Binding = 2, Buffer = dispatchConfigBuffer, Offset = 0, Size = (nuint)Unsafe.SizeOf<PreparedCompositeDispatchConfig>() };
                return 3;
            },
            (pass) => flushContext.Api.ComputePassEncoderDispatchWorkgroups(pass, 1, 1, 1),
            out error);

    private bool DispatchPreparedCompositeTileScatter(
        WebGPUFlushContext flushContext,
        WgpuBuffer* paramsBuffer,
        WgpuBuffer* tileStartsBuffer,
        WgpuBuffer* tileWriteOffsetsBuffer,
        WgpuBuffer* tileCommandIndicesBuffer,
        WgpuBuffer* dispatchConfigBuffer,
        uint commandCount,
        out string? error)
        => this.DispatchComputePass(
            flushContext,
            "prepared-composite-tile-scatter",
            PreparedCompositeTileScatterComputeShader.Code,
            TryCreatePreparedCompositeTileScatterBindGroupLayout,
            (entries) =>
            {
                entries[0] = new BindGroupEntry { Binding = 0, Buffer = paramsBuffer, Offset = 0, Size = nuint.MaxValue };
                entries[1] = new BindGroupEntry { Binding = 1, Buffer = tileStartsBuffer, Offset = 0, Size = nuint.MaxValue };
                entries[2] = new BindGroupEntry { Binding = 2, Buffer = tileWriteOffsetsBuffer, Offset = 0, Size = nuint.MaxValue };
                entries[3] = new BindGroupEntry { Binding = 3, Buffer = tileCommandIndicesBuffer, Offset = 0, Size = nuint.MaxValue };
                entries[4] = new BindGroupEntry { Binding = 4, Buffer = dispatchConfigBuffer, Offset = 0, Size = (nuint)Unsafe.SizeOf<PreparedCompositeDispatchConfig>() };
                return 5;
            },
            (pass) => flushContext.Api.ComputePassEncoderDispatchWorkgroups(
                pass,
                DivideRoundUp(checked((int)commandCount), CompositeTileCommandWorkgroupSize),
                1,
                1),
            out error);

    private static bool TryGetOrCreateImageTextureView<TPixel>(
        WebGPUFlushContext flushContext,
        Image<TPixel> image,
        TextureFormat textureFormat,
        out TextureView* textureView,
        out string? error)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        if (flushContext.TryGetCachedSourceTextureView(image, out textureView))
        {
            error = null;
            return true;
        }

        TextureDescriptor descriptor = new()
        {
            Usage = TextureUsage.TextureBinding | TextureUsage.CopyDst,
            Dimension = TextureDimension.Dimension2D,
            Size = new Extent3D((uint)image.Width, (uint)image.Height, 1),
            Format = textureFormat,
            MipLevelCount = 1,
            SampleCount = 1
        };

        Texture* texture = flushContext.Api.DeviceCreateTexture(flushContext.Device, in descriptor);
        if (texture is null)
        {
            textureView = null;
            error = "Failed to create image texture.";
            return false;
        }

        TextureViewDescriptor viewDescriptor = new()
        {
            Format = descriptor.Format,
            Dimension = TextureViewDimension.Dimension2D,
            BaseMipLevel = 0,
            MipLevelCount = 1,
            BaseArrayLayer = 0,
            ArrayLayerCount = 1,
            Aspect = TextureAspect.All
        };

        textureView = flushContext.Api.TextureCreateView(texture, in viewDescriptor);
        if (textureView is null)
        {
            flushContext.Api.TextureRelease(texture);
            error = "Failed to create image texture view.";
            return false;
        }

        flushContext.TrackTexture(texture);
        flushContext.TrackTextureView(textureView);
        flushContext.CacheSourceTextureView(image, textureView);

        Buffer2DRegion<TPixel> region = new(image.Frames.RootFrame.PixelBuffer, image.Bounds);
        WebGPUFlushContext.UploadTextureFromRegion(
            flushContext.Api,
            flushContext.Queue,
            texture,
            region,
            flushContext.MemoryAllocator);

        error = null;
        return true;
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
    /// Initializes destination storage from the current destination texture contents in premultiplied form.
    /// </summary>
    private static bool TryInitializeDestinationPixels(
        WebGPUFlushContext flushContext,
        TextureView* sourceTextureView,
        WgpuBuffer* destinationPixelsBuffer,
        in Rectangle destinationBounds,
        int sourceOriginX,
        int sourceOriginY,
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

        BufferDescriptor paramsDescriptor = new()
        {
            Usage = BufferUsage.Uniform | BufferUsage.CopyDst,
            Size = (nuint)Unsafe.SizeOf<CompositeDestinationInitParameters>()
        };

        WgpuBuffer* paramsBuffer = flushContext.Api.DeviceCreateBuffer(flushContext.Device, in paramsDescriptor);
        if (paramsBuffer is null)
        {
            error = "Failed to create destination initialization parameter buffer.";
            return false;
        }

        flushContext.TrackBuffer(paramsBuffer);
        CompositeDestinationInitParameters parameters = new(
            destinationBounds.Width,
            destinationBounds.Height,
            sourceOriginX,
            sourceOriginY);
        flushContext.Api.QueueWriteBuffer(
            flushContext.Queue,
            paramsBuffer,
            0,
            &parameters,
            (nuint)Unsafe.SizeOf<CompositeDestinationInitParameters>());

        BindGroupEntry* bindGroupEntries = stackalloc BindGroupEntry[3];
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
        bindGroupEntries[2] = new BindGroupEntry
        {
            Binding = 2,
            Buffer = paramsBuffer,
            Offset = 0,
            Size = (nuint)Unsafe.SizeOf<CompositeDestinationInitParameters>()
        };

        BindGroupDescriptor bindGroupDescriptor = new()
        {
            Layout = bindGroupLayout,
            EntryCount = 3,
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
            uint dispatchX = DivideRoundUp(destinationBounds.Width, CompositeComputeWorkgroupSize);
            uint dispatchY = DivideRoundUp(destinationBounds.Height, CompositeComputeWorkgroupSize);
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
    /// Writes composed premultiplied destination storage back to the render target through a fullscreen blit.
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
        if (!flushContext.BeginRenderPass(flushContext.TargetView, loadExisting: true))
        {
            error = "Failed to begin destination blit render pass.";
            return false;
        }

        flushContext.Api.RenderPassEncoderSetPipeline(flushContext.PassEncoder, pipeline);
        flushContext.Api.RenderPassEncoderSetBindGroup(flushContext.PassEncoder, 0, bindGroup, 0, null);
        flushContext.Api.RenderPassEncoderSetViewport(
            flushContext.PassEncoder,
            0,
            0,
            flushContext.TargetBounds.Width,
            flushContext.TargetBounds.Height,
            0,
            1);
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
        BindGroupLayoutEntry* entries = stackalloc BindGroupLayoutEntry[3];
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
        entries[2] = new BindGroupLayoutEntry
        {
            Binding = 2,
            Visibility = ShaderStage.Compute,
            Buffer = new BufferBindingLayout
            {
                Type = BufferBindingType.Uniform,
                HasDynamicOffset = false,
                MinBindingSize = (nuint)Unsafe.SizeOf<CompositeDestinationInitParameters>()
            }
        };

        BindGroupLayoutDescriptor descriptor = new()
        {
            EntryCount = 3,
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
    /// Creates the bind-group layout used by prepared composite compute shader.
    /// </summary>
    private static bool TryCreatePreparedCompositeBindGroupLayout(
        WebGPU api,
        Device* device,
        out BindGroupLayout* layout,
        out string? error)
    {
        BindGroupLayoutEntry* entries = stackalloc BindGroupLayoutEntry[8];
        entries[0] = new BindGroupLayoutEntry
        {
            Binding = 0,
            Visibility = ShaderStage.Compute,
            Texture = new TextureBindingLayout
            {
                SampleType = TextureSampleType.UnfilterableFloat,
                ViewDimension = TextureViewDimension.Dimension2D,
                Multisampled = false
            }
        };
        entries[1] = new BindGroupLayoutEntry
        {
            Binding = 1,
            Visibility = ShaderStage.Compute,
            Texture = new TextureBindingLayout
            {
                SampleType = TextureSampleType.Float,
                ViewDimension = TextureViewDimension.Dimension2D,
                Multisampled = false
            }
        };
        entries[2] = new BindGroupLayoutEntry
        {
            Binding = 2,
            Visibility = ShaderStage.Compute,
            Buffer = new BufferBindingLayout
            {
                Type = BufferBindingType.Storage,
                HasDynamicOffset = false,
                MinBindingSize = 0
            }
        };
        entries[3] = new BindGroupLayoutEntry
        {
            Binding = 3,
            Visibility = ShaderStage.Compute,
            Buffer = new BufferBindingLayout
            {
                Type = BufferBindingType.ReadOnlyStorage,
                HasDynamicOffset = false,
                MinBindingSize = 0
            }
        };
        entries[4] = new BindGroupLayoutEntry
        {
            Binding = 4,
            Visibility = ShaderStage.Compute,
            Buffer = new BufferBindingLayout
            {
                Type = BufferBindingType.ReadOnlyStorage,
                HasDynamicOffset = false,
                MinBindingSize = 0
            }
        };
        entries[5] = new BindGroupLayoutEntry
        {
            Binding = 5,
            Visibility = ShaderStage.Compute,
            Buffer = new BufferBindingLayout
            {
                Type = BufferBindingType.Storage,
                HasDynamicOffset = false,
                MinBindingSize = 0
            }
        };
        entries[6] = new BindGroupLayoutEntry
        {
            Binding = 6,
            Visibility = ShaderStage.Compute,
            Buffer = new BufferBindingLayout
            {
                Type = BufferBindingType.ReadOnlyStorage,
                HasDynamicOffset = false,
                MinBindingSize = 0
            }
        };
        entries[7] = new BindGroupLayoutEntry
        {
            Binding = 7,
            Visibility = ShaderStage.Compute,
            Buffer = new BufferBindingLayout
            {
                Type = BufferBindingType.Uniform,
                HasDynamicOffset = false,
                MinBindingSize = (nuint)Unsafe.SizeOf<PreparedCompositeDispatchConfig>()
            }
        };

        BindGroupLayoutDescriptor descriptor = new()
        {
            EntryCount = 8,
            Entries = entries
        };

        layout = api.DeviceCreateBindGroupLayout(device, in descriptor);
        if (layout is null)
        {
            error = "Failed to create prepared composite bind group layout.";
            return false;
        }

        error = null;
        return true;
    }

    private static bool TryCreatePreparedCompositeTileCountBindGroupLayout(
        WebGPU api,
        Device* device,
        out BindGroupLayout* layout,
        out string? error)
    {
        BindGroupLayoutEntry* entries = stackalloc BindGroupLayoutEntry[3];
        entries[0] = new BindGroupLayoutEntry
        {
            Binding = 0,
            Visibility = ShaderStage.Compute,
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
            Visibility = ShaderStage.Compute,
            Buffer = new BufferBindingLayout
            {
                Type = BufferBindingType.Storage,
                HasDynamicOffset = false,
                MinBindingSize = 0
            }
        };
        entries[2] = new BindGroupLayoutEntry
        {
            Binding = 2,
            Visibility = ShaderStage.Compute,
            Buffer = new BufferBindingLayout
            {
                Type = BufferBindingType.Uniform,
                HasDynamicOffset = false,
                MinBindingSize = (nuint)Unsafe.SizeOf<PreparedCompositeDispatchConfig>()
            }
        };

        BindGroupLayoutDescriptor descriptor = new()
        {
            EntryCount = 3,
            Entries = entries
        };

        layout = api.DeviceCreateBindGroupLayout(device, in descriptor);
        if (layout is null)
        {
            error = "Failed to create prepared composite tile-count bind group layout.";
            return false;
        }

        error = null;
        return true;
    }

    private static bool TryCreatePreparedCompositeTilePrefixBindGroupLayout(
        WebGPU api,
        Device* device,
        out BindGroupLayout* layout,
        out string? error)
    {
        BindGroupLayoutEntry* entries = stackalloc BindGroupLayoutEntry[3];
        entries[0] = new BindGroupLayoutEntry
        {
            Binding = 0,
            Visibility = ShaderStage.Compute,
            Buffer = new BufferBindingLayout
            {
                Type = BufferBindingType.Storage,
                HasDynamicOffset = false,
                MinBindingSize = 0
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
        entries[2] = new BindGroupLayoutEntry
        {
            Binding = 2,
            Visibility = ShaderStage.Compute,
            Buffer = new BufferBindingLayout
            {
                Type = BufferBindingType.Uniform,
                HasDynamicOffset = false,
                MinBindingSize = (nuint)Unsafe.SizeOf<PreparedCompositeDispatchConfig>()
            }
        };

        BindGroupLayoutDescriptor descriptor = new()
        {
            EntryCount = 3,
            Entries = entries
        };

        layout = api.DeviceCreateBindGroupLayout(device, in descriptor);
        if (layout is null)
        {
            error = "Failed to create prepared composite tile-prefix bind group layout.";
            return false;
        }

        error = null;
        return true;
    }

    private static bool TryCreatePreparedCompositeTileScatterBindGroupLayout(
        WebGPU api,
        Device* device,
        out BindGroupLayout* layout,
        out string? error)
    {
        BindGroupLayoutEntry* entries = stackalloc BindGroupLayoutEntry[5];
        entries[0] = new BindGroupLayoutEntry
        {
            Binding = 0,
            Visibility = ShaderStage.Compute,
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
            Visibility = ShaderStage.Compute,
            Buffer = new BufferBindingLayout
            {
                Type = BufferBindingType.ReadOnlyStorage,
                HasDynamicOffset = false,
                MinBindingSize = 0
            }
        };
        entries[2] = new BindGroupLayoutEntry
        {
            Binding = 2,
            Visibility = ShaderStage.Compute,
            Buffer = new BufferBindingLayout
            {
                Type = BufferBindingType.Storage,
                HasDynamicOffset = false,
                MinBindingSize = 0
            }
        };
        entries[3] = new BindGroupLayoutEntry
        {
            Binding = 3,
            Visibility = ShaderStage.Compute,
            Buffer = new BufferBindingLayout
            {
                Type = BufferBindingType.Storage,
                HasDynamicOffset = false,
                MinBindingSize = 0
            }
        };
        entries[4] = new BindGroupLayoutEntry
        {
            Binding = 4,
            Visibility = ShaderStage.Compute,
            Buffer = new BufferBindingLayout
            {
                Type = BufferBindingType.Uniform,
                HasDynamicOffset = false,
                MinBindingSize = (nuint)Unsafe.SizeOf<PreparedCompositeDispatchConfig>()
            }
        };

        BindGroupLayoutDescriptor descriptor = new()
        {
            EntryCount = 5,
            Entries = entries
        };

        layout = api.DeviceCreateBindGroupLayout(device, in descriptor);
        if (layout is null)
        {
            error = "Failed to create prepared composite tile-scatter bind group layout.";
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint AlignTo256(uint value) => (value + 255U) & ~255U;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint FloatToUInt32Bits(float value)
        => unchecked((uint)System.BitConverter.SingleToInt32Bits(value));

    /// <summary>
    /// Finalizes one flush by submitting command buffers and optionally reading results back to CPU memory.
    /// </summary>
    private bool TryFinalizeFlush<TPixel>(
        WebGPUFlushContext flushContext,
        Buffer2DRegion<TPixel> cpuRegion,
        Rectangle? readbackBounds)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        flushContext.EndRenderPassIfOpen();
        if (flushContext.RequiresReadback)
        {
            return this.TryReadBackToCpuRegion(flushContext, cpuRegion, readbackBounds);
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
    private bool TryReadBackToCpuRegion<TPixel>(
        WebGPUFlushContext flushContext,
        Buffer2DRegion<TPixel> destinationRegion,
        Rectangle? readbackBounds)
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

        Rectangle copyBounds = readbackBounds ?? new Rectangle(0, 0, destinationRegion.Width, destinationRegion.Height);
        if (copyBounds.Width <= 0 || copyBounds.Height <= 0)
        {
            return true;
        }

        uint copyBytesPerRow = checked((uint)copyBounds.Width * (uint)Unsafe.SizeOf<TPixel>());
        copyBytesPerRow = (copyBytesPerRow + 255U) & ~255U;

        ImageCopyTexture source = new()
        {
            Texture = flushContext.TargetTexture,
            MipLevel = 0,
            Origin = new Origin3D((uint)copyBounds.X, (uint)copyBounds.Y, 0),
            Aspect = TextureAspect.All
        };

        ImageCopyBuffer destination = new()
        {
            Buffer = flushContext.ReadbackBuffer,
            Layout = new TextureDataLayout
            {
                Offset = 0,
                BytesPerRow = copyBytesPerRow,
                RowsPerImage = (uint)copyBounds.Height
            }
        };

        Extent3D copySize = new((uint)copyBounds.Width, (uint)copyBounds.Height, 1);
        flushContext.Api.CommandEncoderCopyTextureToBuffer(flushContext.CommandEncoder, in source, in destination, in copySize);

        if (!TrySubmit(flushContext))
        {
            return false;
        }

        return this.TryReadBackBufferToRegion(
            flushContext,
            flushContext.ReadbackBuffer,
            checked((int)copyBytesPerRow),
            destinationRegion,
            copyBounds);
    }

    /// <summary>
    /// Maps the readback buffer and copies pixel data into the destination region.
    /// </summary>
    private bool TryReadBackBufferToRegion<TPixel>(
        WebGPUFlushContext flushContext,
        WgpuBuffer* readbackBuffer,
        int sourceRowBytes,
        Buffer2DRegion<TPixel> destinationRegion,
        in Rectangle copyBounds)
        where TPixel : unmanaged
    {
        int destinationRowBytes = checked(copyBounds.Width * Unsafe.SizeOf<TPixel>());
        int readbackByteCount = checked(sourceRowBytes * copyBounds.Height);
        if (!this.TryMapReadBuffer(flushContext, readbackBuffer, (nuint)readbackByteCount, out byte* mappedData))
        {
            return false;
        }

        try
        {
            ReadOnlySpan<byte> sourceData = new(mappedData, readbackByteCount);
            int destinationStrideBytes = checked(destinationRegion.Buffer.Width * Unsafe.SizeOf<TPixel>());

            // Fast path for contiguous full-width rows.
            if (copyBounds.X == 0 &&
                copyBounds.Width == destinationRegion.Width &&
                TryGetSingleMemory(destinationRegion.Buffer, out Memory<TPixel> contiguousDestination))
            {
                Span<byte> destinationBytes = MemoryMarshal.AsBytes(contiguousDestination.Span);
                int destinationStart = checked((destinationRegion.Rectangle.Y + copyBounds.Y) * destinationStrideBytes);
                int copyByteCount = checked(destinationStrideBytes * copyBounds.Height);
                Span<byte> destinationSlice = destinationBytes.Slice(destinationStart, copyByteCount);
                if (sourceRowBytes == destinationStrideBytes)
                {
                    sourceData[..copyByteCount].CopyTo(destinationSlice);
                    return true;
                }

                for (int y = 0; y < copyBounds.Height; y++)
                {
                    sourceData.Slice(y * sourceRowBytes, destinationStrideBytes)
                        .CopyTo(destinationSlice.Slice(y * destinationStrideBytes, destinationStrideBytes));
                }

                return true;
            }

            for (int y = 0; y < copyBounds.Height; y++)
            {
                ReadOnlySpan<byte> sourceRow = sourceData.Slice(y * sourceRowBytes, destinationRowBytes);
                MemoryMarshal.Cast<byte, TPixel>(sourceRow).CopyTo(
                    destinationRegion.DangerousGetRowSpan(copyBounds.Y + y).Slice(copyBounds.X, copyBounds.Width));
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
        if (extension is not null)
        {
            _ = extension.DevicePoll(flushContext.Device, true, (WrappedSubmissionIndex*)null);
        }

        return signal.Wait(CallbackTimeoutMilliseconds);
    }

    /// <summary>
    /// Destination initialization parameters consumed by <see cref="CompositeDestinationInitShader"/>.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private readonly struct CompositeDestinationInitParameters
    {
        public readonly int BatchWidth;
        public readonly int BatchHeight;
        public readonly int SourceOriginX;
        public readonly int SourceOriginY;

        public CompositeDestinationInitParameters(
            int batchWidth,
            int batchHeight,
            int sourceOriginX,
            int sourceOriginY)
        {
            this.BatchWidth = batchWidth;
            this.BatchHeight = batchHeight;
            this.SourceOriginX = sourceOriginX;
            this.SourceOriginY = sourceOriginY;
        }
    }

    /// <summary>
    /// Destination blit parameters consumed by <see cref="CompositeDestinationBlitShader"/>.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private readonly struct CompositeDestinationBlitParameters
    {
        public readonly int BatchWidth;
        public readonly int BatchHeight;
        public readonly int TargetOriginX;
        public readonly int TargetOriginY;

        public CompositeDestinationBlitParameters(
            int batchWidth,
            int batchHeight,
            int targetOriginX,
            int targetOriginY)
        {
            this.BatchWidth = batchWidth;
            this.BatchHeight = batchHeight;
            this.TargetOriginX = targetOriginX;
            this.TargetOriginY = targetOriginY;
        }
    }

    private readonly struct PreparedCompositePendingCommand
    {
        public PreparedCompositePendingCommand(int coverageDefinitionIndex, in PreparedCompositionCommand command)
        {
            this.CoverageDefinitionIndex = coverageDefinitionIndex;
            this.Command = command;
        }

        public int CoverageDefinitionIndex { get; }

        public PreparedCompositionCommand Command { get; }
    }

    private readonly struct CoveragePlacement
    {
        public CoveragePlacement(int originX, int originY, int width, int height)
        {
            this.OriginX = originX;
            this.OriginY = originY;
            this.Width = width;
            this.Height = height;
        }

        public int OriginX { get; }

        public int OriginY { get; }

        public int Width { get; }

        public int Height { get; }
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct PreparedCompositeDispatchConfig
    {
        public readonly uint TargetWidth;
        public readonly uint TargetHeight;
        public readonly uint TileCountX;
        public readonly uint TileCountY;
        public readonly uint TileCount;
        public readonly uint CommandCount;
        public readonly uint Pad0;
        public readonly uint Pad1;

        public PreparedCompositeDispatchConfig(
            uint targetWidth,
            uint targetHeight,
            uint tileCountX,
            uint tileCountY,
            uint tileCount,
            uint commandCount)
        {
            this.TargetWidth = targetWidth;
            this.TargetHeight = targetHeight;
            this.TileCountX = tileCountX;
            this.TileCountY = tileCountY;
            this.TileCount = tileCount;
            this.CommandCount = commandCount;
            this.Pad0 = 0;
            this.Pad1 = 0;
        }
    }

    /// <summary>
    /// Prepared composite command parameters consumed by <see cref="PreparedCompositeComputeShader"/>.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private readonly struct PreparedCompositeParameters
    {
        public readonly uint DestinationX;
        public readonly uint DestinationY;
        public readonly uint DestinationWidth;
        public readonly uint DestinationHeight;
        public readonly uint CoverageOffsetX;
        public readonly uint CoverageOffsetY;
        public readonly uint TargetWidth;
        public readonly uint BrushType;
        public readonly uint BrushOriginX;
        public readonly uint BrushOriginY;
        public readonly uint BrushRegionX;
        public readonly uint BrushRegionY;
        public readonly uint BrushRegionWidth;
        public readonly uint BrushRegionHeight;
        public readonly uint ColorBlendMode;
        public readonly uint AlphaCompositionMode;
        public readonly uint BlendPercentage;
        public readonly uint SolidR;
        public readonly uint SolidG;
        public readonly uint SolidB;
        public readonly uint SolidA;

        public PreparedCompositeParameters(
            int destinationX,
            int destinationY,
            int destinationWidth,
            int destinationHeight,
            int coverageOffsetX,
            int coverageOffsetY,
            int targetWidth,
            uint brushType,
            int brushOriginX,
            int brushOriginY,
            int brushRegionX,
            int brushRegionY,
            int brushRegionWidth,
            int brushRegionHeight,
            uint colorBlendMode,
            uint alphaCompositionMode,
            float blendPercentage,
            Vector4 solidColor)
        {
            this.DestinationX = (uint)destinationX;
            this.DestinationY = (uint)destinationY;
            this.DestinationWidth = (uint)destinationWidth;
            this.DestinationHeight = (uint)destinationHeight;
            this.CoverageOffsetX = (uint)coverageOffsetX;
            this.CoverageOffsetY = (uint)coverageOffsetY;
            this.TargetWidth = (uint)targetWidth;
            this.BrushType = brushType;
            this.BrushOriginX = (uint)brushOriginX;
            this.BrushOriginY = (uint)brushOriginY;
            this.BrushRegionX = (uint)brushRegionX;
            this.BrushRegionY = (uint)brushRegionY;
            this.BrushRegionWidth = (uint)brushRegionWidth;
            this.BrushRegionHeight = (uint)brushRegionHeight;
            this.ColorBlendMode = colorBlendMode;
            this.AlphaCompositionMode = alphaCompositionMode;
            this.BlendPercentage = FloatToUInt32Bits(blendPercentage);
            this.SolidR = FloatToUInt32Bits(solidColor.X);
            this.SolidG = FloatToUInt32Bits(solidColor.Y);
            this.SolidB = FloatToUInt32Bits(solidColor.Z);
            this.SolidA = FloatToUInt32Bits(solidColor.W);
        }
    }
}
