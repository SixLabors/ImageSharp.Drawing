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
/// </remarks>
public sealed unsafe partial class WebGPUDrawingBackend : IDrawingBackend, IDisposable
{
    private const int CompositeTileWidth = 16;
    private const int CompositeTileHeight = 16;

    private const string PreparedCompositeParamsBufferKey = "prepared-composite/params";
    private const string PreparedCompositeDispatchConfigBufferKey = "prepared-composite/dispatch-config";
    private const string PreparedCompositeColorStopsBufferKey = "prepared-composite/color-stops";
    private const string StrokeExpandPipelineKey = "stroke-expand";
    private const string StrokeExpandCommandsBufferKey = "stroke-expand/commands";
    private const string StrokeExpandConfigBufferKey = "stroke-expand/config";
    private const string StrokeExpandCounterBufferKey = "stroke-expand/counter";
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
    /// GPU brush type identifiers. Values match the WGSL <c>brush_type</c> field constants.
    /// </summary>
    private enum PreparedBrushType : uint
    {
        Solid = 0,
        Image = 1,
        LinearGradient = 2,
        RadialGradient = 3,
        RadialGradientTwoCircle = 4,
        EllipticGradient = 5,
        SweepGradient = 6,
        Pattern = 7,
        Recolor = 8,
    }

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

