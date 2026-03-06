// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Buffers;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Silk.NET.WebGPU;
using Silk.NET.WebGPU.Extensions.WGPU;
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
    private const int CompositeTileWidth = 16;
    private const int CompositeTileHeight = 16;
    private const int CompositeBinTileCountX = 16;
    private const int CompositeBinTileCountY = 16;
    private const int CompositeBinningWorkgroupSize = 256;
    private const int CompositeBinWidth = CompositeTileWidth * CompositeBinTileCountX;
    private const int CompositeBinHeight = CompositeTileHeight * CompositeBinTileCountY;
    private const uint PreparedBrushTypeSolid = 0;
    private const uint PreparedBrushTypeImage = 1;
    private const string PreparedCompositeParamsBufferKey = "prepared-composite/params";
    private const string PreparedCompositeCommandBboxesBufferKey = "prepared-composite/command-bboxes";
    private const string PreparedCompositeTileCountsBufferKey = "prepared-composite/tile-counts";
    private const string PreparedCompositeTileStartsBufferKey = "prepared-composite/tile-starts";
    private const string PreparedCompositeTileIndicesBufferKey = "prepared-composite/tile-indices";
    private const string PreparedCompositeBinHeaderBufferKey = "prepared-composite/bin-header";
    private const string PreparedCompositeBinDataBufferKey = "prepared-composite/bin-data";
    private const string PreparedCompositeBinningBumpBufferKey = "prepared-composite/binning-bump";
    private const string PreparedCompositeDispatchConfigBufferKey = "prepared-composite/dispatch-config";
    private const int UniformBufferOffsetAlignment = 256;
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

        TextureFormat textureFormat = WebGPUTextureFormatMapper.ToSilk(formatId);

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
                commandCount,
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
        int commandCount,
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

        // Use the target texture directly as the backdrop source.
        // This avoids an extra texture allocation and target→source copy.
        TextureView* backdropTextureView = flushContext.TargetView;
        int sourceOriginX = targetLocalBounds.X;
        int sourceOriginY = targetLocalBounds.Y;

        if (!TryCreateCompositionTexture(
                flushContext,
                targetLocalBounds.Width,
                targetLocalBounds.Height,
                out Texture* outputTexture,
                out TextureView* outputTextureView,
                out error))
        {
            return false;
        }

        int outputOriginX = 0;
        int outputOriginY = 0;

        List<CompositionCoverageDefinition> coverageDefinitions = [];
        Dictionary<CoverageDefinitionIdentity, int> coverageDefinitionIndexByKey = [];
        int[] batchCoverageIndices = new int[preparedBatches.Count];
        for (int i = 0; i < batchCoverageIndices.Length; i++)
        {
            batchCoverageIndices[i] = -1;
        }

        for (int i = 0; i < preparedBatches.Count; i++)
        {
            CompositionBatch batch = preparedBatches[i];
            IReadOnlyList<PreparedCompositionCommand> commands = batch.Commands;
            if (commands.Count == 0)
            {
                continue;
            }

            CoverageDefinitionIdentity definitionIdentity = new(batch.Definition);
            if (!coverageDefinitionIndexByKey.TryGetValue(definitionIdentity, out int coverageDefinitionIndex))
            {
                coverageDefinitionIndex = coverageDefinitions.Count;
                coverageDefinitions.Add(batch.Definition);
                coverageDefinitionIndexByKey.Add(definitionIdentity, coverageDefinitionIndex);
            }

            batchCoverageIndices[i] = coverageDefinitionIndex;
            this.TestingComputePathBatchCount++;
        }

        if (commandCount == 0)
        {
            error = null;
            return true;
        }

        if (!this.TryCreateEdgeBuffer<TPixel>(
                flushContext,
                coverageDefinitions,
                configuration,
                out WgpuBuffer* edgeBuffer,
                out nuint edgeBufferSize,
                out EdgePlacement[] edgePlacements,
                out int totalEdgeCount,
                out int totalCsrEntries,
                out int totalCsrIndices,
                out error))
        {
            return false;
        }

        if (!this.TryDispatchPreparedCompositeCommands<TPixel>(
                flushContext,
                backdropTextureView,
                outputTextureView,
                targetBounds,
                targetLocalBounds,
                sourceOriginX,
                sourceOriginY,
                outputOriginX,
                outputOriginY,
                preparedBatches,
                batchCoverageIndices,
                commandCount,
                edgePlacements,
                edgeBuffer,
                edgeBufferSize,
                totalEdgeCount,
                totalCsrEntries,
                totalCsrIndices,
                out error))
        {
            return false;
        }

        if (flushContext.RequiresReadback)
        {
            // CPU target: read back directly from the output texture at (0,0)
            // instead of copying output→target and then reading from target.
            flushContext.ReadbackSourceOverride = outputTexture;
        }
        else
        {
            // Native GPU surface: copy composited output back into the target.
            CopyTextureRegion(
                flushContext,
                outputTexture,
                0,
                0,
                flushContext.TargetTexture,
                targetLocalBounds.X,
                targetLocalBounds.Y,
                targetLocalBounds.Width,
                targetLocalBounds.Height);
        }

        error = null;
        return true;
    }

    private bool TryDispatchPreparedCompositeCommands<TPixel>(
        WebGPUFlushContext flushContext,
        TextureView* backdropTextureView,
        TextureView* outputTextureView,
        Rectangle targetBounds,
        Rectangle targetLocalBounds,
        int sourceOriginX,
        int sourceOriginY,
        int outputOriginX,
        int outputOriginY,
        List<CompositionBatch> preparedBatches,
        int[] batchCoverageIndices,
        int commandCount,
        EdgePlacement[] edgePlacements,
        WgpuBuffer* edgeBuffer,
        nuint edgeBufferSize,
        int totalEdgeCount,
        int totalCsrEntries,
        int totalCsrIndices,
        out string? error)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        error = null;
        if (commandCount == 0)
        {
            return true;
        }

        if (!PreparedCompositeFineComputeShader.TryGetCode(flushContext.TextureFormat, out byte[] shaderCode, out error))
        {
            return false;
        }

        if (!PreparedCompositeFineComputeShader.TryGetInputSampleType(flushContext.TextureFormat, out TextureSampleType inputTextureSampleType))
        {
            error = $"Prepared composite fine shader does not support texture format '{flushContext.TextureFormat}'.";
            return false;
        }

        string pipelineKey = $"prepared-composite-fine/{flushContext.TextureFormat}";
        bool LayoutFactory(WebGPU api, Device* device, out BindGroupLayout* layout, out string? layoutError)
            => TryCreatePreparedCompositeFineBindGroupLayout(
                api,
                device,
                flushContext.TextureFormat,
                inputTextureSampleType,
                out layout,
                out layoutError);

        if (!flushContext.DeviceState.TryGetOrCreateCompositeComputePipeline(
                pipelineKey,
                shaderCode,
                LayoutFactory,
                out BindGroupLayout* bindGroupLayout,
                out ComputePipeline* pipeline,
                out error))
        {
            return false;
        }

        int tileCountX = checked((int)DivideRoundUp(targetLocalBounds.Width, CompositeTileWidth));
        int tileCountY = checked((int)DivideRoundUp(targetLocalBounds.Height, CompositeTileHeight));
        int tileCount = checked(tileCountX * tileCountY);
        int widthInBins = checked((int)DivideRoundUp(tileCountX, CompositeBinTileCountX));
        int heightInBins = checked((int)DivideRoundUp(tileCountY, CompositeBinTileCountY));
        int binCount = checked(widthInBins * heightInBins);
        if (tileCount == 0)
        {
            return true;
        }

        uint parameterSize = (uint)Unsafe.SizeOf<PreparedCompositeParameters>();
        IMemoryOwner<PreparedCompositeParameters> parametersOwner =
            flushContext.MemoryAllocator.Allocate<PreparedCompositeParameters>(commandCount);
        IMemoryOwner<PreparedCompositeCommandBbox> bboxesOwner =
            flushContext.MemoryAllocator.Allocate<PreparedCompositeCommandBbox>(commandCount);
        try
        {
            int flushCommandCount = commandCount;
            Span<PreparedCompositeParameters> parameters = parametersOwner.Memory.Span[..commandCount];
            Span<PreparedCompositeCommandBbox> commandBboxes = bboxesOwner.Memory.Span[..commandCount];
            TextureView* brushTextureView = backdropTextureView;
            nint brushTextureViewHandle = (nint)backdropTextureView;
            bool hasImageTexture = false;
            uint maxTileCommandIndices = 0;
            uint binningPairCount = 0;

            int commandIndex = 0;
            for (int batchIndex = 0; batchIndex < preparedBatches.Count; batchIndex++)
            {
                int coverageDefinitionIndex = batchCoverageIndices[batchIndex];
                if (coverageDefinitionIndex < 0)
                {
                    continue;
                }

                IReadOnlyList<PreparedCompositionCommand> commands = preparedBatches[batchIndex].Commands;
                for (int i = 0; i < commands.Count; i++)
                {
                    PreparedCompositionCommand command = commands[i];

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
                                out TextureView* resolvedBrushTextureView,
                                out error))
                        {
                            return false;
                        }

                        if (!hasImageTexture)
                        {
                            brushTextureView = resolvedBrushTextureView;
                            brushTextureViewHandle = (nint)resolvedBrushTextureView;
                            hasImageTexture = true;
                        }
                        else if (brushTextureViewHandle != (nint)resolvedBrushTextureView)
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

                    EdgePlacement edgePlacement = edgePlacements[coverageDefinitionIndex];
                    Rectangle destinationRegion = command.DestinationRegion;
                    Point sourceOffset = command.SourceOffset;

                    int destinationX = destinationRegion.X - targetLocalBounds.X;
                    int destinationY = destinationRegion.Y - targetLocalBounds.Y;

                    // Edge origin: transforms target-local pixel to edge-local space.
                    // edge_local = pixel - edge_origin, where edge_origin = destination - sourceOffset.
                    int edgeOriginX = destinationX - sourceOffset.X;
                    int edgeOriginY = destinationY - sourceOffset.Y;

                    int minTileX = destinationX / CompositeTileWidth;
                    int minTileY = destinationY / CompositeTileHeight;
                    int maxTileX = (destinationX + destinationRegion.Width - 1) / CompositeTileWidth;
                    int maxTileY = (destinationY + destinationRegion.Height - 1) / CompositeTileHeight;
                    uint tileEmitCount = checked((uint)((maxTileX - minTileX + 1) * (maxTileY - minTileY + 1)));
                    maxTileCommandIndices = checked(maxTileCommandIndices + tileEmitCount);

                    int minBinX = destinationX / CompositeBinWidth;
                    int minBinY = destinationY / CompositeBinHeight;
                    int maxBinX = (destinationX + destinationRegion.Width - 1) / CompositeBinWidth;
                    int maxBinY = (destinationY + destinationRegion.Height - 1) / CompositeBinHeight;
                    uint binEmitCount = checked((uint)((maxBinX - minBinX + 1) * (maxBinY - minBinY + 1)));
                    binningPairCount = checked(binningPairCount + binEmitCount);
                    PreparedCompositeParameters commandParameters = new(
                    destinationX,
                    destinationY,
                    destinationRegion.Width,
                    destinationRegion.Height,
                    edgePlacement.EdgeStart,
                    edgePlacement.FillRule,
                    edgeOriginX,
                    edgeOriginY,
                    edgePlacement.CsrOffsetsStart,
                    edgePlacement.CsrBandCount,
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

                    parameters[commandIndex] = commandParameters;
                    commandBboxes[commandIndex] = new PreparedCompositeCommandBbox(
                        destinationX,
                        destinationY,
                        destinationX + destinationRegion.Width,
                        destinationY + destinationRegion.Height);
                    commandIndex++;
                }
            }

            int usedParameterByteCount = checked(flushCommandCount * (int)parameterSize);
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

            fixed (PreparedCompositeParameters* usedParametersPtr = parameters)
            {
                flushContext.Api.QueueWriteBuffer(
                    flushContext.Queue,
                    paramsBuffer,
                    0,
                    usedParametersPtr,
                    (nuint)usedParameterByteCount);
            }

            int usedCommandBboxByteCount = checked(flushCommandCount * Unsafe.SizeOf<PreparedCompositeCommandBbox>());
            if (!flushContext.DeviceState.TryGetOrCreateSharedBuffer(
                    PreparedCompositeCommandBboxesBufferKey,
                    BufferUsage.Storage | BufferUsage.CopyDst,
                    (nuint)usedCommandBboxByteCount,
                    out WgpuBuffer* commandBboxesBuffer,
                    out _,
                    out error))
            {
                return false;
            }

            fixed (PreparedCompositeCommandBbox* usedBboxesPtr = commandBboxes)
            {
                flushContext.Api.QueueWriteBuffer(
                    flushContext.Queue,
                    commandBboxesBuffer,
                    0,
                    usedBboxesPtr,
                    (nuint)usedCommandBboxByteCount);
            }

            int partitionCount = (int)DivideRoundUp(flushCommandCount, CompositeBinningWorkgroupSize);
            uint binningSize = Math.Max(binningPairCount, 1u);
            int binHeaderCount = checked(partitionCount * CompositeBinningWorkgroupSize);
            int binHeaderByteCount = checked(binHeaderCount * Unsafe.SizeOf<PreparedCompositeBinHeader>());
            if (!flushContext.DeviceState.TryGetOrCreateSharedBuffer(
                    PreparedCompositeBinHeaderBufferKey,
                    BufferUsage.Storage | BufferUsage.CopyDst,
                    (nuint)binHeaderByteCount,
                    out WgpuBuffer* binHeaderBuffer,
                    out _,
                    out error))
            {
                return false;
            }

            nuint binDataByteCount = checked(binningSize * (nuint)sizeof(uint));
            if (!flushContext.DeviceState.TryGetOrCreateSharedBuffer(
                    PreparedCompositeBinDataBufferKey,
                    BufferUsage.Storage | BufferUsage.CopyDst,
                    binDataByteCount,
                    out WgpuBuffer* binDataBuffer,
                    out _,
                    out error))
            {
                return false;
            }

            if (!flushContext.DeviceState.TryGetOrCreateSharedBuffer(
                    PreparedCompositeBinningBumpBufferKey,
                    BufferUsage.Storage | BufferUsage.CopyDst,
                    (nuint)Unsafe.SizeOf<PreparedCompositeBinningBump>(),
                    out WgpuBuffer* binningBumpBuffer,
                    out _,
                    out error))
            {
                return false;
            }

            flushContext.Api.CommandEncoderClearBuffer(
                flushContext.CommandEncoder,
                binningBumpBuffer,
                0,
                (nuint)Unsafe.SizeOf<PreparedCompositeBinningBump>());

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
                tileStartsBuffer,
                0,
                (nuint)tileStartsByteCount);

            flushContext.Api.CommandEncoderClearBuffer(
                flushContext.CommandEncoder,
                tileCountsBuffer,
                0,
                (nuint)tileCountsByteCount);

            uint tileCommandCapacity = maxTileCommandIndices;
            nuint usedTileCommandCount = Math.Max(tileCommandCapacity, 1u);
            nuint tileCommandIndicesByteCount = checked(usedTileCommandCount * sizeof(uint));
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
                (uint)flushCommandCount,
                (uint)sourceOriginX,
                (uint)sourceOriginY,
                (uint)outputOriginX,
                (uint)outputOriginY,
                (uint)widthInBins,
                (uint)heightInBins,
                (uint)binCount,
                (uint)partitionCount,
                binningSize,
                0u);
            flushContext.Api.QueueWriteBuffer(
                flushContext.Queue,
                dispatchConfigBuffer,
                0,
                &dispatchConfig,
                dispatchConfigSize);

            // Build CSR (Compressed Sparse Row) edge bucketing on GPU.
            int csrEntries = Math.Max(totalCsrEntries, 1);
            int csrIndices = Math.Max(totalCsrIndices, 1);
            nuint csrBandCountsByteCount = checked((nuint)(csrEntries * sizeof(uint)));
            nuint csrOffsetsByteCount = csrBandCountsByteCount;
            nuint csrWriteCursorsByteCount = csrBandCountsByteCount;
            nuint csrIndicesByteCount = checked((nuint)(csrIndices * sizeof(uint)));

            if (!TryGetOrCreateCoverageBuffer(flushContext, "csr-band-counts", BufferUsage.Storage | BufferUsage.CopyDst, csrBandCountsByteCount, out WgpuBuffer* csrBandCountsBuffer, out error) ||
                !TryGetOrCreateCoverageBuffer(flushContext, "csr-offsets", BufferUsage.Storage | BufferUsage.CopyDst, csrOffsetsByteCount, out WgpuBuffer* csrOffsetsBuffer, out error) ||
                !TryGetOrCreateCoverageBuffer(flushContext, "csr-write-cursors", BufferUsage.Storage | BufferUsage.CopyDst, csrWriteCursorsByteCount, out WgpuBuffer* csrWriteCursorsBuffer, out error) ||
                !TryGetOrCreateCoverageBuffer(flushContext, "csr-indices", BufferUsage.Storage | BufferUsage.CopyDst, csrIndicesByteCount, out WgpuBuffer* csrIndicesBuffer, out error))
            {
                return false;
            }

            if (totalEdgeCount > 0 && totalCsrEntries > 0)
            {
                // CSR config uniform.
                nuint csrConfigSize = (nuint)Unsafe.SizeOf<CsrConfig>();
                if (!flushContext.DeviceState.TryGetOrCreateSharedBuffer(
                        "csr-config",
                        BufferUsage.Uniform | BufferUsage.CopyDst,
                        csrConfigSize,
                        out WgpuBuffer* csrConfigBuffer,
                        out _,
                        out error))
                {
                    return false;
                }

                CsrConfig csrConfig = new((uint)totalEdgeCount);
                flushContext.Api.QueueWriteBuffer(flushContext.Queue, csrConfigBuffer, 0, &csrConfig, csrConfigSize);

                // Clear band counts before the count dispatch.
                flushContext.Api.CommandEncoderClearBuffer(flushContext.CommandEncoder, csrBandCountsBuffer, 0, csrBandCountsByteCount);

                // Dispatch CSR count: each thread processes one edge, atomicAdd to band_counts.
                uint csrDispatchCount = DivideRoundUp(totalEdgeCount, CsrWorkgroupSize);
                if (!this.DispatchComputePass(
                        flushContext,
                        "csr-count",
                        CsrCountComputeShader.Code,
                        TryCreateCsrCountBindGroupLayout,
                        (entries) =>
                        {
                            entries[0] = new BindGroupEntry { Binding = 0, Buffer = edgeBuffer, Offset = 0, Size = edgeBufferSize };
                            entries[1] = new BindGroupEntry { Binding = 1, Buffer = csrBandCountsBuffer, Offset = 0, Size = csrBandCountsByteCount };
                            entries[2] = new BindGroupEntry { Binding = 2, Buffer = csrConfigBuffer, Offset = 0, Size = csrConfigSize };
                            return 3;
                        },
                        (pass) => flushContext.Api.ComputePassEncoderDispatchWorkgroups(pass, csrDispatchCount, 1, 1),
                        out error))
                {
                    return false;
                }

                // CSR prefix sum: exclusive prefix sum over band_counts → csr_offsets.
                // Reuse the existing tile prefix infrastructure by providing a dispatch config
                // with tile_count = totalCsrEntries.
                nuint csrPrefixConfigSize = (nuint)Unsafe.SizeOf<PreparedCompositeDispatchConfig>();
                if (!flushContext.DeviceState.TryGetOrCreateSharedBuffer(
                        "csr-prefix-dispatch-config",
                        BufferUsage.Uniform | BufferUsage.CopyDst,
                        csrPrefixConfigSize,
                        out WgpuBuffer* csrPrefixDispatchConfigBuffer,
                        out _,
                        out error))
                {
                    return false;
                }

                PreparedCompositeDispatchConfig csrPrefixConfig = new(0, 0, 0, 0, (uint)totalCsrEntries, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
                flushContext.Api.QueueWriteBuffer(flushContext.Queue, csrPrefixDispatchConfigBuffer, 0, &csrPrefixConfig, csrPrefixConfigSize);

                // Dispatch tile prefix sum using band_counts as input and csr_offsets as output.
                if (!this.DispatchPreparedCompositeTilePrefix(
                        flushContext,
                        csrBandCountsBuffer,
                        csrOffsetsBuffer,
                        csrPrefixDispatchConfigBuffer,
                        totalCsrEntries,
                        out error))
                {
                    return false;
                }

                // Clear write cursors before scatter.
                flushContext.Api.CommandEncoderClearBuffer(flushContext.CommandEncoder, csrWriteCursorsBuffer, 0, csrWriteCursorsByteCount);

                // Dispatch CSR scatter: each thread processes one edge, scatters into csr_indices.
                if (!this.DispatchComputePass(
                        flushContext,
                        "csr-scatter",
                        CsrScatterComputeShader.Code,
                        TryCreateCsrScatterBindGroupLayout,
                        (entries) =>
                        {
                            entries[0] = new BindGroupEntry { Binding = 0, Buffer = edgeBuffer, Offset = 0, Size = edgeBufferSize };
                            entries[1] = new BindGroupEntry { Binding = 1, Buffer = csrOffsetsBuffer, Offset = 0, Size = csrOffsetsByteCount };
                            entries[2] = new BindGroupEntry { Binding = 2, Buffer = csrWriteCursorsBuffer, Offset = 0, Size = csrWriteCursorsByteCount };
                            entries[3] = new BindGroupEntry { Binding = 3, Buffer = csrIndicesBuffer, Offset = 0, Size = csrIndicesByteCount };
                            entries[4] = new BindGroupEntry { Binding = 4, Buffer = csrConfigBuffer, Offset = 0, Size = csrConfigSize };
                            return 5;
                        },
                        (pass) => flushContext.Api.ComputePassEncoderDispatchWorkgroups(pass, csrDispatchCount, 1, 1),
                        out error))
                {
                    return false;
                }
            }
            else
            {
                // No edges: clear CSR buffers.
                flushContext.Api.CommandEncoderClearBuffer(flushContext.CommandEncoder, csrBandCountsBuffer, 0, csrBandCountsByteCount);
                flushContext.Api.CommandEncoderClearBuffer(flushContext.CommandEncoder, csrOffsetsBuffer, 0, csrOffsetsByteCount);
                flushContext.Api.CommandEncoderClearBuffer(flushContext.CommandEncoder, csrIndicesBuffer, 0, csrIndicesByteCount);
            }

            if (tileCommandCapacity > 0 && flushCommandCount > 0)
            {
                // Pass 1: Binning + tile count share a compute pass.
                {
                    ComputePassDescriptor setupPassDescriptor = default;
                    ComputePassEncoder* pass = flushContext.Api.CommandEncoderBeginComputePass(
                        flushContext.CommandEncoder, in setupPassDescriptor);
                    if (pass is null)
                    {
                        error = "Failed to begin binning/tile-count compute pass.";
                        return false;
                    }

                    try
                    {
                        if (!this.DispatchPreparedCompositeBinningInto(
                                flushContext,
                                pass,
                                commandBboxesBuffer,
                                binHeaderBuffer,
                                binDataBuffer,
                                binningBumpBuffer,
                                dispatchConfigBuffer,
                                flushCommandCount,
                                out error) ||
                            !this.DispatchPreparedCompositeTileCountInto(
                                flushContext,
                                pass,
                                commandBboxesBuffer,
                                binHeaderBuffer,
                                binDataBuffer,
                                tileCountsBuffer,
                                dispatchConfigBuffer,
                                widthInBins,
                                heightInBins,
                                out error))
                        {
                            return false;
                        }
                    }
                    finally
                    {
                        flushContext.Api.ComputePassEncoderEnd(pass);
                        flushContext.Api.ComputePassEncoderRelease(pass);
                    }
                }

                // Tile prefix sum needs a buffer clear between passes, then
                // prefix phases + tile fill share a second compute pass.
                if (!this.DispatchPreparedCompositeTilePrefixAndTileFill(
                        flushContext,
                        commandBboxesBuffer,
                        binHeaderBuffer,
                        binDataBuffer,
                        tileCountsBuffer,
                        tileStartsBuffer,
                        tileCommandIndicesBuffer,
                        dispatchConfigBuffer,
                        tileCount,
                        widthInBins,
                        heightInBins,
                        out error))
                {
                    return false;
                }
            }

            // Fine composite dispatch with CSR buffers.
            BindGroupEntry* bindGroupEntries = stackalloc BindGroupEntry[11];
            bindGroupEntries[0] = new BindGroupEntry
            {
                Binding = 0,
                Buffer = edgeBuffer,
                Offset = 0,
                Size = edgeBufferSize
            };
            bindGroupEntries[1] = new BindGroupEntry
            {
                Binding = 1,
                TextureView = backdropTextureView
            };
            bindGroupEntries[2] = new BindGroupEntry
            {
                Binding = 2,
                TextureView = brushTextureView
            };
            bindGroupEntries[3] = new BindGroupEntry
            {
                Binding = 3,
                TextureView = outputTextureView
            };
            bindGroupEntries[4] = new BindGroupEntry
            {
                Binding = 4,
                Buffer = paramsBuffer,
                Offset = 0,
                Size = (nuint)usedParameterByteCount
            };
            bindGroupEntries[5] = new BindGroupEntry
            {
                Binding = 5,
                Buffer = tileStartsBuffer,
                Offset = 0,
                Size = (nuint)tileStartsByteCount
            };
            bindGroupEntries[6] = new BindGroupEntry
            {
                Binding = 6,
                Buffer = tileCountsBuffer,
                Offset = 0,
                Size = (nuint)tileCountsByteCount
            };
            bindGroupEntries[7] = new BindGroupEntry
            {
                Binding = 7,
                Buffer = tileCommandIndicesBuffer,
                Offset = 0,
                Size = tileCommandIndicesByteCount
            };
            bindGroupEntries[8] = new BindGroupEntry
            {
                Binding = 8,
                Buffer = dispatchConfigBuffer,
                Offset = 0,
                Size = dispatchConfigSize
            };
            bindGroupEntries[9] = new BindGroupEntry
            {
                Binding = 9,
                Buffer = csrOffsetsBuffer,
                Offset = 0,
                Size = csrOffsetsByteCount
            };
            bindGroupEntries[10] = new BindGroupEntry
            {
                Binding = 10,
                Buffer = csrIndicesBuffer,
                Offset = 0,
                Size = csrIndicesByteCount
            };

            BindGroupDescriptor bindGroupDescriptor = new()
            {
                Layout = bindGroupLayout,
                EntryCount = 11,
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
                    (uint)tileCountX,
                    (uint)tileCountY,
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
            bboxesOwner.Dispose();
        }

        error = null;
        return true;
    }

    private bool DispatchPreparedCompositeTileCount(
        WebGPUFlushContext flushContext,
        WgpuBuffer* commandBboxesBuffer,
        WgpuBuffer* binHeaderBuffer,
        WgpuBuffer* binDataBuffer,
        WgpuBuffer* tileCountsBuffer,
        WgpuBuffer* dispatchConfigBuffer,
        int widthInBins,
        int heightInBins,
        out string? error)
        => this.DispatchComputePass(
            flushContext,
            "prepared-composite-tile-count",
            PreparedCompositeTileCountComputeShader.Code,
            TryCreatePreparedCompositeTileCountBindGroupLayout,
            (entries) =>
            {
                entries[0] = new BindGroupEntry { Binding = 0, Buffer = commandBboxesBuffer, Offset = 0, Size = nuint.MaxValue };
                entries[1] = new BindGroupEntry { Binding = 1, Buffer = binHeaderBuffer, Offset = 0, Size = nuint.MaxValue };
                entries[2] = new BindGroupEntry { Binding = 2, Buffer = binDataBuffer, Offset = 0, Size = nuint.MaxValue };
                entries[3] = new BindGroupEntry { Binding = 3, Buffer = tileCountsBuffer, Offset = 0, Size = nuint.MaxValue };
                entries[4] = new BindGroupEntry { Binding = 4, Buffer = dispatchConfigBuffer, Offset = 0, Size = (nuint)Unsafe.SizeOf<PreparedCompositeDispatchConfig>() };
                return 5;
            },
            (pass) =>
            {
                uint workgroupCountX = (uint)widthInBins;
                uint workgroupCountY = (uint)heightInBins;
                if (workgroupCountX > 0 && workgroupCountY > 0)
                {
                    flushContext.Api.ComputePassEncoderDispatchWorkgroups(pass, workgroupCountX, workgroupCountY, 1);
                }
            },
            out error);

    private bool DispatchPreparedCompositeBinning(
        WebGPUFlushContext flushContext,
        WgpuBuffer* commandBboxesBuffer,
        WgpuBuffer* binHeaderBuffer,
        WgpuBuffer* binDataBuffer,
        WgpuBuffer* binningBumpBuffer,
        WgpuBuffer* dispatchConfigBuffer,
        int commandCount,
        out string? error)
        => this.DispatchComputePass(
            flushContext,
            "prepared-composite-binning",
            PreparedCompositeBinningComputeShader.Code,
            TryCreatePreparedCompositeBinningBindGroupLayout,
            (entries) =>
            {
                entries[0] = new BindGroupEntry { Binding = 0, Buffer = commandBboxesBuffer, Offset = 0, Size = nuint.MaxValue };
                entries[1] = new BindGroupEntry { Binding = 1, Buffer = binHeaderBuffer, Offset = 0, Size = nuint.MaxValue };
                entries[2] = new BindGroupEntry { Binding = 2, Buffer = binDataBuffer, Offset = 0, Size = nuint.MaxValue };
                entries[3] = new BindGroupEntry { Binding = 3, Buffer = binningBumpBuffer, Offset = 0, Size = nuint.MaxValue };
                entries[4] = new BindGroupEntry { Binding = 4, Buffer = dispatchConfigBuffer, Offset = 0, Size = (nuint)Unsafe.SizeOf<PreparedCompositeDispatchConfig>() };
                return 5;
            },
            (pass) =>
            {
                uint workgroupCount = DivideRoundUp(commandCount, CompositeBinningWorkgroupSize);
                if (workgroupCount > 0)
                {
                    flushContext.Api.ComputePassEncoderDispatchWorkgroups(pass, workgroupCount, 1, 1);
                }
            },
            out error);

    private bool DispatchPreparedCompositeTilePrefix(
        WebGPUFlushContext flushContext,
        WgpuBuffer* tileCountsBuffer,
        WgpuBuffer* tileStartsBuffer,
        WgpuBuffer* dispatchConfigBuffer,
        int tileCount,
        out string? error)
    {
        const int tilesPerWorkgroup = PreparedCompositeTilePrefixLocalComputeShader.TilesPerWorkgroup;
        int blockCount = checked((int)DivideRoundUp(tileCount, tilesPerWorkgroup));

        // Allocate block_sums buffer for inter-workgroup prefix propagation.
        nuint blockSumsSize = checked((nuint)(Math.Max(blockCount, 1) * sizeof(uint)));
        if (!flushContext.DeviceState.TryGetOrCreateSharedBuffer(
                "prepared-composite-tile-prefix-block-sums",
                BufferUsage.Storage | BufferUsage.CopyDst,
                blockSumsSize,
                out WgpuBuffer* blockSumsBuffer,
                out _,
                out error))
        {
            return false;
        }

        flushContext.Api.CommandEncoderClearBuffer(
            flushContext.CommandEncoder,
            blockSumsBuffer,
            0,
            blockSumsSize);

        // Phase 1: Local prefix sum per workgroup + store block totals.
        if (!this.DispatchComputePass(
                flushContext,
                "prepared-composite-tile-prefix-local",
                PreparedCompositeTilePrefixLocalComputeShader.Code,
                TryCreatePreparedCompositeTilePrefixLocalBindGroupLayout,
                (entries) =>
                {
                    entries[0] = new BindGroupEntry { Binding = 0, Buffer = tileCountsBuffer, Offset = 0, Size = nuint.MaxValue };
                    entries[1] = new BindGroupEntry { Binding = 1, Buffer = tileStartsBuffer, Offset = 0, Size = nuint.MaxValue };
                    entries[2] = new BindGroupEntry { Binding = 2, Buffer = blockSumsBuffer, Offset = 0, Size = blockSumsSize };
                    entries[3] = new BindGroupEntry { Binding = 3, Buffer = dispatchConfigBuffer, Offset = 0, Size = (nuint)Unsafe.SizeOf<PreparedCompositeDispatchConfig>() };
                    return 4;
                },
                (pass) => flushContext.Api.ComputePassEncoderDispatchWorkgroups(pass, (uint)blockCount, 1, 1),
                out error))
        {
            return false;
        }

        if (blockCount <= 1)
        {
            // Single workgroup — no cross-block propagation needed.
            return true;
        }

        // Phase 2: Prefix sum over block_sums (single workgroup handles all blocks).
        uint blockCountValue = (uint)blockCount;
        nuint prefixConfigSize = sizeof(uint);

        // We need a small uniform buffer for the block count.
        if (!flushContext.DeviceState.TryGetOrCreateSharedBuffer(
                "prepared-composite-tile-prefix-block-config",
                BufferUsage.Uniform | BufferUsage.CopyDst,
                prefixConfigSize,
                out WgpuBuffer* prefixConfigBuffer,
                out _,
                out error))
        {
            return false;
        }

        flushContext.Api.QueueWriteBuffer(flushContext.Queue, prefixConfigBuffer, 0, &blockCountValue, prefixConfigSize);

        if (!this.DispatchComputePass(
                flushContext,
                "prepared-composite-tile-prefix-block-scan",
                PreparedCompositeTilePrefixBlockScanComputeShader.Code,
                TryCreatePreparedCompositeTilePrefixBlockScanBindGroupLayout,
                (entries) =>
                {
                    entries[0] = new BindGroupEntry { Binding = 0, Buffer = blockSumsBuffer, Offset = 0, Size = blockSumsSize };
                    entries[1] = new BindGroupEntry { Binding = 1, Buffer = prefixConfigBuffer, Offset = 0, Size = prefixConfigSize };
                    return 2;
                },
                (pass) => flushContext.Api.ComputePassEncoderDispatchWorkgroups(pass, 1, 1, 1),
                out error))
        {
            return false;
        }

        // Phase 3: Propagate block prefixes to tile_starts.
        if (!this.DispatchComputePass(
                flushContext,
                "prepared-composite-tile-prefix-propagate",
                PreparedCompositeTilePrefixPropagateComputeShader.Code,
                TryCreatePreparedCompositeTilePrefixPropagateBindGroupLayout,
                (entries) =>
                {
                    entries[0] = new BindGroupEntry { Binding = 0, Buffer = blockSumsBuffer, Offset = 0, Size = blockSumsSize };
                    entries[1] = new BindGroupEntry { Binding = 1, Buffer = tileStartsBuffer, Offset = 0, Size = nuint.MaxValue };
                    entries[2] = new BindGroupEntry { Binding = 2, Buffer = dispatchConfigBuffer, Offset = 0, Size = (nuint)Unsafe.SizeOf<PreparedCompositeDispatchConfig>() };
                    return 3;
                },
                (pass) => flushContext.Api.ComputePassEncoderDispatchWorkgroups(pass, (uint)blockCount, 1, 1),
                out error))
        {
            return false;
        }

        return true;
    }

    private bool DispatchPreparedCompositeTileFill(
        WebGPUFlushContext flushContext,
        WgpuBuffer* commandBboxesBuffer,
        WgpuBuffer* binHeaderBuffer,
        WgpuBuffer* binDataBuffer,
        WgpuBuffer* tileStartsBuffer,
        WgpuBuffer* tileCountsBuffer,
        WgpuBuffer* tileCommandIndicesBuffer,
        WgpuBuffer* dispatchConfigBuffer,
        int widthInBins,
        int heightInBins,
        out string? error)
        => this.DispatchComputePass(
            flushContext,
            "prepared-composite-tile-fill",
            PreparedCompositeTileFillComputeShader.Code,
            TryCreatePreparedCompositeTileFillBindGroupLayout,
            (entries) =>
            {
                entries[0] = new BindGroupEntry { Binding = 0, Buffer = commandBboxesBuffer, Offset = 0, Size = nuint.MaxValue };
                entries[1] = new BindGroupEntry { Binding = 1, Buffer = binHeaderBuffer, Offset = 0, Size = nuint.MaxValue };
                entries[2] = new BindGroupEntry { Binding = 2, Buffer = binDataBuffer, Offset = 0, Size = nuint.MaxValue };
                entries[3] = new BindGroupEntry { Binding = 3, Buffer = tileStartsBuffer, Offset = 0, Size = nuint.MaxValue };
                entries[4] = new BindGroupEntry { Binding = 4, Buffer = tileCountsBuffer, Offset = 0, Size = nuint.MaxValue };
                entries[5] = new BindGroupEntry { Binding = 5, Buffer = tileCommandIndicesBuffer, Offset = 0, Size = nuint.MaxValue };
                entries[6] = new BindGroupEntry { Binding = 6, Buffer = dispatchConfigBuffer, Offset = 0, Size = (nuint)Unsafe.SizeOf<PreparedCompositeDispatchConfig>() };
                return 7;
            },
            (pass) =>
            {
                uint workgroupCountX = (uint)widthInBins;
                uint workgroupCountY = (uint)heightInBins;
                if (workgroupCountX > 0 && workgroupCountY > 0)
                {
                    flushContext.Api.ComputePassEncoderDispatchWorkgroups(pass, workgroupCountX, workgroupCountY, 1);
                }
            },
            out error);

    private bool DispatchPreparedCompositeBinningInto(
        WebGPUFlushContext flushContext,
        ComputePassEncoder* passEncoder,
        WgpuBuffer* commandBboxesBuffer,
        WgpuBuffer* binHeaderBuffer,
        WgpuBuffer* binDataBuffer,
        WgpuBuffer* binningBumpBuffer,
        WgpuBuffer* dispatchConfigBuffer,
        int commandCount,
        out string? error)
        => this.DispatchIntoComputePass(
            flushContext,
            passEncoder,
            "prepared-composite-binning",
            PreparedCompositeBinningComputeShader.Code,
            TryCreatePreparedCompositeBinningBindGroupLayout,
            (entries) =>
            {
                entries[0] = new BindGroupEntry { Binding = 0, Buffer = commandBboxesBuffer, Offset = 0, Size = nuint.MaxValue };
                entries[1] = new BindGroupEntry { Binding = 1, Buffer = binHeaderBuffer, Offset = 0, Size = nuint.MaxValue };
                entries[2] = new BindGroupEntry { Binding = 2, Buffer = binDataBuffer, Offset = 0, Size = nuint.MaxValue };
                entries[3] = new BindGroupEntry { Binding = 3, Buffer = binningBumpBuffer, Offset = 0, Size = nuint.MaxValue };
                entries[4] = new BindGroupEntry { Binding = 4, Buffer = dispatchConfigBuffer, Offset = 0, Size = (nuint)Unsafe.SizeOf<PreparedCompositeDispatchConfig>() };
                return 5;
            },
            (pass) =>
            {
                uint workgroupCount = DivideRoundUp(commandCount, CompositeBinningWorkgroupSize);
                if (workgroupCount > 0)
                {
                    flushContext.Api.ComputePassEncoderDispatchWorkgroups(pass, workgroupCount, 1, 1);
                }
            },
            out error);

    private bool DispatchPreparedCompositeTileCountInto(
        WebGPUFlushContext flushContext,
        ComputePassEncoder* passEncoder,
        WgpuBuffer* commandBboxesBuffer,
        WgpuBuffer* binHeaderBuffer,
        WgpuBuffer* binDataBuffer,
        WgpuBuffer* tileCountsBuffer,
        WgpuBuffer* dispatchConfigBuffer,
        int widthInBins,
        int heightInBins,
        out string? error)
        => this.DispatchIntoComputePass(
            flushContext,
            passEncoder,
            "prepared-composite-tile-count",
            PreparedCompositeTileCountComputeShader.Code,
            TryCreatePreparedCompositeTileCountBindGroupLayout,
            (entries) =>
            {
                entries[0] = new BindGroupEntry { Binding = 0, Buffer = commandBboxesBuffer, Offset = 0, Size = nuint.MaxValue };
                entries[1] = new BindGroupEntry { Binding = 1, Buffer = binHeaderBuffer, Offset = 0, Size = nuint.MaxValue };
                entries[2] = new BindGroupEntry { Binding = 2, Buffer = binDataBuffer, Offset = 0, Size = nuint.MaxValue };
                entries[3] = new BindGroupEntry { Binding = 3, Buffer = tileCountsBuffer, Offset = 0, Size = nuint.MaxValue };
                entries[4] = new BindGroupEntry { Binding = 4, Buffer = dispatchConfigBuffer, Offset = 0, Size = (nuint)Unsafe.SizeOf<PreparedCompositeDispatchConfig>() };
                return 5;
            },
            (pass) =>
            {
                uint workgroupCountX = (uint)widthInBins;
                uint workgroupCountY = (uint)heightInBins;
                if (workgroupCountX > 0 && workgroupCountY > 0)
                {
                    flushContext.Api.ComputePassEncoderDispatchWorkgroups(pass, workgroupCountX, workgroupCountY, 1);
                }
            },
            out error);

    private bool DispatchPreparedCompositeTilePrefixAndTileFill(
        WebGPUFlushContext flushContext,
        WgpuBuffer* commandBboxesBuffer,
        WgpuBuffer* binHeaderBuffer,
        WgpuBuffer* binDataBuffer,
        WgpuBuffer* tileCountsBuffer,
        WgpuBuffer* tileStartsBuffer,
        WgpuBuffer* tileCommandIndicesBuffer,
        WgpuBuffer* dispatchConfigBuffer,
        int tileCount,
        int widthInBins,
        int heightInBins,
        out string? error)
    {
        const int tilesPerWorkgroup = PreparedCompositeTilePrefixLocalComputeShader.TilesPerWorkgroup;
        int blockCount = checked((int)DivideRoundUp(tileCount, tilesPerWorkgroup));

        // Allocate block_sums buffer for inter-workgroup prefix propagation.
        nuint blockSumsSize = checked((nuint)(Math.Max(blockCount, 1) * sizeof(uint)));
        if (!flushContext.DeviceState.TryGetOrCreateSharedBuffer(
                "prepared-composite-tile-prefix-block-sums",
                BufferUsage.Storage | BufferUsage.CopyDst,
                blockSumsSize,
                out WgpuBuffer* blockSumsBuffer,
                out _,
                out error))
        {
            return false;
        }

        // ClearBuffer must happen outside a compute pass.
        flushContext.Api.CommandEncoderClearBuffer(
            flushContext.CommandEncoder,
            blockSumsBuffer,
            0,
            blockSumsSize);

        // Prepare prefix config buffer (needed for multi-block case).
        WgpuBuffer* prefixConfigBuffer = null;
        nuint prefixConfigSize = sizeof(uint);
        if (blockCount > 1)
        {
            uint blockCountValue = (uint)blockCount;
            if (!flushContext.DeviceState.TryGetOrCreateSharedBuffer(
                    "prepared-composite-tile-prefix-block-config",
                    BufferUsage.Uniform | BufferUsage.CopyDst,
                    prefixConfigSize,
                    out prefixConfigBuffer,
                    out _,
                    out error))
            {
                return false;
            }

            flushContext.Api.QueueWriteBuffer(flushContext.Queue, prefixConfigBuffer, 0, &blockCountValue, prefixConfigSize);
        }

        // All prefix phases + tile fill share a single compute pass.
        ComputePassDescriptor prefixPassDescriptor = default;
        ComputePassEncoder* pass = flushContext.Api.CommandEncoderBeginComputePass(
            flushContext.CommandEncoder, in prefixPassDescriptor);
        if (pass is null)
        {
            error = "Failed to begin prefix/tile-fill compute pass.";
            return false;
        }

        try
        {
            // Phase 1: Local prefix sum per workgroup + store block totals.
            if (!this.DispatchIntoComputePass(
                    flushContext,
                    pass,
                    "prepared-composite-tile-prefix-local",
                    PreparedCompositeTilePrefixLocalComputeShader.Code,
                    TryCreatePreparedCompositeTilePrefixLocalBindGroupLayout,
                    (entries) =>
                    {
                        entries[0] = new BindGroupEntry { Binding = 0, Buffer = tileCountsBuffer, Offset = 0, Size = nuint.MaxValue };
                        entries[1] = new BindGroupEntry { Binding = 1, Buffer = tileStartsBuffer, Offset = 0, Size = nuint.MaxValue };
                        entries[2] = new BindGroupEntry { Binding = 2, Buffer = blockSumsBuffer, Offset = 0, Size = blockSumsSize };
                        entries[3] = new BindGroupEntry { Binding = 3, Buffer = dispatchConfigBuffer, Offset = 0, Size = (nuint)Unsafe.SizeOf<PreparedCompositeDispatchConfig>() };
                        return 4;
                    },
                    (p) => flushContext.Api.ComputePassEncoderDispatchWorkgroups(p, (uint)blockCount, 1, 1),
                    out error))
            {
                return false;
            }

            if (blockCount > 1)
            {
                // Phase 2: Prefix sum over block_sums.
                if (!this.DispatchIntoComputePass(
                        flushContext,
                        pass,
                        "prepared-composite-tile-prefix-block-scan",
                        PreparedCompositeTilePrefixBlockScanComputeShader.Code,
                        TryCreatePreparedCompositeTilePrefixBlockScanBindGroupLayout,
                        (entries) =>
                        {
                            entries[0] = new BindGroupEntry { Binding = 0, Buffer = blockSumsBuffer, Offset = 0, Size = blockSumsSize };
                            entries[1] = new BindGroupEntry { Binding = 1, Buffer = prefixConfigBuffer, Offset = 0, Size = prefixConfigSize };
                            return 2;
                        },
                        (p) => flushContext.Api.ComputePassEncoderDispatchWorkgroups(p, 1, 1, 1),
                        out error))
                {
                    return false;
                }

                // Phase 3: Propagate block prefixes to tile_starts.
                if (!this.DispatchIntoComputePass(
                        flushContext,
                        pass,
                        "prepared-composite-tile-prefix-propagate",
                        PreparedCompositeTilePrefixPropagateComputeShader.Code,
                        TryCreatePreparedCompositeTilePrefixPropagateBindGroupLayout,
                        (entries) =>
                        {
                            entries[0] = new BindGroupEntry { Binding = 0, Buffer = blockSumsBuffer, Offset = 0, Size = blockSumsSize };
                            entries[1] = new BindGroupEntry { Binding = 1, Buffer = tileStartsBuffer, Offset = 0, Size = nuint.MaxValue };
                            entries[2] = new BindGroupEntry { Binding = 2, Buffer = dispatchConfigBuffer, Offset = 0, Size = (nuint)Unsafe.SizeOf<PreparedCompositeDispatchConfig>() };
                            return 3;
                        },
                        (p) => flushContext.Api.ComputePassEncoderDispatchWorkgroups(p, (uint)blockCount, 1, 1),
                        out error))
                {
                    return false;
                }
            }

            // Tile fill dispatched into the same pass.
            if (!this.DispatchIntoComputePass(
                    flushContext,
                    pass,
                    "prepared-composite-tile-fill",
                    PreparedCompositeTileFillComputeShader.Code,
                    TryCreatePreparedCompositeTileFillBindGroupLayout,
                    (entries) =>
                    {
                        entries[0] = new BindGroupEntry { Binding = 0, Buffer = commandBboxesBuffer, Offset = 0, Size = nuint.MaxValue };
                        entries[1] = new BindGroupEntry { Binding = 1, Buffer = binHeaderBuffer, Offset = 0, Size = nuint.MaxValue };
                        entries[2] = new BindGroupEntry { Binding = 2, Buffer = binDataBuffer, Offset = 0, Size = nuint.MaxValue };
                        entries[3] = new BindGroupEntry { Binding = 3, Buffer = tileStartsBuffer, Offset = 0, Size = nuint.MaxValue };
                        entries[4] = new BindGroupEntry { Binding = 4, Buffer = tileCountsBuffer, Offset = 0, Size = nuint.MaxValue };
                        entries[5] = new BindGroupEntry { Binding = 5, Buffer = tileCommandIndicesBuffer, Offset = 0, Size = nuint.MaxValue };
                        entries[6] = new BindGroupEntry { Binding = 6, Buffer = dispatchConfigBuffer, Offset = 0, Size = (nuint)Unsafe.SizeOf<PreparedCompositeDispatchConfig>() };
                        return 7;
                    },
                    (p) =>
                    {
                        uint workgroupCountX = (uint)widthInBins;
                        uint workgroupCountY = (uint)heightInBins;
                        if (workgroupCountX > 0 && workgroupCountY > 0)
                        {
                            flushContext.Api.ComputePassEncoderDispatchWorkgroups(p, workgroupCountX, workgroupCountY, 1);
                        }
                    },
                    out error))
            {
                return false;
            }

            return true;
        }
        finally
        {
            flushContext.Api.ComputePassEncoderEnd(pass);
            flushContext.Api.ComputePassEncoderRelease(pass);
        }
    }

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
    /// Creates the bind-group layout used by prepared composite compute shader.
    /// </summary>
    private static bool TryCreatePreparedCompositeFineBindGroupLayout(
        WebGPU api,
        Device* device,
        TextureFormat outputTextureFormat,
        TextureSampleType inputTextureSampleType,
        out BindGroupLayout* layout,
        out string? error)
    {
        BindGroupLayoutEntry* entries = stackalloc BindGroupLayoutEntry[11];
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
            Texture = new TextureBindingLayout
            {
                SampleType = inputTextureSampleType,
                ViewDimension = TextureViewDimension.Dimension2D,
                Multisampled = false
            }
        };
        entries[2] = new BindGroupLayoutEntry
        {
            Binding = 2,
            Visibility = ShaderStage.Compute,
            Texture = new TextureBindingLayout
            {
                SampleType = inputTextureSampleType,
                ViewDimension = TextureViewDimension.Dimension2D,
                Multisampled = false
            }
        };
        entries[3] = new BindGroupLayoutEntry
        {
            Binding = 3,
            Visibility = ShaderStage.Compute,
            StorageTexture = new StorageTextureBindingLayout
            {
                Access = StorageTextureAccess.WriteOnly,
                Format = outputTextureFormat,
                ViewDimension = TextureViewDimension.Dimension2D
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
                Type = BufferBindingType.ReadOnlyStorage,
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
                Type = BufferBindingType.Storage,
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
                Type = BufferBindingType.ReadOnlyStorage,
                HasDynamicOffset = false,
                MinBindingSize = 0
            }
        };
        entries[8] = new BindGroupLayoutEntry
        {
            Binding = 8,
            Visibility = ShaderStage.Compute,
            Buffer = new BufferBindingLayout
            {
                Type = BufferBindingType.Uniform,
                HasDynamicOffset = false,
                MinBindingSize = (nuint)Unsafe.SizeOf<PreparedCompositeDispatchConfig>()
            }
        };
        entries[9] = new BindGroupLayoutEntry
        {
            Binding = 9,
            Visibility = ShaderStage.Compute,
            Buffer = new BufferBindingLayout
            {
                Type = BufferBindingType.ReadOnlyStorage,
                HasDynamicOffset = false,
                MinBindingSize = 0
            }
        };
        entries[10] = new BindGroupLayoutEntry
        {
            Binding = 10,
            Visibility = ShaderStage.Compute,
            Buffer = new BufferBindingLayout
            {
                Type = BufferBindingType.ReadOnlyStorage,
                HasDynamicOffset = false,
                MinBindingSize = 0
            }
        };

        BindGroupLayoutDescriptor descriptor = new()
        {
            EntryCount = 11,
            Entries = entries
        };

        layout = api.DeviceCreateBindGroupLayout(device, in descriptor);
        if (layout is null)
        {
            error = "Failed to create prepared composite fine bind group layout.";
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
                Type = BufferBindingType.ReadOnlyStorage,
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
            error = "Failed to create prepared composite tile-count bind group layout.";
            return false;
        }

        error = null;
        return true;
    }

    private static bool TryCreatePreparedCompositeBinningBindGroupLayout(
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
            error = "Failed to create prepared composite binning bind group layout.";
            return false;
        }

        error = null;
        return true;
    }

    private static bool TryCreatePreparedCompositeTilePrefixLocalBindGroupLayout(
        WebGPU api,
        Device* device,
        out BindGroupLayout* layout,
        out string? error)
    {
        BindGroupLayoutEntry* entries = stackalloc BindGroupLayoutEntry[4];
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
                Type = BufferBindingType.Uniform,
                HasDynamicOffset = false,
                MinBindingSize = (nuint)Unsafe.SizeOf<PreparedCompositeDispatchConfig>()
            }
        };

        BindGroupLayoutDescriptor descriptor = new()
        {
            EntryCount = 4,
            Entries = entries
        };

        layout = api.DeviceCreateBindGroupLayout(device, in descriptor);
        if (layout is null)
        {
            error = "Failed to create prepared composite tile-prefix-local bind group layout.";
            return false;
        }

        error = null;
        return true;
    }

    private static bool TryCreatePreparedCompositeTilePrefixBlockScanBindGroupLayout(
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
                Type = BufferBindingType.Uniform,
                HasDynamicOffset = false,
                MinBindingSize = sizeof(uint)
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
            error = "Failed to create prepared composite tile-prefix-block-scan bind group layout.";
            return false;
        }

        error = null;
        return true;
    }

    private static bool TryCreatePreparedCompositeTilePrefixPropagateBindGroupLayout(
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
            error = "Failed to create prepared composite tile-prefix-propagate bind group layout.";
            return false;
        }

        error = null;
        return true;
    }

    private static bool TryCreatePreparedCompositeTileFillBindGroupLayout(
        WebGPU api,
        Device* device,
        out BindGroupLayout* layout,
        out string? error)
    {
        BindGroupLayoutEntry* entries = stackalloc BindGroupLayoutEntry[7];
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
                Type = BufferBindingType.ReadOnlyStorage,
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
                Type = BufferBindingType.Storage,
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
                Type = BufferBindingType.Uniform,
                HasDynamicOffset = false,
                MinBindingSize = (nuint)Unsafe.SizeOf<PreparedCompositeDispatchConfig>()
            }
        };

        BindGroupLayoutDescriptor descriptor = new()
        {
            EntryCount = 7,
            Entries = entries
        };

        layout = api.DeviceCreateBindGroupLayout(device, in descriptor);
        if (layout is null)
        {
            error = "Failed to create prepared composite tile-fill bind group layout.";
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
    private static void CopyTextureRegion(
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint FloatToUInt32Bits(float value)
        => unchecked((uint)BitConverter.SingleToInt32Bits(value));

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

        // When ReadbackSourceOverride is set, the output texture already contains the
        // composited result at (0,0), so we read from there instead of the target texture.
        bool useOverride = flushContext.ReadbackSourceOverride is not null;
        Texture* readbackTexture = useOverride ? flushContext.ReadbackSourceOverride : flushContext.TargetTexture;
        uint readbackOriginX = useOverride ? 0 : (uint)copyBounds.X;
        uint readbackOriginY = useOverride ? 0 : (uint)copyBounds.Y;

        ImageCopyTexture source = new()
        {
            Texture = readbackTexture,
            MipLevel = 0,
            Origin = new Origin3D(readbackOriginX, readbackOriginY, 0),
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
            int destinationStrideBytes = checked(destinationRegion.Buffer.RowStride * Unsafe.SizeOf<TPixel>());

            // Fast path for contiguous full-width rows.
            if (copyBounds.X == 0 &&
                copyBounds.Width == destinationRegion.Width &&
                destinationRegion.Buffer.DangerousTryGetSingleMemory(out Memory<TPixel> contiguousDestination))
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

        this.DisposeCoverageResources();
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
    /// Key that identifies a coverage definition for reuse within a flush.
    /// </summary>
    private readonly struct CoverageDefinitionIdentity : IEquatable<CoverageDefinitionIdentity>
    {
        private readonly int definitionKey;
        private readonly IPath path;
        private readonly Rectangle interest;
        private readonly IntersectionRule intersectionRule;
        private readonly RasterizationMode rasterizationMode;
        private readonly RasterizerSamplingOrigin samplingOrigin;

        public CoverageDefinitionIdentity(in CompositionCoverageDefinition definition)
        {
            this.definitionKey = definition.DefinitionKey;
            this.path = definition.Path;
            this.interest = definition.RasterizerOptions.Interest;
            this.intersectionRule = definition.RasterizerOptions.IntersectionRule;
            this.rasterizationMode = definition.RasterizerOptions.RasterizationMode;
            this.samplingOrigin = definition.RasterizerOptions.SamplingOrigin;
        }

        /// <summary>
        /// Determines whether this identity equals the provided coverage identity.
        /// </summary>
        /// <param name="other">The identity to compare.</param>
        /// <returns><see langword="true"/> when the identities describe the same coverage definition; otherwise <see langword="false"/>.</returns>
        public bool Equals(CoverageDefinitionIdentity other)
            => this.definitionKey == other.definitionKey &&
               ReferenceEquals(this.path, other.path) &&
               this.interest.Equals(other.interest) &&
               this.intersectionRule == other.intersectionRule &&
               this.rasterizationMode == other.rasterizationMode &&
               this.samplingOrigin == other.samplingOrigin;

        /// <inheritdoc/>
        public override bool Equals(object? obj)
            => obj is CoverageDefinitionIdentity other && this.Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode()
            => HashCode.Combine(
                this.definitionKey,
                RuntimeHelpers.GetHashCode(this.path),
                this.interest,
                (int)this.intersectionRule,
                (int)this.rasterizationMode,
                (int)this.samplingOrigin);
    }

    private readonly struct EdgePlacement
    {
        public EdgePlacement(uint edgeStart, uint edgeCount, uint fillRule, uint csrOffsetsStart, uint csrBandCount)
        {
            this.EdgeStart = edgeStart;
            this.EdgeCount = edgeCount;
            this.FillRule = fillRule;
            this.CsrOffsetsStart = csrOffsetsStart;
            this.CsrBandCount = csrBandCount;
        }

        public uint EdgeStart { get; }

        public uint EdgeCount { get; }

        public uint FillRule { get; }

        public uint CsrOffsetsStart { get; }

        public uint CsrBandCount { get; }
    }

    /// <summary>
    /// Dispatch constants shared across composite compute passes.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private readonly struct PreparedCompositeDispatchConfig
    {
        public readonly uint TargetWidth;
        public readonly uint TargetHeight;
        public readonly uint TileCountX;
        public readonly uint TileCountY;
        public readonly uint TileCount;
        public readonly uint CommandCount;
        public readonly uint SourceOriginX;
        public readonly uint SourceOriginY;
        public readonly uint OutputOriginX;
        public readonly uint OutputOriginY;
        public readonly uint WidthInBins;
        public readonly uint HeightInBins;
        public readonly uint BinCount;
        public readonly uint PartitionCount;
        public readonly uint BinningSize;
        public readonly uint BinDataStart;

        public PreparedCompositeDispatchConfig(
            uint targetWidth,
            uint targetHeight,
            uint tileCountX,
            uint tileCountY,
            uint tileCount,
            uint commandCount,
            uint sourceOriginX,
            uint sourceOriginY,
            uint outputOriginX,
            uint outputOriginY,
            uint widthInBins,
            uint heightInBins,
            uint binCount,
            uint partitionCount,
            uint binningSize,
            uint binDataStart)
        {
            this.TargetWidth = targetWidth;
            this.TargetHeight = targetHeight;
            this.TileCountX = tileCountX;
            this.TileCountY = tileCountY;
            this.TileCount = tileCount;
            this.CommandCount = commandCount;
            this.SourceOriginX = sourceOriginX;
            this.SourceOriginY = sourceOriginY;
            this.OutputOriginX = outputOriginX;
            this.OutputOriginY = outputOriginY;
            this.WidthInBins = widthInBins;
            this.HeightInBins = heightInBins;
            this.BinCount = binCount;
            this.PartitionCount = partitionCount;
            this.BinningSize = binningSize;
            this.BinDataStart = binDataStart;
        }
    }

    /// <summary>
    /// Integer bounding box for a prepared composite command in destination-local coordinates.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private readonly struct PreparedCompositeCommandBbox
    {
        public readonly int X0;
        public readonly int Y0;
        public readonly int X1;
        public readonly int Y1;

        public PreparedCompositeCommandBbox(int x0, int y0, int x1, int y1)
        {
            this.X0 = x0;
            this.Y0 = y0;
            this.X1 = x1;
            this.Y1 = y1;
        }
    }

    /// <summary>
    /// Per-bin header describing the packed command list region.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private readonly struct PreparedCompositeBinHeader
    {
        public readonly uint ElementCount;
        public readonly uint ChunkOffset;

        public PreparedCompositeBinHeader(uint elementCount, uint chunkOffset)
        {
            this.ElementCount = elementCount;
            this.ChunkOffset = chunkOffset;
        }
    }

    /// <summary>
    /// Bump allocator state for command binning.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private readonly struct PreparedCompositeBinningBump
    {
        public readonly uint Failed;
        public readonly uint Binning;

        public PreparedCompositeBinningBump(uint failed, uint binning)
        {
            this.Failed = failed;
            this.Binning = binning;
        }
    }

    /// <summary>
    /// Prepared composite command parameters consumed by <see cref="PreparedCompositeFineComputeShader"/>.
    /// Layout matches the WGSL <c>Params</c> struct exactly (24 u32 fields = 96 bytes).
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private readonly struct PreparedCompositeParameters
    {
        public readonly uint DestinationX;
        public readonly uint DestinationY;
        public readonly uint DestinationWidth;
        public readonly uint DestinationHeight;
        public readonly uint EdgeStart;
        public readonly uint FillRuleValue;
        public readonly uint EdgeOriginX;
        public readonly uint EdgeOriginY;
        public readonly uint CsrOffsetsStart;
        public readonly uint CsrBandCount;
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
            uint edgeStart,
            uint fillRuleValue,
            int edgeOriginX,
            int edgeOriginY,
            uint csrOffsetsStart,
            uint csrBandCount,
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
            this.EdgeStart = edgeStart;
            this.FillRuleValue = fillRuleValue;
            this.EdgeOriginX = unchecked((uint)edgeOriginX);
            this.EdgeOriginY = unchecked((uint)edgeOriginY);
            this.CsrOffsetsStart = csrOffsetsStart;
            this.CsrBandCount = csrBandCount;
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