    /// <inheritdoc />
    public void FlushCompositions<TPixel>(
        Configuration configuration,
        ICanvasFrame<TPixel> target,
        CompositionScene compositionScene)
        where TPixel : unmanaged, IPixel<TPixel>
    {
#if DEBUG_TIMING
        long tMethodStart = Stopwatch.GetTimestamp();
#endif
        this.ThrowIfDisposed();
        if (compositionScene.Commands.Count == 0)
        {
            return;
        }

        if (!TryGetCompositeTextureFormat<TPixel>(out WebGPUTextureFormatId formatId, out FeatureName requiredFeature) ||
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
            List<PreparedCompositionCommand> commands = batch.Commands;
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

        WebGPUFlushContext? flushContext = WebGPUFlushContext.Create(
            target,
            textureFormat,
            requiredFeature,
            pixelSizeInBytes,
            configuration.MemoryAllocator,
            compositionBounds);

        if (flushContext is null)
        {
            this.TestingFallbackPrepareCoverageCallCount += commandCount;
            this.TestingFallbackCompositeCoverageCallCount += commandCount;
            this.FlushCompositionsFallback(
                configuration,
                target,
                compositionScene,
                target.TryGetCpuRegion(out Buffer2DRegion<TPixel> _),
                compositionBounds);
            return;
        }

        try
        {
            gpuReady = true;
            this.TestingPrepareCoverageCallCount += commandCount;
            this.TestingReleaseCoverageCallCount += commandCount;

            bool renderOk = this.TryRenderPreparedFlush<TPixel>(
                flushContext,
                preparedBatches,
                configuration,
                target.Bounds,
                compositionBounds.Value,
                commandCount,
                out Rectangle effectiveBounds,
                out failure);

            bool finalizeOk = renderOk && this.TryFinalizeFlush(flushContext, cpuRegion, effectiveBounds);

            gpuSuccess = finalizeOk;
        }
        catch (Exception ex)
        {
            failure = ex.Message;
            gpuSuccess = false;
        }
        finally
        {
            flushContext.Dispose();
            this.DisposeCoverageResources();
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

    /// <inheritdoc />
    public void ReleaseFrameResources<TPixel>(
        Configuration configuration,
        ICanvasFrame<TPixel> target)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        nint targetIdentity = (nint)RuntimeHelpers.GetHashCode(target);
        WebGPUFlushContext.ReleaseCpuTargetEntries(targetIdentity);
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
    private static bool IsSupportedCompositionBrush(Brush brush)
        => brush is SolidBrush
            or ImageBrush
            or LinearGradientBrush
            or RadialGradientBrush
            or EllipticGradientBrush
            or SweepGradientBrush
            or PatternBrush
            or RecolorBrush;

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
        ICanvasFrame<TPixel> stagingFrame = new MemoryCanvasFrame<TPixel>(stagingRegion);
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
        out Rectangle effectiveCompositionBounds,
        out string? error)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        effectiveCompositionBounds = compositionBounds;
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
            List<PreparedCompositionCommand> commands = batch.Commands;
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

        // Prepare stroke definitions for GPU distance-field evaluation.
        // Instead of expanding to a filled outline on the CPU, we compute
        // the interest rectangle from the centerline bounds inflated by
        // half the stroke width and pass the centerline edges to the GPU.
        // For dashed strokes, we pre-split the path into dash segments
        // on the CPU (cheap) while keeping the actual stroke coverage on GPU.
        for (int i = 0; i < coverageDefinitions.Count; i++)
        {
            CompositionCoverageDefinition definition = coverageDefinitions[i];
            if (!definition.IsStroke)
            {
                continue;
            }

            IPath strokePath = definition.Path;

            // For dashed strokes, split the path into dash segments.
            // This reuses the outline generation with a minimal width to
            // produce the dash-split centerline path, but instead we use
            // the dedicated dash splitting API.
            if (definition.StrokePattern.Length > 0)
            {
                // For dashed strokes, split the path into dash segments on the CPU
                // so the GPU evaluates solid strokes on each dash segment.
                strokePath = strokePath.GenerateDashes(definition.StrokeWidth, definition.StrokePattern.Span);
            }

            float halfWidth = definition.StrokeWidth * 0.5f;
            float maxExtent = halfWidth * Math.Max((float)(definition.StrokeOptions?.MiterLimit ?? 4.0), 1.0f);

            RectangleF pathBounds = strokePath.Bounds;
            pathBounds = new RectangleF(
                pathBounds.X + 0.5F - maxExtent,
                pathBounds.Y + 0.5F - maxExtent,
                pathBounds.Width + (maxExtent * 2),
                pathBounds.Height + (maxExtent * 2));

            Rectangle interest = Rectangle.FromLTRB(
                (int)MathF.Floor(pathBounds.Left),
                (int)MathF.Floor(pathBounds.Top),
                (int)MathF.Ceiling(pathBounds.Right),
                (int)MathF.Ceiling(pathBounds.Bottom));

            RasterizerOptions opts = definition.RasterizerOptions;
            coverageDefinitions[i] = new CompositionCoverageDefinition(
                definition.DefinitionKey,
                strokePath,
                new RasterizerOptions(interest, opts.IntersectionRule, opts.RasterizationMode, opts.SamplingOrigin, opts.AntialiasThreshold),
                definition.DestinationOffset,
                definition.StrokeOptions,
                definition.StrokeWidth,
                definition.StrokePattern);

            // Re-prepare all batches that reference this coverage definition.
            for (int b = 0; b < preparedBatches.Count; b++)
            {
                if (batchCoverageIndices[b] == i)
                {
                    CompositionScenePlanner.ReprepareBatchCommands(
                        preparedBatches[b].Commands,
                        targetBounds,
                        interest);
                }
            }
        }

        // Recompute effective composition bounds from updated command destinations
        // after stroke re-preparation tightened the interest rectangles.
        Rectangle targetExtent = new(0, 0, flushContext.TargetBounds.Width, flushContext.TargetBounds.Height);
        Rectangle? tightBounds = null;
        for (int batchIndex = 0; batchIndex < preparedBatches.Count; batchIndex++)
        {
            List<PreparedCompositionCommand> cmds = preparedBatches[batchIndex].Commands;
            for (int i = 0; i < cmds.Count; i++)
            {
                Rectangle destination = Rectangle.Intersect(cmds[i].DestinationRegion, targetExtent);
                if (destination.Width > 0 && destination.Height > 0)
                {
                    tightBounds = tightBounds.HasValue
                        ? Rectangle.Union(tightBounds.Value, destination)
                        : destination;
                }
            }
        }

        if (tightBounds.HasValue)
        {
            effectiveCompositionBounds = tightBounds.Value;
        }

        if (!this.TryCreateEdgeBuffer<TPixel>(
                flushContext,
                coverageDefinitions,
                configuration,
                out WgpuBuffer* edgeBuffer,
                out nuint edgeBufferSize,
                out EdgePlacement[] edgePlacements,
                out int totalEdgeCount,
                out int totalBandOffsetEntries,
                out WgpuBuffer* bandOffsetsBuffer,
                out nuint bandOffsetsBufferSize,
                out StrokeExpandInfo strokeExpandInfo,
                out error))
        {
            return false;
        }

        // Dispatch stroke expansion before composite rasterization.
        // This generates outline edges from centerline edges in a separate compute pass.
        if (strokeExpandInfo.HasCommands)
        {
            if (!this.TryDispatchStrokeExpand(
                    flushContext,
                    edgeBuffer,
                    edgeBufferSize,
                    strokeExpandInfo,
                    out error))
            {
                return false;
            }
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
                bandOffsetsBuffer,
                bandOffsetsBufferSize,
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
        WgpuBuffer* bandOffsetsBuffer,
        nuint bandOffsetsBufferSize,
        out string? error)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        error = null;
        if (commandCount == 0)
        {
            return true;
        }

        if (!CompositeComputeShader.TryGetCode(flushContext.TextureFormat, out byte[] shaderCode, out error))
        {
            return false;
        }

        if (!CompositeComputeShader.TryGetInputSampleType(flushContext.TextureFormat, out TextureSampleType inputTextureSampleType))
        {
            error = $"Prepared composite fine shader does not support texture format '{flushContext.TextureFormat}'.";
            return false;
        }

        string pipelineKey = $"prepared-composite-fine/{flushContext.TextureFormat}";
        bool LayoutFactory(WebGPU api, Device* device, out BindGroupLayout* layout, out string? layoutError)
            => TryCreateCompositeBindGroupLayout(
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
        if (tileCount == 0)
        {
            return true;
        }

        uint parameterSize = (uint)Unsafe.SizeOf<PreparedCompositeParameters>();
        IMemoryOwner<PreparedCompositeParameters> parametersOwner =
            flushContext.MemoryAllocator.Allocate<PreparedCompositeParameters>(commandCount);
        List<float> colorStopsList = [];
        try
        {
            int flushCommandCount = commandCount;
            Span<PreparedCompositeParameters> parameters = parametersOwner.Memory.Span;
            TextureView* brushTextureView = backdropTextureView;
            nint brushTextureViewHandle = (nint)backdropTextureView;
            bool hasImageTexture = false;

            int commandIndex = 0;
            for (int batchIndex = 0; batchIndex < preparedBatches.Count; batchIndex++)
            {
                int coverageDefinitionIndex = batchCoverageIndices[batchIndex];
                if (coverageDefinitionIndex < 0)
                {
                    continue;
                }

                List<PreparedCompositionCommand> commands = preparedBatches[batchIndex].Commands;
                for (int i = 0; i < commands.Count; i++)
                {
                    PreparedCompositionCommand command = commands[i];

                    PreparedBrushType brushType;
                    int brushOriginX = 0;
                    int brushOriginY = 0;
                    int brushRegionX = 0;
                    int brushRegionY = 0;
                    int brushRegionWidth = 1;
                    int brushRegionHeight = 1;
                    Vector4 solidColor = default;
                    uint gp4 = 0, gp5 = 0, gp6 = 0, gp7 = 0;
                    uint stopsOffset = 0, stopCount = 0;

                    if (command.Brush is SolidBrush solidBrush)
                    {
                        brushType = PreparedBrushType.Solid;
                        solidColor = solidBrush.Color.ToScaledVector4();
                    }
                    else if (command.Brush is ImageBrush imageBrush)
                    {
                        brushType = PreparedBrushType.Image;
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
                    else if (command.Brush is LinearGradientBrush linearBrush)
                    {
                        brushType = PreparedBrushType.LinearGradient;
                        PointF start = linearBrush.StartPoint;
                        PointF end = linearBrush.EndPoint;

                        solidColor = new Vector4(start.X, start.Y, end.X, end.Y);
                        gp4 = (uint)linearBrush.RepetitionMode;
                        PackColorStops(linearBrush.ColorStops, colorStopsList, out stopsOffset, out stopCount);
                    }
                    else if (command.Brush is RadialGradientBrush radialBrush)
                    {
                        if (radialBrush.IsTwoCircle)
                        {
                            brushType = PreparedBrushType.RadialGradientTwoCircle;

                            // Pass raw brush properties; shader computes derived values.
                            solidColor = new Vector4(radialBrush.Center0.X, radialBrush.Center0.Y, radialBrush.Center1!.Value.X, radialBrush.Center1.Value.Y);
                            gp4 = FloatToUInt32Bits(radialBrush.Radius0);
                            gp5 = FloatToUInt32Bits(radialBrush.Radius1!.Value);
                            gp6 = (uint)radialBrush.RepetitionMode;
                        }
                        else
                        {
                            brushType = PreparedBrushType.RadialGradient;

                            // Pass raw brush properties; shader computes derived values.
                            solidColor = new Vector4(radialBrush.Center0.X, radialBrush.Center0.Y, radialBrush.Radius0, 0f);
                            gp4 = (uint)radialBrush.RepetitionMode;
                        }

                        PackColorStops(radialBrush.ColorStops, colorStopsList, out stopsOffset, out stopCount);
                    }
                    else if (command.Brush is EllipticGradientBrush ellipticBrush)
                    {
                        brushType = PreparedBrushType.EllipticGradient;

                        // Pass raw brush properties; shader computes rotation and radii.
                        solidColor = new Vector4(ellipticBrush.Center.X, ellipticBrush.Center.Y, ellipticBrush.ReferenceAxisEnd.X, ellipticBrush.ReferenceAxisEnd.Y);
                        gp4 = FloatToUInt32Bits(ellipticBrush.AxisRatio);
                        gp5 = (uint)ellipticBrush.RepetitionMode;
                        PackColorStops(ellipticBrush.ColorStops, colorStopsList, out stopsOffset, out stopCount);
                    }
                    else if (command.Brush is SweepGradientBrush sweepBrush)
                    {
                        brushType = PreparedBrushType.SweepGradient;

                        // Pass raw brush properties; shader computes radians and sweep.
                        solidColor = new Vector4(sweepBrush.Center.X, sweepBrush.Center.Y, sweepBrush.StartAngleDegrees, sweepBrush.EndAngleDegrees);
                        gp4 = (uint)sweepBrush.RepetitionMode;
                        PackColorStops(sweepBrush.ColorStops, colorStopsList, out stopsOffset, out stopCount);
                    }
                    else if (command.Brush is PatternBrush patternBrush)
                    {
                        brushType = PreparedBrushType.Pattern;
                        DenseMatrix<Color> pattern = patternBrush.Pattern;
                        solidColor = new Vector4(pattern.Columns, pattern.Rows, 0f, 0f);
                        PackPatternColors(pattern, colorStopsList, out stopsOffset);
                    }
                    else if (command.Brush is RecolorBrush recolorBrush)
                    {
                        brushType = PreparedBrushType.Recolor;
                        Vector4 src = recolorBrush.SourceColor.ToScaledVector4();
                        Vector4 tgt = recolorBrush.TargetColor.ToScaledVector4();
                        solidColor = src;
                        gp4 = FloatToUInt32Bits(tgt.X);
                        gp5 = FloatToUInt32Bits(tgt.Y);
                        gp6 = FloatToUInt32Bits(tgt.Z);
                        gp7 = FloatToUInt32Bits(tgt.W);
                        stopsOffset = FloatToUInt32Bits(recolorBrush.Threshold);
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
                        solidColor,
                        command.GraphicsOptions.Antialias ? 0u : 1u,
                        command.GraphicsOptions.AntialiasThreshold,
                        gp4,
                        gp5,
                        gp6,
                        gp7,
                        stopsOffset,
                        stopCount);

                    parameters[commandIndex] = commandParameters;
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
                0,
                0,
                0,
                0,
                0,
                0);
            flushContext.Api.QueueWriteBuffer(
                flushContext.Queue,
                dispatchConfigBuffer,
                0,
                &dispatchConfig,
                dispatchConfigSize);

            // Color stops / pattern buffer (binding 7).
            nuint colorStopsBufferSize = colorStopsList.Count > 0
                ? (nuint)(colorStopsList.Count * sizeof(float))
                : 20; // minimum 1 ColorStop (5 × f32 = 20 bytes)
            if (!flushContext.DeviceState.TryGetOrCreateSharedBuffer(
                    PreparedCompositeColorStopsBufferKey,
                    BufferUsage.Storage | BufferUsage.CopyDst,
                    colorStopsBufferSize,
                    out WgpuBuffer* colorStopsBuffer,
                    out _,
                    out error))
            {
                return false;
            }

            if (colorStopsList.Count > 0)
            {
                Span<float> stopsSpan = CollectionsMarshal.AsSpan(colorStopsList);
                fixed (float* stopsPtr = stopsSpan)
                {
                    flushContext.Api.QueueWriteBuffer(
                        flushContext.Queue,
                        colorStopsBuffer,
                        0,
                        stopsPtr,
                        (nuint)(stopsSpan.Length * sizeof(float)));
                }
            }

            // Band offsets are pre-computed on CPU and uploaded directly.
            // Edges are pre-split at band boundaries, eliminating CSR index indirection.
            BindGroupEntry* bindGroupEntries = stackalloc BindGroupEntry[8];
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
                Buffer = dispatchConfigBuffer,
                Offset = 0,
                Size = dispatchConfigSize
            };
            bindGroupEntries[6] = new BindGroupEntry
            {
                Binding = 6,
                Buffer = bandOffsetsBuffer,
                Offset = 0,
                Size = bandOffsetsBufferSize
            };
            bindGroupEntries[7] = new BindGroupEntry
            {
                Binding = 7,
                Buffer = colorStopsBuffer,
                Offset = 0,
                Size = colorStopsBufferSize
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
        }

        error = null;
        return true;
    }

    /// <summary>
    /// Dispatches the stroke expand compute shader to generate outline edges
    /// from centerline edges. Must be called before the composite dispatch
    /// so the generated edges are available for the fill rasterizer.
    /// </summary>
    private bool TryDispatchStrokeExpand(
        WebGPUFlushContext flushContext,
        WgpuBuffer* edgeBuffer,
        nuint edgeBufferSize,
        StrokeExpandInfo expandInfo,
        out string? error)
    {
        error = null;
        if (!expandInfo.HasCommands)
        {
            return true;
        }

        List<StrokeExpandCommand> commands = expandInfo.Commands!;

        // Create or get the pipeline.
        static bool LayoutFactory(WebGPU api, Device* device, out BindGroupLayout* layout, out string? layoutError)
            => TryCreateStrokeExpandBindGroupLayout(api, device, out layout, out layoutError);

        if (!flushContext.DeviceState.TryGetOrCreateCompositeComputePipeline(
                StrokeExpandPipelineKey,
                StrokeExpandComputeShader.Code,
                LayoutFactory,
                out BindGroupLayout* bindGroupLayout,
                out ComputePipeline* pipeline,
                out error))
        {
            return false;
        }

        // Build GPU command array.
        int commandCount = commands.Count;
        using IMemoryOwner<GpuStrokeExpandCommand> gpuCommandsOwner = flushContext.MemoryAllocator.Allocate<GpuStrokeExpandCommand>(commandCount);
        Span<GpuStrokeExpandCommand> gpuCommands = gpuCommandsOwner.Memory.Span;
        for (int i = 0; i < commandCount; i++)
        {
            gpuCommands[i] = new GpuStrokeExpandCommand(commands[i]);
        }

        nuint commandsSize = (nuint)(commandCount * Unsafe.SizeOf<GpuStrokeExpandCommand>());
        if (!flushContext.DeviceState.TryGetOrCreateSharedBuffer(
                StrokeExpandCommandsBufferKey,
                BufferUsage.Storage | BufferUsage.CopyDst,
                commandsSize,
                out WgpuBuffer* commandsBuffer,
                out _,
                out error))
        {
            return false;
        }

        fixed (GpuStrokeExpandCommand* commandsPtr = &MemoryMarshal.GetReference(gpuCommands))
        {
            flushContext.Api.QueueWriteBuffer(
                flushContext.Queue,
                commandsBuffer,
                0,
                commandsPtr,
                commandsSize);
        }

        // Config uniform.
        nuint configSize = (nuint)Unsafe.SizeOf<GpuStrokeExpandConfig>();
        if (!flushContext.DeviceState.TryGetOrCreateSharedBuffer(
                StrokeExpandConfigBufferKey,
                BufferUsage.Uniform | BufferUsage.CopyDst,
                configSize,
                out WgpuBuffer* configBuffer,
                out _,
                out error))
        {
            return false;
        }

        GpuStrokeExpandConfig config = new(
            (uint)expandInfo.TotalCenterlineEdges,
            (uint)commandCount);
        flushContext.Api.QueueWriteBuffer(
            flushContext.Queue,
            configBuffer,
            0,
            &config,
            configSize);

        // Atomic output counters — one u32 per command, initialized to 0.
        nuint counterSize = (nuint)(expandInfo.Commands!.Count * sizeof(uint));
        if (!flushContext.DeviceState.TryGetOrCreateSharedBuffer(
                StrokeExpandCounterBufferKey,
                BufferUsage.Storage | BufferUsage.CopyDst,
                counterSize,
                out WgpuBuffer* counterBuffer,
                out _,
                out error))
        {
            return false;
        }

        // Clear the counter to 0.
        flushContext.Api.CommandEncoderClearBuffer(flushContext.CommandEncoder, counterBuffer, 0, counterSize);

        // Bind group.
        BindGroupEntry* bindGroupEntries = stackalloc BindGroupEntry[4];
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
            Buffer = commandsBuffer,
            Offset = 0,
            Size = commandsSize
        };
        bindGroupEntries[2] = new BindGroupEntry
        {
            Binding = 2,
            Buffer = configBuffer,
            Offset = 0,
            Size = configSize
        };
        bindGroupEntries[3] = new BindGroupEntry
        {
            Binding = 3,
            Buffer = counterBuffer,
            Offset = 0,
            Size = counterSize
        };

        BindGroupDescriptor bindGroupDescriptor = new()
        {
            Layout = bindGroupLayout,
            EntryCount = 4,
            Entries = bindGroupEntries
        };

        BindGroup* bindGroup = flushContext.Api.DeviceCreateBindGroup(flushContext.Device, in bindGroupDescriptor);
        if (bindGroup is null)
        {
            error = "Failed to create stroke expand bind group.";
            return false;
        }

        flushContext.TrackBindGroup(bindGroup);

        // Dispatch in a separate compute pass (guarantees ordering before composite pass).
        ComputePassDescriptor passDescriptor = default;
        ComputePassEncoder* passEncoder = flushContext.Api.CommandEncoderBeginComputePass(
            flushContext.CommandEncoder, in passDescriptor);
        if (passEncoder is null)
        {
            error = "Failed to begin stroke expand compute pass.";
            return false;
        }

        try
        {
            uint workgroupCount = DivideRoundUp(expandInfo.TotalCenterlineEdges, 256);
            flushContext.Api.ComputePassEncoderSetPipeline(passEncoder, pipeline);
            flushContext.Api.ComputePassEncoderSetBindGroup(passEncoder, 0, bindGroup, 0, null);
            flushContext.Api.ComputePassEncoderDispatchWorkgroups(passEncoder, workgroupCount, 1, 1);
        }
        finally
        {
            flushContext.Api.ComputePassEncoderEnd(passEncoder);
            flushContext.Api.ComputePassEncoderRelease(passEncoder);
        }

        return true;
    }

    private static bool TryCreateStrokeExpandBindGroupLayout(
        WebGPU api,
        Device* device,
        out BindGroupLayout* layout,
        out string? error)
    {
        layout = null;
        BindGroupLayoutEntry* entries = stackalloc BindGroupLayoutEntry[4];
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
                Type = BufferBindingType.Uniform,
                HasDynamicOffset = false,
                MinBindingSize = (nuint)Unsafe.SizeOf<GpuStrokeExpandConfig>()
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
                MinBindingSize = sizeof(uint)
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
            error = "Failed to create stroke expand bind group layout.";
            return false;
        }

        error = null;
        return true;
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
    private static bool TryCreateCompositeBindGroupLayout(
        WebGPU api,
        Device* device,
        TextureFormat outputTextureFormat,
        TextureSampleType inputTextureSampleType,
        out BindGroupLayout* layout,
        out string? error)
    {
        BindGroupLayoutEntry* entries = stackalloc BindGroupLayoutEntry[8];
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
                Type = BufferBindingType.Uniform,
                HasDynamicOffset = false,
                MinBindingSize = (nuint)Unsafe.SizeOf<PreparedCompositeDispatchConfig>()
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
                Type = BufferBindingType.ReadOnlyStorage,
                HasDynamicOffset = false,
                MinBindingSize = 0
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
            error = "Failed to create prepared composite fine bind group layout.";
            return false;
        }

        error = null;
        return true;
    }

    private static bool TryCreateCsrPrefixLocalBindGroupLayout(
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

    private static bool TryCreateCsrPrefixBlockScanBindGroupLayout(
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

    private static bool TryCreateCsrPrefixPropagateBindGroupLayout(
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
    /// Packs color stops into the shared float list for GPU upload.
    /// Each stop is 5 floats: ratio, R, G, B, A (matching the WGSL <c>ColorStop</c> struct).
    /// </summary>
    private static void PackColorStops(
        ReadOnlySpan<ColorStop> stops,
        List<float> buffer,
        out uint offset,
        out uint count)
    {
        offset = (uint)(buffer.Count / 5);
        count = (uint)stops.Length;
        for (int i = 0; i < stops.Length; i++)
        {
            ColorStop stop = stops[i];
            Vector4 color = stop.Color.ToScaledVector4();
            buffer.Add(stop.Ratio);
            buffer.Add(color.X);
            buffer.Add(color.Y);
            buffer.Add(color.Z);
            buffer.Add(color.W);
        }
    }

    /// <summary>
    /// Packs pattern colors into the shared float list for GPU upload.
    /// Each cell is 5 floats: ratio (0), R, G, B, A (reusing the <c>ColorStop</c> layout).
    /// </summary>
    private static void PackPatternColors(
        DenseMatrix<Color> pattern,
        List<float> buffer,
        out uint offset)
    {
        offset = (uint)(buffer.Count / 5);
        ReadOnlySpan<Color> data = pattern.Data;
        for (int i = 0; i < data.Length; i++)
        {
            Vector4 color = data[i].ToScaledVector4();
            buffer.Add(0f); // ratio unused
            buffer.Add(color.X);
            buffer.Add(color.Y);
            buffer.Add(color.Z);
            buffer.Add(color.W);
        }
    }

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
        private readonly float antialiasThreshold;

        public CoverageDefinitionIdentity(in CompositionCoverageDefinition definition)
        {
            this.definitionKey = definition.DefinitionKey;
            this.path = definition.Path;
            this.interest = definition.RasterizerOptions.Interest;
            this.intersectionRule = definition.RasterizerOptions.IntersectionRule;
            this.rasterizationMode = definition.RasterizerOptions.RasterizationMode;
            this.samplingOrigin = definition.RasterizerOptions.SamplingOrigin;
            this.antialiasThreshold = definition.RasterizerOptions.AntialiasThreshold;
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
               this.samplingOrigin == other.samplingOrigin &&
               this.antialiasThreshold == other.antialiasThreshold;

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
                (int)this.samplingOrigin,
                this.antialiasThreshold);
    }

    private readonly struct EdgePlacement
    {
        public EdgePlacement(
            uint edgeStart,
            uint edgeCount,
            uint fillRule,
            uint csrOffsetsStart,
            uint csrBandCount)
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
    /// Prepared composite command parameters consumed by <see cref="CompositeComputeShader"/>.
    /// Layout matches the WGSL <c>Params</c> struct exactly (32 u32 fields = 128 bytes).
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

        /// <summary>General-purpose brush param 0. For solid brush: R. For gradients: see plan.</summary>
        public readonly uint Gp0;

        /// <summary>General-purpose brush param 1. For solid brush: G.</summary>
        public readonly uint Gp1;

        /// <summary>General-purpose brush param 2. For solid brush: B.</summary>
        public readonly uint Gp2;

        /// <summary>General-purpose brush param 3. For solid brush: A.</summary>
        public readonly uint Gp3;

        public readonly uint RasterizationMode;
        public readonly uint AntialiasThreshold;

        /// <summary>General-purpose brush param 4.</summary>
        public readonly uint Gp4;

        /// <summary>General-purpose brush param 5.</summary>
        public readonly uint Gp5;

        /// <summary>General-purpose brush param 6.</summary>
        public readonly uint Gp6;

        /// <summary>General-purpose brush param 7.</summary>
        public readonly uint Gp7;

        /// <summary>Index into the color stop / pattern buffer.</summary>
        public readonly uint StopsOffset;

        /// <summary>Number of color stops for gradient commands.</summary>
        public readonly uint StopCount;

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
            PreparedBrushType brushType,
            int brushOriginX,
            int brushOriginY,
            int brushRegionX,
            int brushRegionY,
            int brushRegionWidth,
            int brushRegionHeight,
            uint colorBlendMode,
            uint alphaCompositionMode,
            float blendPercentage,
            Vector4 solidColor,
            uint rasterizationMode,
            float antialiasThreshold,
            uint gp4 = 0,
            uint gp5 = 0,
            uint gp6 = 0,
            uint gp7 = 0,
            uint stopsOffset = 0,
            uint stopCount = 0)
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
            this.BrushType = (uint)brushType;
            this.BrushOriginX = (uint)brushOriginX;
            this.BrushOriginY = (uint)brushOriginY;
            this.BrushRegionX = (uint)brushRegionX;
            this.BrushRegionY = (uint)brushRegionY;
            this.BrushRegionWidth = (uint)brushRegionWidth;
            this.BrushRegionHeight = (uint)brushRegionHeight;
            this.ColorBlendMode = colorBlendMode;
            this.AlphaCompositionMode = alphaCompositionMode;
            this.BlendPercentage = FloatToUInt32Bits(blendPercentage);
            this.Gp0 = FloatToUInt32Bits(solidColor.X);
            this.Gp1 = FloatToUInt32Bits(solidColor.Y);
            this.Gp2 = FloatToUInt32Bits(solidColor.Z);
            this.Gp3 = FloatToUInt32Bits(solidColor.W);
            this.RasterizationMode = rasterizationMode;
            this.AntialiasThreshold = FloatToUInt32Bits(antialiasThreshold);
            this.Gp4 = gp4;
            this.Gp5 = gp5;
            this.Gp6 = gp6;
            this.Gp7 = gp7;
            this.StopsOffset = stopsOffset;
            this.StopCount = stopCount;
        }
    }
}
