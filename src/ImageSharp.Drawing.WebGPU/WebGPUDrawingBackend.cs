// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Buffers;
using System.Collections.Concurrent;
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

#pragma warning disable SA1201 // Elements should appear in the correct order
/// <summary>
/// WebGPU-backed implementation of <see cref="IDrawingBackend"/>.
/// </summary>
/// <remarks>
/// <para>
/// This backend intentionally preserves the <see cref="IDrawingBackend"/> contract used by
/// processors and <c>DrawingCanvas&lt;TPixel&gt;</c>. The public flow is identical to the default
/// backend:
/// </para>
/// <list type="number">
/// <item><description>Prepare path coverage into a reusable handle.</description></item>
/// <item><description>Composite prepared coverage into a target region using brush + graphics options.</description></item>
/// <item><description>Release coverage handle resources deterministically.</description></item>
/// </list>
/// <para>
/// The implementation detail differs: coverage preparation is accelerated through WebGPU render
/// passes while composition uses a dedicated blend shader targeting <c>Rgba8Unorm</c>.
/// </para>
/// <para>
/// Internally, the backend is split into two independent phases:
/// </para>
/// <list type="number">
/// <item>
/// Coverage preparation:
/// path geometry is flattened in local-interest coordinates, converted to edge triangles,
/// then rasterized by a stencil-and-cover render pass into an <c>R8Unorm</c> coverage mask.
/// This avoids per-pixel edge scans in shader code.
/// </item>
/// <item>
/// Coverage composition:
/// a composition shader samples the prepared coverage mask and applies brush/blend rules into
/// an <c>Rgba8Unorm</c> target texture using source-over semantics.
/// </item>
/// </list>
/// <para>
/// Coverage rasterization supports both fill rules:
/// <see cref="IntersectionRule.EvenOdd"/> and <see cref="IntersectionRule.NonZero"/>.
/// The active rule selects the appropriate stencil pipeline at draw time.
/// </para>
/// <para>
/// Composition runs in session mode:
/// the target region is uploaded once, multiple composite operations execute on the same GPU
/// texture, then one readback copies results to the destination buffer.
/// </para>
/// <para>
/// Threading model: all GPU object creation, command encoding, submission, and map/readback are
/// synchronized by <see cref="gpuSync"/>. This keeps native resource lifetime deterministic and
/// prevents command submission races while still allowing concurrent high-level calls.
/// </para>
/// <para>
/// Handle ownership model: prepared coverage is stored in <see cref="preparedCoverage"/> and owned
/// by this backend instance. The caller receives only an opaque <see cref="DrawingCoverageHandle"/>.
/// Releasing the handle always releases the corresponding GPU texture/view (or fallback handle).
/// </para>
/// <para>
/// Sampling model: path geometry is translated to local interest space and adjusted for
/// <see cref="RasterizerSamplingOrigin"/> before rasterization so coverage generation remains
/// consistent with canvas-local coordinate semantics.
/// </para>
/// <para>
/// If a GPU path is unavailable for the current operation (unsupported pixel/brush/blend mode
/// or initialization failure), behavior falls back to <see cref="DefaultDrawingBackend"/> so
/// output remains deterministic and API semantics stay consistent.
/// </para>
/// </remarks>
internal sealed unsafe class WebGPUDrawingBackend : IDrawingBackend, IDisposable
{
    private const uint CompositeVertexCount = 6;
    private const uint CoverageCoverVertexCount = 3;
    private const uint CoverageSampleCount = 4;
    private const int CallbackTimeoutMilliseconds = 10_000;

    private static ReadOnlySpan<byte> CompositeVertexEntryPoint => "vs_main\0"u8;

    private static ReadOnlySpan<byte> CompositeFragmentEntryPoint => "fs_main\0"u8;

    private static ReadOnlySpan<byte> CoverageStencilVertexEntryPoint => "vs_edge\0"u8;

    private static ReadOnlySpan<byte> CoverageStencilFragmentEntryPoint => "fs_stencil\0"u8;

    private static ReadOnlySpan<byte> CoverageCoverVertexEntryPoint => "vs_cover\0"u8;

    private static ReadOnlySpan<byte> CoverageCoverFragmentEntryPoint => "fs_cover\0"u8;

    private readonly object gpuSync = new();
    private readonly ConcurrentDictionary<int, CoverageEntry> preparedCoverage = new();
    private readonly DefaultDrawingBackend fallbackBackend;

    private int nextCoverageHandleId;
    private bool isDisposed;
    private WebGpuRuntime.Lease? runtimeLease;
    private WebGPU? webGpu;
    private Wgpu? wgpuExtension;
    private Instance* instance;
    private Adapter* adapter;
    private Device* device;
    private Queue* queue;
    private BindGroupLayout* compositeBindGroupLayout;
    private PipelineLayout* compositePipelineLayout;
    private RenderPipeline* compositePipeline;
    private PipelineLayout* coveragePipelineLayout;
    private RenderPipeline* coverageStencilEvenOddPipeline;
    private RenderPipeline* coverageStencilNonZeroIncrementPipeline;
    private RenderPipeline* coverageStencilNonZeroDecrementPipeline;
    private RenderPipeline* coverageCoverPipeline;

    private int compositeSessionDepth;
    private bool compositeSessionGpuActive;
    private bool compositeSessionDirty;
    private Buffer2DRegion<Rgba32> compositeSessionTarget;
    private Texture* compositeSessionTargetTexture;
    private TextureView* compositeSessionTargetView;
    private WgpuBuffer* compositeSessionReadbackBuffer;
    private CommandEncoder* compositeSessionCommandEncoder;
    private uint compositeSessionReadbackBytesPerRow;
    private ulong compositeSessionReadbackByteCount;
    private int compositeSessionResourceWidth;
    private int compositeSessionResourceHeight;
    private static readonly bool TraceEnabled = string.Equals(
        Environment.GetEnvironmentVariable("IMAGESHARP_WEBGPU_TRACE"),
        "1",
        StringComparison.Ordinal);

    public WebGPUDrawingBackend() => this.fallbackBackend = DefaultDrawingBackend.Instance;

    private static void Trace(string message)
    {
        if (TraceEnabled)
        {
            Console.Error.WriteLine($"[WebGPU] {message}");
        }
    }

    /// <summary>
    /// Gets the total number of coverage preparation requests.
    /// </summary>
    public int PrepareCoverageCallCount { get; private set; }

    /// <summary>
    /// Gets the number of coverage preparations executed on the GPU.
    /// </summary>
    public int GpuPrepareCoverageCallCount { get; private set; }

    /// <summary>
    /// Gets the number of coverage preparations delegated to the fallback backend.
    /// </summary>
    public int FallbackPrepareCoverageCallCount { get; private set; }

    /// <summary>
    /// Gets the total number of composition requests.
    /// </summary>
    public int CompositeCoverageCallCount { get; private set; }

    /// <summary>
    /// Gets the number of compositions executed on the GPU.
    /// </summary>
    public int GpuCompositeCoverageCallCount { get; private set; }

    /// <summary>
    /// Gets the number of compositions delegated to the fallback backend.
    /// </summary>
    public int FallbackCompositeCoverageCallCount { get; private set; }

    /// <summary>
    /// Gets the number of released coverage handles.
    /// </summary>
    public int ReleaseCoverageCallCount { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the backend completed GPU initialization.
    /// </summary>
    public bool IsGpuReady { get; private set; }

    /// <summary>
    /// Gets a value indicating whether GPU initialization has been attempted.
    /// </summary>
    public bool GpuInitializationAttempted { get; private set; }

    /// <summary>
    /// Gets the last GPU initialization failure reason, if any.
    /// </summary>
    public string? LastGpuInitializationFailure { get; private set; }

    /// <summary>
    /// Gets the number of prepared coverage entries currently cached by handle.
    /// </summary>
    public int LiveCoverageCount => this.preparedCoverage.Count;

    /// <summary>
    /// Begins a composite session for a target region.
    /// </summary>
    /// <remarks>
    /// Nested calls are reference-counted. The first successful call uploads the target
    /// pixels into a GPU texture. The final matching <see cref="EndCompositeSession{TPixel}"/>
    /// flushes GPU results back to the target.
    /// </remarks>
    public void BeginCompositeSession<TPixel>(Configuration configuration, Buffer2DRegion<TPixel> target)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        this.ThrowIfDisposed();
        Guard.NotNull(configuration, nameof(configuration));
        Guard.NotNull(target.Buffer, nameof(target));

        if (this.compositeSessionDepth > 0)
        {
            this.compositeSessionDepth++;
            return;
        }

        this.compositeSessionDepth = 1;
        this.compositeSessionGpuActive = false;
        this.compositeSessionDirty = false;

        if (!CanUseGpuSession<TPixel>() || !this.TryEnsureGpuReady())
        {
            return;
        }

        Buffer2DRegion<Rgba32> rgbaTarget = Unsafe.As<Buffer2DRegion<TPixel>, Buffer2DRegion<Rgba32>>(ref target);

        lock (this.gpuSync)
        {
            if (!this.TryBeginCompositeSessionLocked(rgbaTarget))
            {
                return;
            }

            this.compositeSessionGpuActive = true;
        }
    }

    /// <summary>
    /// Ends a previously started composite session.
    /// </summary>
    /// <remarks>
    /// When this is the outermost session and GPU work has modified the session texture,
    /// the method performs one readback into the destination region, then clears active
    /// session state. Session textures/buffers can be retained and reused by later sessions.
    /// </remarks>
    public void EndCompositeSession<TPixel>(Configuration configuration, Buffer2DRegion<TPixel> target)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        this.ThrowIfDisposed();
        Guard.NotNull(configuration, nameof(configuration));
        Guard.NotNull(target.Buffer, nameof(target));

        if (this.compositeSessionDepth <= 0)
        {
            return;
        }

        this.compositeSessionDepth--;
        if (this.compositeSessionDepth > 0)
        {
            return;
        }

        lock (this.gpuSync)
        {
            Trace($"EndCompositeSession: gpuActive={this.compositeSessionGpuActive} dirty={this.compositeSessionDirty}");
            if (this.compositeSessionGpuActive && this.compositeSessionDirty)
            {
                this.TryFlushCompositeSessionLocked();
            }

            this.ResetCompositeSessionStateLocked();
        }

        this.compositeSessionGpuActive = false;
        this.compositeSessionDirty = false;
    }

    /// <summary>
    /// Fills a path on the specified target region.
    /// </summary>
    /// <remarks>
    /// The method clips interest bounds to the local target region, prepares reusable coverage,
    /// then composites that coverage with the supplied brush.
    /// </remarks>
    public void FillPath<TPixel>(
        Configuration configuration,
        Buffer2DRegion<TPixel> target,
        IPath path,
        Brush brush,
        GraphicsOptions graphicsOptions,
        in RasterizerOptions rasterizerOptions)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        this.ThrowIfDisposed();
        Guard.NotNull(configuration, nameof(configuration));
        Guard.NotNull(target.Buffer, nameof(target));
        Guard.NotNull(path, nameof(path));
        Guard.NotNull(brush, nameof(brush));

        if (!CanUseGpuSession<TPixel>())
        {
            this.fallbackBackend.FillPath(configuration, target, path, brush, graphicsOptions, rasterizerOptions);
            return;
        }

        Rectangle localTargetBounds = new(0, 0, target.Width, target.Height);
        Rectangle clippedInterest = Rectangle.Intersect(localTargetBounds, rasterizerOptions.Interest);
        if (clippedInterest.Equals(Rectangle.Empty))
        {
            return;
        }

        RasterizerOptions clippedOptions = clippedInterest.Equals(rasterizerOptions.Interest)
            ? rasterizerOptions
            : new RasterizerOptions(
                clippedInterest,
                rasterizerOptions.IntersectionRule,
                rasterizerOptions.RasterizationMode,
                rasterizerOptions.SamplingOrigin);

        CoveragePreparationMode preparationMode =
            this.SupportsCoverageComposition<TPixel>(brush, graphicsOptions)
                ? CoveragePreparationMode.Default
                : CoveragePreparationMode.Fallback;

        long prepareStart = 0;
        if (TraceEnabled)
        {
            prepareStart = Stopwatch.GetTimestamp();
        }

        DrawingCoverageHandle coverageHandle = this.PrepareCoverage(
            path,
            clippedOptions,
            configuration.MemoryAllocator,
            preparationMode);

        if (TraceEnabled)
        {
            double prepareMs = Stopwatch.GetElapsedTime(prepareStart).TotalMilliseconds;
            Trace($"FillPath: prepare={prepareMs:F3}ms mode={preparationMode}");
        }

        if (!coverageHandle.IsValid)
        {
            return;
        }

        try
        {
            Buffer2DRegion<TPixel> compositeTarget = target.GetSubRegion(clippedInterest);
            bool openedCompositeSession = false;
            if (preparationMode == CoveragePreparationMode.Default && this.compositeSessionDepth == 0)
            {
                this.BeginCompositeSession(configuration, compositeTarget);
                openedCompositeSession = true;
            }

            Rectangle brushBounds = Rectangle.Ceiling(path.Bounds);

            try
            {
                long compositeStart = 0;
                if (TraceEnabled)
                {
                    compositeStart = Stopwatch.GetTimestamp();
                }

                this.CompositeCoverage(
                    configuration,
                    compositeTarget,
                    coverageHandle,
                    Point.Empty,
                    brush,
                    graphicsOptions,
                    brushBounds);

                if (TraceEnabled)
                {
                    double compositeMs = Stopwatch.GetElapsedTime(compositeStart).TotalMilliseconds;
                    Trace($"FillPath: composite={compositeMs:F3}ms");
                }
            }
            finally
            {
                if (openedCompositeSession)
                {
                    this.EndCompositeSession(configuration, compositeTarget);
                }
            }
        }
        finally
        {
            this.ReleaseCoverage(coverageHandle);
        }
    }

    /// <summary>
    /// Fills a rectangular region on the specified target region.
    /// </summary>
    /// <remarks>
    /// Rect fills are normalized through <see cref="FillPath{TPixel}(Configuration, Buffer2DRegion{TPixel}, IPath, Brush, GraphicsOptions, in RasterizerOptions)"/>
    /// so both APIs share the same coverage and composition paths.
    /// </remarks>
    public void FillRegion<TPixel>(
        Configuration configuration,
        Buffer2DRegion<TPixel> target,
        Brush brush,
        GraphicsOptions graphicsOptions,
        Rectangle region)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        this.ThrowIfDisposed();
        Guard.NotNull(configuration, nameof(configuration));
        Guard.NotNull(target.Buffer, nameof(target));
        Guard.NotNull(brush, nameof(brush));

        if (!CanUseGpuSession<TPixel>())
        {
            this.fallbackBackend.FillRegion(configuration, target, brush, graphicsOptions, region);
            return;
        }

        Rectangle localTargetBounds = new(0, 0, target.Width, target.Height);
        Rectangle clippedRegion = Rectangle.Intersect(localTargetBounds, region);
        if (clippedRegion.Equals(Rectangle.Empty))
        {
            return;
        }

        RasterizationMode rasterizationMode = graphicsOptions.Antialias
            ? RasterizationMode.Antialiased
            : RasterizationMode.Aliased;

        RasterizerOptions rasterizerOptions = new(
            clippedRegion,
            IntersectionRule.NonZero,
            rasterizationMode,
            RasterizerSamplingOrigin.PixelBoundary);

        RectangularPolygon fillShape = new(
            clippedRegion.X,
            clippedRegion.Y,
            clippedRegion.Width,
            clippedRegion.Height);

        this.FillPath(
            configuration,
            target,
            fillShape,
            brush,
            graphicsOptions,
            rasterizerOptions);
    }

    /// <summary>
    /// Determines whether this backend can composite coverage with the given brush/options.
    /// </summary>
    public bool SupportsCoverageComposition<TPixel>(Brush brush, in GraphicsOptions graphicsOptions)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        Guard.NotNull(brush, nameof(brush));

        return CanUseGpuComposite<TPixel>(graphicsOptions)
            && WebGpuBrushData.TryCreate(brush, out _)
            && this.TryEnsureGpuReady();
    }

    /// <summary>
    /// Prepares coverage for a path and returns an opaque reusable handle.
    /// </summary>
    /// <remarks>
    /// GPU preparation flattens path edges into local-interest coordinates, builds a tiled edge index,
    /// and rasterizes the coverage texture. Unsupported scenarios delegate to fallback preparation.
    /// </remarks>
    public DrawingCoverageHandle PrepareCoverage(
        IPath path,
        in RasterizerOptions rasterizerOptions,
        MemoryAllocator allocator,
        CoveragePreparationMode preparationMode)
    {
        this.ThrowIfDisposed();
        Guard.NotNull(path, nameof(path));
        Guard.NotNull(allocator, nameof(allocator));

        this.PrepareCoverageCallCount++;
        Size size = rasterizerOptions.Interest.Size;
        if (size.Width <= 0 || size.Height <= 0)
        {
            return default;
        }

        if (preparationMode == CoveragePreparationMode.Fallback)
        {
            return this.PrepareCoverageFallback(path, rasterizerOptions, allocator);
        }

        if (!this.TryEnsureGpuReady())
        {
            return this.PrepareCoverageFallback(path, rasterizerOptions, allocator);
        }

        if (!TryBuildCoverageTriangles(
            path,
            rasterizerOptions.Interest.Location,
            rasterizerOptions.Interest.Size,
            rasterizerOptions.SamplingOrigin,
            out CoverageTriangleData coverageTriangleData))
        {
            return this.PrepareCoverageFallback(path, rasterizerOptions, allocator);
        }

        Texture* coverageTexture = null;
        TextureView* coverageView = null;
        lock (this.gpuSync)
        {
            if (!this.IsGpuReady ||
                this.webGpu is null ||
                this.device is null ||
                this.queue is null ||
                this.coverageStencilEvenOddPipeline is null ||
                this.coverageStencilNonZeroIncrementPipeline is null ||
                this.coverageStencilNonZeroDecrementPipeline is null ||
                this.coverageCoverPipeline is null ||
                !this.TryRasterizeCoverageTextureLocked(
                    coverageTriangleData,
                    in rasterizerOptions,
                    out coverageTexture,
                    out coverageView))
            {
                return this.PrepareCoverageFallback(path, rasterizerOptions, allocator);
            }
        }

        int handleId = Interlocked.Increment(ref this.nextCoverageHandleId);
        CoverageEntry entry = new(size.Width, size.Height)
        {
            GpuCoverageTexture = coverageTexture,
            GpuCoverageView = coverageView
        };

        if (!this.preparedCoverage.TryAdd(handleId, entry))
        {
            lock (this.gpuSync)
            {
                this.ReleaseCoverageTextureLocked(entry);
            }

            entry.Dispose();
            throw new InvalidOperationException("Failed to cache prepared coverage.");
        }

        this.GpuPrepareCoverageCallCount++;
        return new DrawingCoverageHandle(handleId);
    }

    private DrawingCoverageHandle PrepareCoverageFallback(
        IPath path,
        in RasterizerOptions rasterizerOptions,
        MemoryAllocator allocator)
    {
        this.FallbackPrepareCoverageCallCount++;
        DrawingCoverageHandle fallbackHandle = this.fallbackBackend.PrepareCoverage(
            path,
            rasterizerOptions,
            allocator,
            CoveragePreparationMode.Fallback);
        if (!fallbackHandle.IsValid)
        {
            return default;
        }

        Size size = rasterizerOptions.Interest.Size;
        int handleId = Interlocked.Increment(ref this.nextCoverageHandleId);
        CoverageEntry entry = new(size.Width, size.Height)
        {
            FallbackCoverageHandle = fallbackHandle
        };

        if (!this.preparedCoverage.TryAdd(handleId, entry))
        {
            this.fallbackBackend.ReleaseCoverage(fallbackHandle);
            throw new InvalidOperationException("Failed to cache prepared fallback coverage.");
        }

        return new DrawingCoverageHandle(handleId);
    }

    /// <summary>
    /// Composes prepared coverage into a target region using the provided brush.
    /// </summary>
    /// <remarks>
    /// Handles prepared in fallback mode are always composed by the fallback backend.
    /// Handles prepared in accelerated mode must be composed in accelerated mode.
    /// Mixed-mode fallback is deliberately disabled to keep behavior explicit.
    /// </remarks>
    public void CompositeCoverage<TPixel>(
        Configuration configuration,
        Buffer2DRegion<TPixel> target,
        DrawingCoverageHandle coverageHandle,
        Point sourceOffset,
        Brush brush,
        in GraphicsOptions graphicsOptions,
        Rectangle brushBounds)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        this.ThrowIfDisposed();
        Guard.NotNull(configuration, nameof(configuration));
        Guard.NotNull(target.Buffer, nameof(target));
        Guard.NotNull(brush, nameof(brush));
        this.CompositeCoverageCallCount++;

        if (!coverageHandle.IsValid)
        {
            return;
        }

        if (!this.preparedCoverage.TryGetValue(coverageHandle.Value, out CoverageEntry? entry))
        {
            throw new InvalidOperationException($"Prepared coverage handle '{coverageHandle.Value}' is not valid.");
        }

        if (entry.IsFallback)
        {
            this.FallbackCompositeCoverageCallCount++;
            this.fallbackBackend.CompositeCoverage(
                configuration,
                target,
                entry.FallbackCoverageHandle,
                sourceOffset,
                brush,
                graphicsOptions,
                brushBounds);
            return;
        }

        if (!CanUseGpuComposite<TPixel>(graphicsOptions) || !this.TryEnsureGpuReady())
        {
            throw new InvalidOperationException(
                "Mixed-mode coverage composition is disabled. Coverage was prepared for accelerated composition, but the current composite settings are not GPU-supported.");
        }

        if (!WebGpuBrushData.TryCreate(brush, out WebGpuBrushData brushData))
        {
            throw new InvalidOperationException(
                "Mixed-mode coverage composition is disabled. Coverage was prepared for accelerated composition, but the current composite settings are not GPU-supported.");
        }

        if (!this.compositeSessionGpuActive || this.compositeSessionDepth <= 0)
        {
            throw new InvalidOperationException(
                "Accelerated coverage composition requires an active composite session.");
        }

        Buffer2DRegion<Rgba32> rgbaTarget = Unsafe.As<Buffer2DRegion<TPixel>, Buffer2DRegion<Rgba32>>(ref target);
        if (!this.TryCompositeCoverageGpu(
            rgbaTarget,
            coverageHandle,
            sourceOffset,
            brushData,
            graphicsOptions.BlendPercentage))
        {
            throw new InvalidOperationException(
                "Accelerated coverage composition failed for a handle prepared for accelerated mode.");
        }

        this.GpuCompositeCoverageCallCount++;
    }

    /// <summary>
    /// Releases a previously prepared coverage handle.
    /// </summary>
    public void ReleaseCoverage(DrawingCoverageHandle coverageHandle)
    {
        this.ReleaseCoverageCallCount++;
        if (!coverageHandle.IsValid)
        {
            return;
        }

        Trace($"ReleaseCoverage: handle={coverageHandle.Value}");
        if (this.preparedCoverage.TryRemove(coverageHandle.Value, out CoverageEntry? entry))
        {
            if (entry.IsFallback)
            {
                this.fallbackBackend.ReleaseCoverage(entry.FallbackCoverageHandle);
            }

            lock (this.gpuSync)
            {
                this.ReleaseCoverageTextureLocked(entry);
            }

            entry.Dispose();
        }
    }

    /// <summary>
    /// Releases all cached coverage and GPU resources owned by this backend instance.
    /// </summary>
    public void Dispose()
    {
        if (this.isDisposed)
        {
            return;
        }

        Trace("Dispose: begin");
        lock (this.gpuSync)
        {
            if (this.compositeSessionGpuActive && this.compositeSessionDirty)
            {
                this.TryFlushCompositeSessionLocked();
            }

            this.ResetCompositeSessionStateLocked();
            this.ReleaseCompositeSessionResourcesLocked();

            foreach (KeyValuePair<int, CoverageEntry> kv in this.preparedCoverage)
            {
                if (kv.Value.IsFallback)
                {
                    this.fallbackBackend.ReleaseCoverage(kv.Value.FallbackCoverageHandle);
                }

                this.ReleaseCoverageTextureLocked(kv.Value);
                kv.Value.Dispose();
            }

            this.preparedCoverage.Clear();
            this.ReleaseGpuResourcesLocked();
        }

        this.isDisposed = true;
        Trace("Dispose: end");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool CanUseGpuComposite<TPixel>(in GraphicsOptions graphicsOptions)
        where TPixel : unmanaged, IPixel<TPixel>
        => typeof(TPixel) == typeof(Rgba32)
        && graphicsOptions.AlphaCompositionMode == PixelAlphaCompositionMode.SrcOver
        && graphicsOptions.ColorBlendingMode == PixelColorBlendingMode.Normal
        && graphicsOptions.BlendPercentage > 0F;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool CanUseGpuSession<TPixel>()
        where TPixel : unmanaged, IPixel<TPixel>
        => typeof(TPixel) == typeof(Rgba32);

    /// <summary>
    /// Ensures this instance has a ready-to-use GPU device/pipeline set.
    /// </summary>
    /// <remarks>
    /// Initialization is single-attempt per backend instance; subsequent calls are
    /// cheap and return cached state.
    /// </remarks>
    private bool TryEnsureGpuReady()
    {
        if (this.IsGpuReady)
        {
            return true;
        }

        lock (this.gpuSync)
        {
            if (this.IsGpuReady)
            {
                return true;
            }

            if (this.GpuInitializationAttempted)
            {
                return false;
            }

            this.GpuInitializationAttempted = true;
            this.LastGpuInitializationFailure = null;
            this.IsGpuReady = this.TryInitializeGpuLocked();
            return this.IsGpuReady;
        }
    }

    /// <summary>
    /// Performs one-time GPU initialization while <see cref="gpuSync"/> is held.
    /// </summary>
    private bool TryInitializeGpuLocked()
    {
        Trace("TryInitializeGpuLocked: begin");
        try
        {
            this.runtimeLease = WebGpuRuntime.Acquire();
            this.webGpu = this.runtimeLease.Api;
            this.wgpuExtension = this.runtimeLease.WgpuExtension;
            Trace($"TryInitializeGpuLocked: extension={(this.wgpuExtension is null ? "none" : "wgpu.h")}");
            this.instance = this.webGpu.CreateInstance((InstanceDescriptor*)null);
            if (this.instance is null)
            {
                this.LastGpuInitializationFailure = "WebGPU.CreateInstance returned null.";
                Trace("TryInitializeGpuLocked: CreateInstance returned null");
                return false;
            }

            Trace("TryInitializeGpuLocked: created instance");
            if (!this.TryRequestAdapterLocked(out this.adapter) || this.adapter is null)
            {
                this.LastGpuInitializationFailure ??= "Failed to request WebGPU adapter.";
                Trace($"TryInitializeGpuLocked: request adapter failed ({this.LastGpuInitializationFailure})");
                return false;
            }

            Trace("TryInitializeGpuLocked: adapter acquired");
            if (!this.TryRequestDeviceLocked(out this.device) || this.device is null)
            {
                this.LastGpuInitializationFailure ??= "Failed to request WebGPU device.";
                Trace($"TryInitializeGpuLocked: request device failed ({this.LastGpuInitializationFailure})");
                return false;
            }

            this.queue = this.webGpu.DeviceGetQueue(this.device);
            if (this.queue is null)
            {
                this.LastGpuInitializationFailure = "WebGPU.DeviceGetQueue returned null.";
                Trace("TryInitializeGpuLocked: DeviceGetQueue returned null");
                return false;
            }

            Trace("TryInitializeGpuLocked: queue acquired");
            if (!this.TryCreateCompositePipelineLocked())
            {
                this.LastGpuInitializationFailure = "Failed to create WebGPU composite pipeline.";
                Trace("TryInitializeGpuLocked: composite pipeline creation failed");
                return false;
            }

            Trace("TryInitializeGpuLocked: composite pipeline ready");
            if (!this.TryCreateCoveragePipelineLocked())
            {
                this.LastGpuInitializationFailure = "Failed to create WebGPU coverage pipeline.";
                Trace("TryInitializeGpuLocked: coverage pipeline creation failed");
                return false;
            }

            Trace("TryInitializeGpuLocked: coverage pipeline ready");
            return true;
        }
        catch (Exception ex)
        {
            this.LastGpuInitializationFailure = $"WebGPU initialization threw: {ex.Message}";
            Trace($"TryInitializeGpuLocked: exception {ex}");
            return false;
        }
        finally
        {
            if (!this.IsGpuReady &&
                (this.compositePipeline is null ||
                 this.compositePipelineLayout is null ||
                 this.compositeBindGroupLayout is null ||
                 this.coverageStencilEvenOddPipeline is null ||
                 this.coverageStencilNonZeroIncrementPipeline is null ||
                 this.coverageStencilNonZeroDecrementPipeline is null ||
                 this.coverageCoverPipeline is null ||
                 this.coveragePipelineLayout is null ||
                 this.device is null ||
                 this.queue is null))
            {
                this.LastGpuInitializationFailure ??= "WebGPU initialization left required resources unavailable.";
                this.ReleaseGpuResourcesLocked();
            }

            Trace($"TryInitializeGpuLocked: end ready={this.IsGpuReady} error={this.LastGpuInitializationFailure ?? "<none>"}");
        }
    }

    private bool TryRequestAdapterLocked(out Adapter* resultAdapter)
    {
        resultAdapter = null;
        if (this.webGpu is null || this.instance is null)
        {
            return false;
        }

        RequestAdapterStatus callbackStatus = RequestAdapterStatus.Unknown;
        Adapter* callbackAdapter = null;
        using ManualResetEventSlim callbackReady = new(false);
        void Callback(RequestAdapterStatus status, Adapter* adapterPtr, byte* messagePtr, void* userDataPtr)
        {
            callbackStatus = status;
            callbackAdapter = adapterPtr;
            _ = messagePtr;
            _ = userDataPtr;
            callbackReady.Set();
        }

        using PfnRequestAdapterCallback callbackPtr = PfnRequestAdapterCallback.From(Callback);
        RequestAdapterOptions options = new()
        {
            PowerPreference = PowerPreference.HighPerformance
        };

        this.webGpu.InstanceRequestAdapter(this.instance, in options, callbackPtr, null);
        if (!this.WaitForSignalLocked(callbackReady))
        {
            this.LastGpuInitializationFailure = "Timed out while waiting for WebGPU adapter request callback.";
            Trace("TryRequestAdapterLocked: timeout waiting for callback");
            return false;
        }

        resultAdapter = callbackAdapter;
        if (callbackStatus != RequestAdapterStatus.Success || callbackAdapter is null)
        {
            this.LastGpuInitializationFailure = $"WebGPU adapter request failed with status '{callbackStatus}'.";
            Trace($"TryRequestAdapterLocked: callback status={callbackStatus} adapter={(nint)callbackAdapter:X}");
            return false;
        }

        return true;
    }

    private bool TryRequestDeviceLocked(out Device* resultDevice)
    {
        resultDevice = null;
        if (this.webGpu is null || this.adapter is null)
        {
            return false;
        }

        RequestDeviceStatus callbackStatus = RequestDeviceStatus.Unknown;
        Device* callbackDevice = null;
        using ManualResetEventSlim callbackReady = new(false);
        void Callback(RequestDeviceStatus status, Device* devicePtr, byte* messagePtr, void* userDataPtr)
        {
            callbackStatus = status;
            callbackDevice = devicePtr;
            _ = messagePtr;
            _ = userDataPtr;
            callbackReady.Set();
        }

        using PfnRequestDeviceCallback callbackPtr = PfnRequestDeviceCallback.From(Callback);
        DeviceDescriptor descriptor = default;
        this.webGpu.AdapterRequestDevice(this.adapter, in descriptor, callbackPtr, null);

        if (!this.WaitForSignalLocked(callbackReady))
        {
            this.LastGpuInitializationFailure = "Timed out while waiting for WebGPU device request callback.";
            Trace("TryRequestDeviceLocked: timeout waiting for callback");
            return false;
        }

        resultDevice = callbackDevice;
        if (callbackStatus != RequestDeviceStatus.Success || callbackDevice is null)
        {
            this.LastGpuInitializationFailure = $"WebGPU device request failed with status '{callbackStatus}'.";
            Trace($"TryRequestDeviceLocked: callback status={callbackStatus} device={(nint)callbackDevice:X}");
            return false;
        }

        return true;
    }

    /// <summary>
    /// Creates the render pipeline used for coverage composition.
    /// </summary>
    private bool TryCreateCompositePipelineLocked()
    {
        if (this.webGpu is null || this.device is null)
        {
            return false;
        }

        BindGroupLayoutEntry* layoutEntries = stackalloc BindGroupLayoutEntry[2];
        layoutEntries[0] = new BindGroupLayoutEntry
        {
            Binding = 0,
            Visibility = ShaderStage.Fragment,
            Texture = new TextureBindingLayout
            {
                SampleType = TextureSampleType.Float,
                ViewDimension = TextureViewDimension.Dimension2D,
                Multisampled = false
            }
        };
        layoutEntries[1] = new BindGroupLayoutEntry
        {
            Binding = 1,
            Visibility = ShaderStage.Vertex | ShaderStage.Fragment,
            Buffer = new BufferBindingLayout
            {
                Type = BufferBindingType.Uniform,
                MinBindingSize = (ulong)Unsafe.SizeOf<CompositeParams>()
            }
        };

        BindGroupLayoutDescriptor layoutDescriptor = new()
        {
            EntryCount = 2,
            Entries = layoutEntries
        };

        this.compositeBindGroupLayout = this.webGpu.DeviceCreateBindGroupLayout(this.device, in layoutDescriptor);
        if (this.compositeBindGroupLayout is null)
        {
            return false;
        }

        BindGroupLayout** bindGroupLayouts = stackalloc BindGroupLayout*[1];
        bindGroupLayouts[0] = this.compositeBindGroupLayout;
        PipelineLayoutDescriptor pipelineLayoutDescriptor = new()
        {
            BindGroupLayoutCount = 1,
            BindGroupLayouts = bindGroupLayouts
        };

        this.compositePipelineLayout = this.webGpu.DeviceCreatePipelineLayout(this.device, in pipelineLayoutDescriptor);
        if (this.compositePipelineLayout is null)
        {
            return false;
        }

        ShaderModule* shaderModule = null;
        try
        {
            ReadOnlySpan<byte> shaderCode = CompositeCoverageShader.Code;
            fixed (byte* shaderCodePtr = shaderCode)
            {
                ShaderModuleWGSLDescriptor wgslDescriptor = new()
                {
                    Chain = new ChainedStruct
                    {
                        SType = SType.ShaderModuleWgslDescriptor
                    },
                    Code = shaderCodePtr
                };

                ShaderModuleDescriptor shaderDescriptor = new()
                {
                    NextInChain = (ChainedStruct*)&wgslDescriptor
                };

                shaderModule = this.webGpu.DeviceCreateShaderModule(this.device, in shaderDescriptor);
            }

            if (shaderModule is null)
            {
                return false;
            }

            ReadOnlySpan<byte> vertexEntryPoint = CompositeVertexEntryPoint;
            ReadOnlySpan<byte> fragmentEntryPoint = CompositeFragmentEntryPoint;
            fixed (byte* vertexEntryPointPtr = vertexEntryPoint)
            {
                fixed (byte* fragmentEntryPointPtr = fragmentEntryPoint)
                {
                    VertexState vertexState = new()
                    {
                        Module = shaderModule,
                        EntryPoint = vertexEntryPointPtr,
                        BufferCount = 0,
                        Buffers = null
                    };

                    BlendState blendState = new()
                    {
                        Color = new BlendComponent
                        {
                            Operation = BlendOperation.Add,
                            SrcFactor = BlendFactor.One,
                            DstFactor = BlendFactor.OneMinusSrcAlpha
                        },
                        Alpha = new BlendComponent
                        {
                            Operation = BlendOperation.Add,
                            SrcFactor = BlendFactor.One,
                            DstFactor = BlendFactor.OneMinusSrcAlpha
                        }
                    };

                    ColorTargetState* colorTargets = stackalloc ColorTargetState[1];
                    colorTargets[0] = new ColorTargetState
                    {
                        Format = TextureFormat.Rgba8Unorm,
                        Blend = &blendState,
                        WriteMask = ColorWriteMask.All
                    };

                    FragmentState fragmentState = new()
                    {
                        Module = shaderModule,
                        EntryPoint = fragmentEntryPointPtr,
                        TargetCount = 1,
                        Targets = colorTargets
                    };

                    RenderPipelineDescriptor pipelineDescriptor = new()
                    {
                        Layout = this.compositePipelineLayout,
                        Vertex = vertexState,
                        Primitive = new PrimitiveState
                        {
                            Topology = PrimitiveTopology.TriangleList,
                            StripIndexFormat = IndexFormat.Undefined,
                            FrontFace = FrontFace.Ccw,
                            CullMode = CullMode.None
                        },
                        DepthStencil = null,
                        Multisample = new MultisampleState
                        {
                            Count = 1,
                            Mask = uint.MaxValue,
                            AlphaToCoverageEnabled = false
                        },
                        Fragment = &fragmentState
                    };

                    this.compositePipeline = this.webGpu.DeviceCreateRenderPipeline(this.device, in pipelineDescriptor);
                }
            }

            return this.compositePipeline is not null;
        }
        finally
        {
            if (shaderModule is not null)
            {
                this.webGpu.ShaderModuleRelease(shaderModule);
            }
        }
    }

    /// <summary>
    /// Creates the render pipeline used for coverage rasterization.
    /// </summary>
    private bool TryCreateCoveragePipelineLocked()
    {
        if (this.webGpu is null || this.device is null)
        {
            return false;
        }

        PipelineLayoutDescriptor pipelineLayoutDescriptor = new()
        {
            BindGroupLayoutCount = 0,
            BindGroupLayouts = null
        };

        this.coveragePipelineLayout = this.webGpu.DeviceCreatePipelineLayout(this.device, in pipelineLayoutDescriptor);
        if (this.coveragePipelineLayout is null)
        {
            return false;
        }

        ShaderModule* shaderModule = null;
        try
        {
            ReadOnlySpan<byte> shaderCode = CoverageRasterizationShader.Code;
            fixed (byte* shaderCodePtr = shaderCode)
            {
                ShaderModuleWGSLDescriptor wgslDescriptor = new()
                {
                    Chain = new ChainedStruct
                    {
                        SType = SType.ShaderModuleWgslDescriptor
                    },
                    Code = shaderCodePtr
                };

                ShaderModuleDescriptor shaderDescriptor = new()
                {
                    NextInChain = (ChainedStruct*)&wgslDescriptor
                };

                shaderModule = this.webGpu.DeviceCreateShaderModule(this.device, in shaderDescriptor);
            }

            if (shaderModule is null)
            {
                return false;
            }

            ReadOnlySpan<byte> stencilVertexEntryPoint = CoverageStencilVertexEntryPoint;
            ReadOnlySpan<byte> stencilFragmentEntryPoint = CoverageStencilFragmentEntryPoint;
            ReadOnlySpan<byte> coverVertexEntryPoint = CoverageCoverVertexEntryPoint;
            ReadOnlySpan<byte> coverFragmentEntryPoint = CoverageCoverFragmentEntryPoint;
            fixed (byte* stencilVertexEntryPointPtr = stencilVertexEntryPoint)
            {
                fixed (byte* stencilFragmentEntryPointPtr = stencilFragmentEntryPoint)
                {
                    VertexAttribute* stencilVertexAttributes = stackalloc VertexAttribute[1];
                    stencilVertexAttributes[0] = new VertexAttribute
                    {
                        Format = VertexFormat.Float32x2,
                        Offset = 0,
                        ShaderLocation = 0
                    };

                    VertexBufferLayout* stencilVertexBuffers = stackalloc VertexBufferLayout[1];
                    stencilVertexBuffers[0] = new VertexBufferLayout
                    {
                        ArrayStride = (ulong)Unsafe.SizeOf<StencilVertex>(),
                        StepMode = VertexStepMode.Vertex,
                        AttributeCount = 1,
                        Attributes = stencilVertexAttributes
                    };

                    VertexState stencilVertexState = new()
                    {
                        Module = shaderModule,
                        EntryPoint = stencilVertexEntryPointPtr,
                        BufferCount = 1,
                        Buffers = stencilVertexBuffers
                    };

                    ColorTargetState* stencilColorTargets = stackalloc ColorTargetState[1];
                    stencilColorTargets[0] = new ColorTargetState
                    {
                        Format = TextureFormat.R8Unorm,
                        Blend = null,
                        WriteMask = ColorWriteMask.None
                    };

                    FragmentState stencilFragmentState = new()
                    {
                        Module = shaderModule,
                        EntryPoint = stencilFragmentEntryPointPtr,
                        TargetCount = 1,
                        Targets = stencilColorTargets
                    };

                    PrimitiveState primitiveState = new()
                    {
                        Topology = PrimitiveTopology.TriangleList,
                        StripIndexFormat = IndexFormat.Undefined,
                        FrontFace = FrontFace.Ccw,
                        CullMode = CullMode.None
                    };

                    MultisampleState multisampleState = new()
                    {
                        Count = CoverageSampleCount,
                        Mask = uint.MaxValue,
                        AlphaToCoverageEnabled = false
                    };

                    StencilFaceState evenOddStencilFace = new()
                    {
                        Compare = CompareFunction.Always,
                        FailOp = StencilOperation.Keep,
                        DepthFailOp = StencilOperation.Keep,
                        PassOp = StencilOperation.Invert
                    };

                    DepthStencilState evenOddDepthStencilState = new()
                    {
                        Format = TextureFormat.Depth24PlusStencil8,
                        DepthWriteEnabled = false,
                        DepthCompare = CompareFunction.Always,
                        StencilFront = evenOddStencilFace,
                        StencilBack = evenOddStencilFace,
                        StencilReadMask = uint.MaxValue,
                        StencilWriteMask = uint.MaxValue,
                        DepthBias = 0,
                        DepthBiasSlopeScale = 0F,
                        DepthBiasClamp = 0F
                    };

                    RenderPipelineDescriptor evenOddPipelineDescriptor = new()
                    {
                        Layout = this.coveragePipelineLayout,
                        Vertex = stencilVertexState,
                        Primitive = primitiveState,
                        DepthStencil = &evenOddDepthStencilState,
                        Multisample = multisampleState,
                        Fragment = &stencilFragmentState
                    };

                    this.coverageStencilEvenOddPipeline = this.webGpu.DeviceCreateRenderPipeline(this.device, in evenOddPipelineDescriptor);
                    if (this.coverageStencilEvenOddPipeline is null)
                    {
                        return false;
                    }

                    StencilFaceState incrementStencilFace = new()
                    {
                        Compare = CompareFunction.Always,
                        FailOp = StencilOperation.Keep,
                        DepthFailOp = StencilOperation.Keep,
                        PassOp = StencilOperation.IncrementWrap
                    };

                    DepthStencilState incrementDepthStencilState = new()
                    {
                        Format = TextureFormat.Depth24PlusStencil8,
                        DepthWriteEnabled = false,
                        DepthCompare = CompareFunction.Always,
                        StencilFront = incrementStencilFace,
                        StencilBack = incrementStencilFace,
                        StencilReadMask = uint.MaxValue,
                        StencilWriteMask = uint.MaxValue,
                        DepthBias = 0,
                        DepthBiasSlopeScale = 0F,
                        DepthBiasClamp = 0F
                    };

                    PrimitiveState incrementPrimitiveState = primitiveState;
                    incrementPrimitiveState.CullMode = CullMode.Back;

                    RenderPipelineDescriptor incrementPipelineDescriptor = new()
                    {
                        Layout = this.coveragePipelineLayout,
                        Vertex = stencilVertexState,
                        Primitive = incrementPrimitiveState,
                        DepthStencil = &incrementDepthStencilState,
                        Multisample = multisampleState,
                        Fragment = &stencilFragmentState
                    };

                    this.coverageStencilNonZeroIncrementPipeline = this.webGpu.DeviceCreateRenderPipeline(this.device, in incrementPipelineDescriptor);
                    if (this.coverageStencilNonZeroIncrementPipeline is null)
                    {
                        return false;
                    }

                    StencilFaceState decrementStencilFace = new()
                    {
                        Compare = CompareFunction.Always,
                        FailOp = StencilOperation.Keep,
                        DepthFailOp = StencilOperation.Keep,
                        PassOp = StencilOperation.DecrementWrap
                    };

                    DepthStencilState decrementDepthStencilState = new()
                    {
                        Format = TextureFormat.Depth24PlusStencil8,
                        DepthWriteEnabled = false,
                        DepthCompare = CompareFunction.Always,
                        StencilFront = decrementStencilFace,
                        StencilBack = decrementStencilFace,
                        StencilReadMask = uint.MaxValue,
                        StencilWriteMask = uint.MaxValue,
                        DepthBias = 0,
                        DepthBiasSlopeScale = 0F,
                        DepthBiasClamp = 0F
                    };

                    PrimitiveState decrementPrimitiveState = primitiveState;
                    decrementPrimitiveState.CullMode = CullMode.Front;

                    RenderPipelineDescriptor decrementPipelineDescriptor = new()
                    {
                        Layout = this.coveragePipelineLayout,
                        Vertex = stencilVertexState,
                        Primitive = decrementPrimitiveState,
                        DepthStencil = &decrementDepthStencilState,
                        Multisample = multisampleState,
                        Fragment = &stencilFragmentState
                    };

                    this.coverageStencilNonZeroDecrementPipeline = this.webGpu.DeviceCreateRenderPipeline(this.device, in decrementPipelineDescriptor);
                    if (this.coverageStencilNonZeroDecrementPipeline is null)
                    {
                        return false;
                    }
                }
            }

            fixed (byte* coverVertexEntryPointPtr = coverVertexEntryPoint)
            {
                fixed (byte* coverFragmentEntryPointPtr = coverFragmentEntryPoint)
                {
                    VertexState coverVertexState = new()
                    {
                        Module = shaderModule,
                        EntryPoint = coverVertexEntryPointPtr,
                        BufferCount = 0,
                        Buffers = null
                    };

                    ColorTargetState* coverColorTargets = stackalloc ColorTargetState[1];
                    coverColorTargets[0] = new ColorTargetState
                    {
                        Format = TextureFormat.R8Unorm,
                        Blend = null,
                        WriteMask = ColorWriteMask.Red
                    };

                    FragmentState coverFragmentState = new()
                    {
                        Module = shaderModule,
                        EntryPoint = coverFragmentEntryPointPtr,
                        TargetCount = 1,
                        Targets = coverColorTargets
                    };

                    StencilFaceState coverStencilFace = new()
                    {
                        Compare = CompareFunction.NotEqual,
                        FailOp = StencilOperation.Keep,
                        DepthFailOp = StencilOperation.Keep,
                        PassOp = StencilOperation.Keep
                    };

                    DepthStencilState coverDepthStencilState = new()
                    {
                        Format = TextureFormat.Depth24PlusStencil8,
                        DepthWriteEnabled = false,
                        DepthCompare = CompareFunction.Always,
                        StencilFront = coverStencilFace,
                        StencilBack = coverStencilFace,
                        StencilReadMask = uint.MaxValue,
                        StencilWriteMask = 0,
                        DepthBias = 0,
                        DepthBiasSlopeScale = 0F,
                        DepthBiasClamp = 0F
                    };

                    RenderPipelineDescriptor coverPipelineDescriptor = new()
                    {
                        Layout = this.coveragePipelineLayout,
                        Vertex = coverVertexState,
                        Primitive = new PrimitiveState
                        {
                            Topology = PrimitiveTopology.TriangleList,
                            StripIndexFormat = IndexFormat.Undefined,
                            FrontFace = FrontFace.Ccw,
                            CullMode = CullMode.None
                        },
                        DepthStencil = &coverDepthStencilState,
                        Multisample = new MultisampleState
                        {
                            Count = CoverageSampleCount,
                            Mask = uint.MaxValue,
                            AlphaToCoverageEnabled = false
                        },
                        Fragment = &coverFragmentState
                    };

                    this.coverageCoverPipeline = this.webGpu.DeviceCreateRenderPipeline(this.device, in coverPipelineDescriptor);
                }
            }

            return this.coverageCoverPipeline is not null;
        }
        finally
        {
            if (shaderModule is not null)
            {
                this.webGpu.ShaderModuleRelease(shaderModule);
            }
        }
    }

    /// <summary>
    /// Rasterizes edge triangles through a stencil-and-cover pass into an <c>R8Unorm</c> texture.
    /// </summary>
    private bool TryRasterizeCoverageTextureLocked(
        in CoverageTriangleData coverageTriangleData,
        in RasterizerOptions rasterizerOptions,
        out Texture* coverageTexture,
        out TextureView* coverageView)
    {
        Trace($"TryRasterizeCoverageTextureLocked: begin triangles={coverageTriangleData.Vertices.Length / 3} size={rasterizerOptions.Interest.Width}x{rasterizerOptions.Interest.Height}");
        coverageTexture = null;
        coverageView = null;

        if (this.webGpu is null ||
            this.device is null ||
            this.queue is null ||
            this.coverageStencilEvenOddPipeline is null ||
            this.coverageStencilNonZeroIncrementPipeline is null ||
            this.coverageStencilNonZeroDecrementPipeline is null ||
            this.coverageCoverPipeline is null ||
            coverageTriangleData.Vertices.Length == 0 ||
            rasterizerOptions.Interest.Width <= 0 ||
            rasterizerOptions.Interest.Height <= 0)
        {
            return false;
        }

        Texture* createdCoverageTexture = null;
        TextureView* createdCoverageView = null;
        Texture* multisampleCoverageTexture = null;
        TextureView* multisampleCoverageView = null;
        Texture* stencilTexture = null;
        TextureView* stencilView = null;
        WgpuBuffer* vertexBuffer = null;
        CommandEncoder* commandEncoder = null;
        RenderPassEncoder* passEncoder = null;
        CommandBuffer* commandBuffer = null;
        bool success = false;
        try
        {
            TextureDescriptor coverageTextureDescriptor = new()
            {
                Usage = TextureUsage.RenderAttachment | TextureUsage.TextureBinding | TextureUsage.CopySrc,
                Dimension = TextureDimension.Dimension2D,
                Size = new Extent3D((uint)rasterizerOptions.Interest.Width, (uint)rasterizerOptions.Interest.Height, 1),
                Format = TextureFormat.R8Unorm,
                MipLevelCount = 1,
                SampleCount = 1
            };

            createdCoverageTexture = this.webGpu.DeviceCreateTexture(this.device, in coverageTextureDescriptor);
            if (createdCoverageTexture is null)
            {
                return false;
            }

            TextureViewDescriptor coverageViewDescriptor = new()
            {
                Format = TextureFormat.R8Unorm,
                Dimension = TextureViewDimension.Dimension2D,
                BaseMipLevel = 0,
                MipLevelCount = 1,
                BaseArrayLayer = 0,
                ArrayLayerCount = 1,
                Aspect = TextureAspect.All
            };

            createdCoverageView = this.webGpu.TextureCreateView(createdCoverageTexture, in coverageViewDescriptor);
            if (createdCoverageView is null)
            {
                return false;
            }

            TextureDescriptor multisampleCoverageTextureDescriptor = new()
            {
                Usage = TextureUsage.RenderAttachment,
                Dimension = TextureDimension.Dimension2D,
                Size = new Extent3D((uint)rasterizerOptions.Interest.Width, (uint)rasterizerOptions.Interest.Height, 1),
                Format = TextureFormat.R8Unorm,
                MipLevelCount = 1,
                SampleCount = CoverageSampleCount
            };

            multisampleCoverageTexture = this.webGpu.DeviceCreateTexture(this.device, in multisampleCoverageTextureDescriptor);
            if (multisampleCoverageTexture is null)
            {
                return false;
            }

            multisampleCoverageView = this.webGpu.TextureCreateView(multisampleCoverageTexture, in coverageViewDescriptor);
            if (multisampleCoverageView is null)
            {
                return false;
            }

            TextureDescriptor stencilTextureDescriptor = new()
            {
                Usage = TextureUsage.RenderAttachment,
                Dimension = TextureDimension.Dimension2D,
                Size = new Extent3D((uint)rasterizerOptions.Interest.Width, (uint)rasterizerOptions.Interest.Height, 1),
                Format = TextureFormat.Depth24PlusStencil8,
                MipLevelCount = 1,
                SampleCount = CoverageSampleCount
            };

            stencilTexture = this.webGpu.DeviceCreateTexture(this.device, in stencilTextureDescriptor);
            if (stencilTexture is null)
            {
                return false;
            }

            TextureViewDescriptor stencilViewDescriptor = new()
            {
                Format = TextureFormat.Depth24PlusStencil8,
                Dimension = TextureViewDimension.Dimension2D,
                BaseMipLevel = 0,
                MipLevelCount = 1,
                BaseArrayLayer = 0,
                ArrayLayerCount = 1,
                Aspect = TextureAspect.All
            };

            stencilView = this.webGpu.TextureCreateView(stencilTexture, in stencilViewDescriptor);
            if (stencilView is null)
            {
                return false;
            }

            ulong vertexByteCount = checked((ulong)coverageTriangleData.Vertices.Length * (ulong)Unsafe.SizeOf<StencilVertex>());
            BufferDescriptor vertexBufferDescriptor = new()
            {
                Usage = BufferUsage.Vertex | BufferUsage.CopyDst,
                Size = vertexByteCount
            };
            vertexBuffer = this.webGpu.DeviceCreateBuffer(this.device, in vertexBufferDescriptor);
            if (vertexBuffer is null)
            {
                return false;
            }

            fixed (StencilVertex* verticesPtr = coverageTriangleData.Vertices)
            {
                this.webGpu.QueueWriteBuffer(this.queue, vertexBuffer, 0, verticesPtr, (nuint)vertexByteCount);
            }

            CommandEncoderDescriptor commandEncoderDescriptor = default;
            commandEncoder = this.webGpu.DeviceCreateCommandEncoder(this.device, in commandEncoderDescriptor);
            if (commandEncoder is null)
            {
                return false;
            }

            RenderPassColorAttachment colorAttachment = new()
            {
                View = multisampleCoverageView,
                ResolveTarget = createdCoverageView,
                LoadOp = LoadOp.Clear,
                StoreOp = StoreOp.Discard,
                ClearValue = default
            };

            RenderPassDepthStencilAttachment depthStencilAttachment = new()
            {
                View = stencilView,
                DepthLoadOp = LoadOp.Clear,
                DepthStoreOp = StoreOp.Discard,
                DepthClearValue = 1F,
                DepthReadOnly = false,
                StencilLoadOp = LoadOp.Clear,
                StencilStoreOp = StoreOp.Discard,
                StencilClearValue = 0,
                StencilReadOnly = false
            };

            RenderPassDescriptor renderPassDescriptor = new()
            {
                ColorAttachmentCount = 1,
                ColorAttachments = &colorAttachment,
                DepthStencilAttachment = &depthStencilAttachment
            };

            passEncoder = this.webGpu.CommandEncoderBeginRenderPass(commandEncoder, in renderPassDescriptor);
            if (passEncoder is null)
            {
                return false;
            }

            this.webGpu.RenderPassEncoderSetStencilReference(passEncoder, 0);
            this.webGpu.RenderPassEncoderSetVertexBuffer(passEncoder, 0, vertexBuffer, 0, vertexByteCount);
            if (rasterizerOptions.IntersectionRule == IntersectionRule.EvenOdd)
            {
                this.webGpu.RenderPassEncoderSetPipeline(passEncoder, this.coverageStencilEvenOddPipeline);
                this.webGpu.RenderPassEncoderDraw(passEncoder, (uint)coverageTriangleData.Vertices.Length, 1, 0, 0);
            }
            else
            {
                this.webGpu.RenderPassEncoderSetPipeline(passEncoder, this.coverageStencilNonZeroIncrementPipeline);
                this.webGpu.RenderPassEncoderDraw(passEncoder, (uint)coverageTriangleData.Vertices.Length, 1, 0, 0);

                this.webGpu.RenderPassEncoderSetPipeline(passEncoder, this.coverageStencilNonZeroDecrementPipeline);
                this.webGpu.RenderPassEncoderDraw(passEncoder, (uint)coverageTriangleData.Vertices.Length, 1, 0, 0);
            }

            this.webGpu.RenderPassEncoderSetStencilReference(passEncoder, 0);
            this.webGpu.RenderPassEncoderSetPipeline(passEncoder, this.coverageCoverPipeline);
            this.webGpu.RenderPassEncoderDraw(passEncoder, CoverageCoverVertexCount, 1, 0, 0);

            this.webGpu.RenderPassEncoderEnd(passEncoder);
            this.webGpu.RenderPassEncoderRelease(passEncoder);
            passEncoder = null;

            CommandBufferDescriptor commandBufferDescriptor = default;
            commandBuffer = this.webGpu.CommandEncoderFinish(commandEncoder, in commandBufferDescriptor);
            if (commandBuffer is null)
            {
                return false;
            }

            this.webGpu.QueueSubmit(this.queue, 1, ref commandBuffer);

            this.webGpu.CommandBufferRelease(commandBuffer);
            commandBuffer = null;
            coverageTexture = createdCoverageTexture;
            coverageView = createdCoverageView;
            createdCoverageTexture = null;
            createdCoverageView = null;
            success = true;
            Trace("TryRasterizeCoverageTextureLocked: submitted");
            return true;
        }
        finally
        {
            if (passEncoder is not null)
            {
                this.webGpu.RenderPassEncoderRelease(passEncoder);
            }

            if (commandBuffer is not null)
            {
                this.webGpu.CommandBufferRelease(commandBuffer);
            }

            if (commandEncoder is not null)
            {
                this.webGpu.CommandEncoderRelease(commandEncoder);
            }

            this.ReleaseBufferLocked(vertexBuffer);
            this.ReleaseTextureViewLocked(stencilView);
            this.ReleaseTextureLocked(stencilTexture);
            this.ReleaseTextureViewLocked(multisampleCoverageView);
            this.ReleaseTextureLocked(multisampleCoverageTexture);

            if (!success)
            {
                this.ReleaseTextureViewLocked(createdCoverageView);
                this.ReleaseTextureLocked(createdCoverageTexture);
            }
        }
    }

    /// <summary>
    /// Flattens a path into local-interest coordinates and converts each edge to a triangle
    /// anchored at an external origin. These triangles are consumed by the stencil pass.
    /// </summary>
    private static bool TryBuildCoverageTriangles(
        IPath path,
        Point interestLocation,
        Size interestSize,
        RasterizerSamplingOrigin samplingOrigin,
        out CoverageTriangleData coverageTriangleData)
    {
        coverageTriangleData = default;
        if (interestSize.Width <= 0 || interestSize.Height <= 0)
        {
            return false;
        }

        float sampleShift = samplingOrigin == RasterizerSamplingOrigin.PixelBoundary ? 0.5F : 0F;
        float offsetX = sampleShift - interestLocation.X;
        float offsetY = sampleShift - interestLocation.Y;

        List<CoverageSegment> segments = [];
        float minX = float.PositiveInfinity;
        float minY = float.PositiveInfinity;

        foreach (ISimplePath simplePath in path.Flatten())
        {
            ReadOnlySpan<PointF> points = simplePath.Points.Span;
            if (points.Length < 2)
            {
                continue;
            }

            for (int i = 1; i < points.Length; i++)
            {
                AddCoverageSegment(points[i - 1], points[i], offsetX, offsetY, segments, ref minX, ref minY);
            }

            if (simplePath.IsClosed)
            {
                AddCoverageSegment(points[^1], points[0], offsetX, offsetY, segments, ref minX, ref minY);
            }
        }

        if (segments.Count == 0 || !float.IsFinite(minX) || !float.IsFinite(minY))
        {
            return false;
        }

        float originX = minX - 1F;
        float originY = minY - 1F;
        float widthScale = 2F / interestSize.Width;
        float heightScale = 2F / interestSize.Height;

        StencilVertex[] vertices = new StencilVertex[checked(segments.Count * 3)];
        int vertexIndex = 0;
        foreach (CoverageSegment segment in segments)
        {
            vertices[vertexIndex++] = ToStencilVertex(originX, originY, widthScale, heightScale);
            vertices[vertexIndex++] = ToStencilVertex(segment.FromX, segment.FromY, widthScale, heightScale);
            vertices[vertexIndex++] = ToStencilVertex(segment.ToX, segment.ToY, widthScale, heightScale);
        }

        coverageTriangleData = new CoverageTriangleData(vertices);
        return true;
    }

    private static void AddCoverageSegment(
        PointF from,
        PointF to,
        float offsetX,
        float offsetY,
        List<CoverageSegment> destination,
        ref float minX,
        ref float minY)
    {
        if (from.Equals(to))
        {
            return;
        }

        if (!float.IsFinite(from.X) ||
            !float.IsFinite(from.Y) ||
            !float.IsFinite(to.X) ||
            !float.IsFinite(to.Y))
        {
            return;
        }

        float fromX = from.X + offsetX;
        float fromY = from.Y + offsetY;
        float toX = to.X + offsetX;
        float toY = to.Y + offsetY;

        destination.Add(new CoverageSegment(fromX, fromY, toX, toY));
        minX = MathF.Min(minX, MathF.Min(fromX, toX));
        minY = MathF.Min(minY, MathF.Min(fromY, toY));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static StencilVertex ToStencilVertex(float x, float y, float widthScale, float heightScale)
    {
        return new StencilVertex
        {
            X = (x * widthScale) - 1F,
            Y = 1F - (y * heightScale)
        };
    }

    private bool WaitForSignalLocked(ManualResetEventSlim signal)
    {
        Stopwatch timer = Stopwatch.StartNew();
        SpinWait spinner = default;
        while (!signal.IsSet)
        {
            if (timer.ElapsedMilliseconds >= CallbackTimeoutMilliseconds)
            {
                return false;
            }

            if (this.wgpuExtension is not null && this.device is not null)
            {
                _ = this.wgpuExtension.DevicePoll(this.device, false, (WrappedSubmissionIndex*)null);
                continue;
            }

            if (this.instance is not null && this.webGpu is not null)
            {
                this.webGpu.InstanceProcessEvents(this.instance);
            }

            if (!signal.IsSet)
            {
                if (spinner.Count < 10)
                {
                    spinner.SpinOnce();
                }
                else
                {
                    Thread.Yield();
                }
            }
        }

        return true;
    }

    private bool TryQueueWriteTextureFromRgbaRegionLocked(Texture* destinationTexture, Buffer2DRegion<Rgba32> sourceRegion)
    {
        if (this.webGpu is null || this.queue is null || destinationTexture is null)
        {
            return false;
        }

        int pixelSizeInBytes = Unsafe.SizeOf<Rgba32>();
        ImageCopyTexture destination = new()
        {
            Texture = destinationTexture,
            MipLevel = 0,
            Origin = new Origin3D(0, 0, 0),
            Aspect = TextureAspect.All
        };

        Extent3D writeSize = new((uint)sourceRegion.Width, (uint)sourceRegion.Height, 1);

        // For full-row regions in a contiguous buffer, upload directly with source stride.
        // For subregions, prefer tightly packed upload to avoid transferring row gaps.
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

            Span<Rgba32> firstRow = sourceRegion.DangerousGetRowSpan(0);
            fixed (Rgba32* uploadPtr = firstRow)
            {
                this.webGpu.QueueWriteTexture(this.queue, in destination, uploadPtr, sourceByteCount, in layout, in writeSize);
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
                ReadOnlySpan<Rgba32> sourceRow = sourceRegion.DangerousGetRowSpan(y);
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
                this.webGpu.QueueWriteTexture(this.queue, in destination, uploadPtr, (nuint)packedByteCount, in layout, in writeSize);
            }

            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    /// <summary>
    /// Ensures session resources for the target size, then uploads target pixels once.
    /// </summary>
    private bool TryBeginCompositeSessionLocked(Buffer2DRegion<Rgba32> target)
    {
        if (!this.IsGpuReady ||
            this.webGpu is null ||
            this.device is null ||
            this.queue is null ||
            target.Width <= 0 ||
            target.Height <= 0)
        {
            return false;
        }

        if (!this.TryEnsureCompositeSessionResourcesLocked(target.Width, target.Height) ||
            this.compositeSessionTargetTexture is null)
        {
            return false;
        }

        this.ResetCompositeSessionStateLocked();
        if (!this.TryQueueWriteTextureFromRgbaRegionLocked(this.compositeSessionTargetTexture, target))
        {
            return false;
        }

        this.compositeSessionTarget = target;
        this.compositeSessionDirty = false;
        return true;
    }

    private bool TryEnsureCompositeSessionResourcesLocked(int width, int height)
    {
        if (!this.IsGpuReady ||
            this.webGpu is null ||
            this.device is null ||
            width <= 0 ||
            height <= 0)
        {
            return false;
        }

        if (this.compositeSessionTargetTexture is not null &&
            this.compositeSessionTargetView is not null &&
            this.compositeSessionReadbackBuffer is not null &&
            this.compositeSessionResourceWidth == width &&
            this.compositeSessionResourceHeight == height)
        {
            return true;
        }

        this.ReleaseCompositeSessionResourcesLocked();

        uint textureRowBytes = checked((uint)width * (uint)Unsafe.SizeOf<Rgba32>());
        uint readbackRowBytes = AlignTo256(textureRowBytes);
        ulong readbackByteCount = (ulong)readbackRowBytes * (uint)height;

        TextureDescriptor targetTextureDescriptor = new()
        {
            Usage = TextureUsage.RenderAttachment | TextureUsage.CopySrc | TextureUsage.CopyDst,
            Dimension = TextureDimension.Dimension2D,
            Size = new Extent3D((uint)width, (uint)height, 1),
            Format = TextureFormat.Rgba8Unorm,
            MipLevelCount = 1,
            SampleCount = 1
        };

        Texture* targetTexture = this.webGpu.DeviceCreateTexture(this.device, in targetTextureDescriptor);
        if (targetTexture is null)
        {
            return false;
        }

        TextureViewDescriptor targetViewDescriptor = new()
        {
            Format = TextureFormat.Rgba8Unorm,
            Dimension = TextureViewDimension.Dimension2D,
            BaseMipLevel = 0,
            MipLevelCount = 1,
            BaseArrayLayer = 0,
            ArrayLayerCount = 1,
            Aspect = TextureAspect.All
        };

        TextureView* targetView = this.webGpu.TextureCreateView(targetTexture, in targetViewDescriptor);
        if (targetView is null)
        {
            this.ReleaseTextureLocked(targetTexture);
            return false;
        }

        BufferDescriptor readbackBufferDescriptor = new()
        {
            Usage = BufferUsage.MapRead | BufferUsage.CopyDst,
            Size = readbackByteCount
        };

        WgpuBuffer* readbackBuffer = this.webGpu.DeviceCreateBuffer(this.device, in readbackBufferDescriptor);
        if (readbackBuffer is null)
        {
            this.ReleaseTextureViewLocked(targetView);
            this.ReleaseTextureLocked(targetTexture);
            return false;
        }

        this.compositeSessionTargetTexture = targetTexture;
        this.compositeSessionTargetView = targetView;
        this.compositeSessionReadbackBuffer = readbackBuffer;
        this.compositeSessionReadbackBytesPerRow = readbackRowBytes;
        this.compositeSessionReadbackByteCount = readbackByteCount;
        this.compositeSessionResourceWidth = width;
        this.compositeSessionResourceHeight = height;
        return true;
    }

    /// <summary>
    /// Reads the session target texture back into the canvas region.
    /// </summary>
    private bool TryFlushCompositeSessionLocked()
    {
        Trace("TryFlushCompositeSessionLocked: begin");
        if (this.webGpu is null ||
            this.device is null ||
            this.queue is null ||
            this.compositeSessionTargetTexture is null ||
            this.compositeSessionReadbackBuffer is null ||
            this.compositeSessionTarget.Width <= 0 ||
            this.compositeSessionTarget.Height <= 0 ||
            this.compositeSessionReadbackByteCount == 0 ||
            this.compositeSessionReadbackBytesPerRow == 0)
        {
            return false;
        }

        CommandEncoder* commandEncoder = this.compositeSessionCommandEncoder;
        bool usingSessionCommandEncoder = commandEncoder is not null;
        CommandBuffer* commandBuffer = null;
        try
        {
            if (commandEncoder is null)
            {
                CommandEncoderDescriptor commandEncoderDescriptor = default;
                commandEncoder = this.webGpu.DeviceCreateCommandEncoder(this.device, in commandEncoderDescriptor);
                if (commandEncoder is null)
                {
                    return false;
                }
            }

            ImageCopyTexture source = new()
            {
                Texture = this.compositeSessionTargetTexture,
                MipLevel = 0,
                Origin = new Origin3D(0, 0, 0),
                Aspect = TextureAspect.All
            };

            ImageCopyBuffer destination = new()
            {
                Buffer = this.compositeSessionReadbackBuffer,
                Layout = new TextureDataLayout
                {
                    Offset = 0,
                    BytesPerRow = this.compositeSessionReadbackBytesPerRow,
                    RowsPerImage = (uint)this.compositeSessionTarget.Height
                }
            };

            Extent3D copySize = new((uint)this.compositeSessionTarget.Width, (uint)this.compositeSessionTarget.Height, 1);
            this.webGpu.CommandEncoderCopyTextureToBuffer(commandEncoder, in source, in destination, in copySize);

            CommandBufferDescriptor commandBufferDescriptor = default;
            commandBuffer = this.webGpu.CommandEncoderFinish(commandEncoder, in commandBufferDescriptor);
            if (commandBuffer is null)
            {
                return false;
            }

            this.compositeSessionCommandEncoder = null;

            this.webGpu.QueueSubmit(this.queue, 1, ref commandBuffer);
            this.webGpu.CommandBufferRelease(commandBuffer);
            commandBuffer = null;

            if (!this.TryReadBackBufferToRgbaRegionLocked(
                this.compositeSessionReadbackBuffer,
                checked((int)this.compositeSessionReadbackBytesPerRow),
                this.compositeSessionTarget))
            {
                Trace("TryFlushCompositeSessionLocked: readback failed");
                return false;
            }

            Trace("TryFlushCompositeSessionLocked: completed");
            return true;
        }
        finally
        {
            if (usingSessionCommandEncoder)
            {
                this.compositeSessionCommandEncoder = null;
            }

            if (commandBuffer is not null)
            {
                this.webGpu.CommandBufferRelease(commandBuffer);
            }

            if (commandEncoder is not null)
            {
                this.webGpu.CommandEncoderRelease(commandEncoder);
            }
        }
    }

    private void ResetCompositeSessionStateLocked()
    {
        if (this.compositeSessionCommandEncoder is not null && this.webGpu is not null)
        {
            this.webGpu.CommandEncoderRelease(this.compositeSessionCommandEncoder);
            this.compositeSessionCommandEncoder = null;
        }

        this.compositeSessionTarget = default;
        this.compositeSessionDirty = false;
    }

    private void ReleaseCompositeSessionResourcesLocked()
    {
        if (this.compositeSessionCommandEncoder is not null && this.webGpu is not null)
        {
            this.webGpu.CommandEncoderRelease(this.compositeSessionCommandEncoder);
            this.compositeSessionCommandEncoder = null;
        }

        this.ReleaseBufferLocked(this.compositeSessionReadbackBuffer);
        this.ReleaseTextureViewLocked(this.compositeSessionTargetView);
        this.ReleaseTextureLocked(this.compositeSessionTargetTexture);
        this.compositeSessionReadbackBuffer = null;
        this.compositeSessionTargetTexture = null;
        this.compositeSessionTargetView = null;
        this.compositeSessionReadbackBytesPerRow = 0;
        this.compositeSessionReadbackByteCount = 0;
        this.compositeSessionResourceWidth = 0;
        this.compositeSessionResourceHeight = 0;
    }

    private bool TryCompositeCoverageGpu(
        Buffer2DRegion<Rgba32> target,
        DrawingCoverageHandle coverageHandle,
        Point sourceOffset,
        WebGpuBrushData brushData,
        float blendPercentage)
    {
        if (!coverageHandle.IsValid)
        {
            return true;
        }

        if (!this.preparedCoverage.TryGetValue(coverageHandle.Value, out CoverageEntry? entry))
        {
            throw new InvalidOperationException($"Prepared coverage handle '{coverageHandle.Value}' is not valid.");
        }

        if (entry.IsFallback)
        {
            return false;
        }

        if (target.Width <= 0 || target.Height <= 0)
        {
            return true;
        }

        if ((uint)sourceOffset.X >= (uint)entry.Width || (uint)sourceOffset.Y >= (uint)entry.Height)
        {
            return true;
        }

        int compositeWidth = Math.Min(target.Width, entry.Width - sourceOffset.X);
        int compositeHeight = Math.Min(target.Height, entry.Height - sourceOffset.Y);
        if (compositeWidth <= 0 || compositeHeight <= 0)
        {
            return true;
        }

        Buffer2DRegion<Rgba32> destinationRegion = target.GetSubRegion(0, 0, compositeWidth, compositeHeight);

        lock (this.gpuSync)
        {
            if (!this.IsGpuReady || this.webGpu is null || this.device is null || this.queue is null ||
                this.compositePipeline is null || this.compositeBindGroupLayout is null)
            {
                return false;
            }

            if (!TryEnsureCoverageTextureLocked(entry))
            {
                return false;
            }

            if (this.compositeSessionGpuActive &&
                this.compositeSessionTargetTexture is not null &&
                this.compositeSessionTargetView is not null)
            {
                int destinationX = destinationRegion.Rectangle.X - this.compositeSessionTarget.Rectangle.X;
                int destinationY = destinationRegion.Rectangle.Y - this.compositeSessionTarget.Rectangle.Y;
                if ((uint)destinationX >= (uint)this.compositeSessionTarget.Width ||
                    (uint)destinationY >= (uint)this.compositeSessionTarget.Height)
                {
                    return false;
                }

                int sessionCompositeWidth = Math.Min(compositeWidth, this.compositeSessionTarget.Width - destinationX);
                int sessionCompositeHeight = Math.Min(compositeHeight, this.compositeSessionTarget.Height - destinationY);
                if (sessionCompositeWidth <= 0 || sessionCompositeHeight <= 0)
                {
                    return true;
                }

                if (!this.TryEnsureCompositeSessionCommandEncoderLocked())
                {
                    return false;
                }

                if (this.TryRunCompositePassLocked(
                    this.compositeSessionCommandEncoder,
                    entry,
                    sourceOffset,
                    brushData,
                    blendPercentage,
                    this.compositeSessionTargetView,
                    this.compositeSessionTarget.Width,
                    this.compositeSessionTarget.Height,
                    destinationX,
                    destinationY,
                    sessionCompositeWidth,
                    sessionCompositeHeight))
                {
                    this.compositeSessionDirty = true;
                    return true;
                }

                if (this.compositeSessionDirty)
                {
                    this.TryFlushCompositeSessionLocked();
                }

                this.ResetCompositeSessionStateLocked();
                this.ReleaseCompositeSessionResourcesLocked();
                this.compositeSessionGpuActive = false;
                return false;
            }

            return false;
        }
    }

    private bool TryEnsureCompositeSessionCommandEncoderLocked()
    {
        if (this.compositeSessionCommandEncoder is not null)
        {
            return true;
        }

        if (this.webGpu is null || this.device is null)
        {
            return false;
        }

        CommandEncoderDescriptor commandEncoderDescriptor = default;
        this.compositeSessionCommandEncoder = this.webGpu.DeviceCreateCommandEncoder(this.device, in commandEncoderDescriptor);
        return this.compositeSessionCommandEncoder is not null;
    }

    private static bool TryEnsureCoverageTextureLocked(CoverageEntry entry)
    {
        if (entry.GpuCoverageTexture is not null && entry.GpuCoverageView is not null)
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Executes one composition draw call into the session target texture.
    /// </summary>
    private bool TryRunCompositePassLocked(
        CommandEncoder* commandEncoder,
        CoverageEntry coverageEntry,
        Point sourceOffset,
        WebGpuBrushData brushData,
        float blendPercentage,
        TextureView* targetView,
        int targetWidth,
        int targetHeight,
        int destinationX,
        int destinationY,
        int compositeWidth,
        int compositeHeight)
    {
        if (this.webGpu is null ||
            this.device is null ||
            this.queue is null ||
            this.compositePipeline is null ||
            this.compositeBindGroupLayout is null ||
            coverageEntry.GpuCoverageView is null ||
            targetView is null ||
            targetWidth <= 0 ||
            targetHeight <= 0)
        {
            return false;
        }

        if (compositeWidth <= 0 || compositeHeight <= 0)
        {
            return true;
        }

        ulong uniformByteCount = (ulong)Unsafe.SizeOf<CompositeParams>();
        WgpuBuffer* uniformBuffer = null;
        BindGroup* bindGroup = null;
        CommandEncoder* createdCommandEncoder = null;
        RenderPassEncoder* passEncoder = null;
        CommandBuffer* commandBuffer = null;
        try
        {
            BufferDescriptor uniformBufferDescriptor = new()
            {
                Usage = BufferUsage.Uniform | BufferUsage.CopyDst,
                Size = uniformByteCount
            };
            uniformBuffer = this.webGpu.DeviceCreateBuffer(this.device, in uniformBufferDescriptor);
            if (uniformBuffer is null)
            {
                return false;
            }

            CompositeParams parameters = new()
            {
                SourceOffsetX = (uint)sourceOffset.X,
                SourceOffsetY = (uint)sourceOffset.Y,
                DestinationX = (uint)destinationX,
                DestinationY = (uint)destinationY,
                DestinationWidth = (uint)compositeWidth,
                DestinationHeight = (uint)compositeHeight,
                TargetWidth = (uint)targetWidth,
                TargetHeight = (uint)targetHeight,
                BrushKind = (uint)brushData.Kind,
                SolidBrushColor = brushData.SolidColor,
                BlendPercentage = blendPercentage
            };

            this.webGpu.QueueWriteBuffer(
                this.queue,
                uniformBuffer,
                0,
                ref parameters,
                (nuint)Unsafe.SizeOf<CompositeParams>());

            BindGroupEntry* bindEntries = stackalloc BindGroupEntry[2];
            bindEntries[0] = new BindGroupEntry
            {
                Binding = 0,
                TextureView = coverageEntry.GpuCoverageView
            };
            bindEntries[1] = new BindGroupEntry
            {
                Binding = 1,
                Buffer = uniformBuffer,
                Offset = 0,
                Size = uniformByteCount
            };

            BindGroupDescriptor bindGroupDescriptor = new()
            {
                Layout = this.compositeBindGroupLayout,
                EntryCount = 2,
                Entries = bindEntries
            };
            bindGroup = this.webGpu.DeviceCreateBindGroup(this.device, in bindGroupDescriptor);
            if (bindGroup is null)
            {
                return false;
            }

            CommandEncoder* compositeCommandEncoder = commandEncoder;
            if (compositeCommandEncoder is null)
            {
                CommandEncoderDescriptor commandEncoderDescriptor = default;
                createdCommandEncoder = this.webGpu.DeviceCreateCommandEncoder(this.device, in commandEncoderDescriptor);
                if (createdCommandEncoder is null)
                {
                    return false;
                }

                compositeCommandEncoder = createdCommandEncoder;
            }

            RenderPassColorAttachment colorAttachment = new()
            {
                View = targetView,
                ResolveTarget = null,
                LoadOp = LoadOp.Load,
                StoreOp = StoreOp.Store,
                ClearValue = default
            };

            RenderPassDescriptor renderPassDescriptor = new()
            {
                ColorAttachmentCount = 1,
                ColorAttachments = &colorAttachment
            };

            passEncoder = this.webGpu.CommandEncoderBeginRenderPass(compositeCommandEncoder, in renderPassDescriptor);
            if (passEncoder is null)
            {
                return false;
            }

            this.webGpu.RenderPassEncoderSetPipeline(passEncoder, this.compositePipeline);
            this.webGpu.RenderPassEncoderSetBindGroup(passEncoder, 0, bindGroup, 0, (uint*)null);
            this.webGpu.RenderPassEncoderDraw(passEncoder, CompositeVertexCount, 1, 0, 0);
            this.webGpu.RenderPassEncoderEnd(passEncoder);
            this.webGpu.RenderPassEncoderRelease(passEncoder);
            passEncoder = null;

            if (createdCommandEncoder is null)
            {
                return true;
            }

            CommandBufferDescriptor commandBufferDescriptor = default;
            commandBuffer = this.webGpu.CommandEncoderFinish(createdCommandEncoder, in commandBufferDescriptor);
            if (commandBuffer is null)
            {
                return false;
            }

            this.webGpu.QueueSubmit(this.queue, 1, ref commandBuffer);

            this.webGpu.CommandBufferRelease(commandBuffer);
            commandBuffer = null;
            return true;
        }
        finally
        {
            if (passEncoder is not null)
            {
                this.webGpu.RenderPassEncoderRelease(passEncoder);
            }

            if (commandBuffer is not null)
            {
                this.webGpu.CommandBufferRelease(commandBuffer);
            }

            if (createdCommandEncoder is not null)
            {
                this.webGpu.CommandEncoderRelease(createdCommandEncoder);
            }

            if (bindGroup is not null)
            {
                this.webGpu.BindGroupRelease(bindGroup);
            }

            this.ReleaseBufferLocked(uniformBuffer);
        }
    }

    private bool TryMapReadBufferLocked(WgpuBuffer* readbackBuffer, nuint byteCount, out byte* mappedData)
    {
        mappedData = null;

        if (this.webGpu is null || readbackBuffer is null)
        {
            return false;
        }

        Trace($"TryReadBackBufferLocked: begin bytes={byteCount}");
        BufferMapAsyncStatus mapStatus = BufferMapAsyncStatus.Unknown;
        using ManualResetEventSlim callbackReady = new(false);
        void Callback(BufferMapAsyncStatus status, void* userDataPtr)
        {
            mapStatus = status;
            _ = userDataPtr;
            callbackReady.Set();
        }

        using PfnBufferMapCallback callbackPtr = PfnBufferMapCallback.From(Callback);
        this.webGpu.BufferMapAsync(readbackBuffer, MapMode.Read, 0, byteCount, callbackPtr, null);

        if (!this.WaitForSignalLocked(callbackReady) || mapStatus != BufferMapAsyncStatus.Success)
        {
            Trace($"TryReadBackBufferLocked: map failed status={mapStatus}");
            return false;
        }

        Trace("TryReadBackBufferLocked: map callback success");
        void* rawMappedData = this.webGpu.BufferGetConstMappedRange(readbackBuffer, 0, byteCount);
        if (rawMappedData is null)
        {
            this.webGpu.BufferUnmap(readbackBuffer);
            Trace("TryReadBackBufferLocked: mapped range null");
            return false;
        }

        mappedData = (byte*)rawMappedData;
        return true;
    }

    private bool TryReadBackBufferToRgbaRegionLocked(
        WgpuBuffer* readbackBuffer,
        int sourceRowBytes,
        Buffer2DRegion<Rgba32> destinationRegion)
    {
        if (destinationRegion.Width <= 0 || destinationRegion.Height <= 0)
        {
            return true;
        }

        int destinationRowBytes = checked(destinationRegion.Width * Unsafe.SizeOf<Rgba32>());
        int readbackByteCount = checked(sourceRowBytes * destinationRegion.Height);
        if (!this.TryMapReadBufferLocked(readbackBuffer, (nuint)readbackByteCount, out byte* mappedData))
        {
            return false;
        }

        try
        {
            ReadOnlySpan<byte> sourceData = new(mappedData, readbackByteCount);
            int destinationStrideBytes = checked(destinationRegion.Buffer.Width * Unsafe.SizeOf<Rgba32>());

            // If the target region spans full rows in a contiguous backing buffer we can copy
            // the mapped data in one block instead of per-row.
            if (destinationRegion.Rectangle.X == 0 &&
                sourceRowBytes == destinationStrideBytes &&
                TryGetSingleMemory(destinationRegion.Buffer, out Memory<Rgba32> contiguousDestination))
            {
                Span<byte> destinationBytes = MemoryMarshal.AsBytes(contiguousDestination.Span);
                int destinationStart = checked(destinationRegion.Rectangle.Y * destinationStrideBytes);
                int copyByteCount = checked(destinationStrideBytes * destinationRegion.Height);
                if (destinationBytes.Length >= destinationStart + copyByteCount)
                {
                    sourceData[..copyByteCount].CopyTo(destinationBytes.Slice(destinationStart, copyByteCount));
                    return true;
                }
            }

            for (int y = 0; y < destinationRegion.Height; y++)
            {
                ReadOnlySpan<byte> sourceRow = sourceData.Slice(y * sourceRowBytes, destinationRowBytes);
                MemoryMarshal.Cast<byte, Rgba32>(sourceRow).CopyTo(destinationRegion.DangerousGetRowSpan(y));
            }

            return true;
        }
        finally
        {
            this.webGpu?.BufferUnmap(readbackBuffer);

            Trace("TryReadBackBufferLocked: completed");
        }
    }

    private void ReleaseCoverageTextureLocked(CoverageEntry entry)
    {
        Trace($"ReleaseCoverageTextureLocked: tex={(nint)entry.GpuCoverageTexture:X} view={(nint)entry.GpuCoverageView:X}");
        this.ReleaseTextureViewLocked(entry.GpuCoverageView);
        this.ReleaseTextureLocked(entry.GpuCoverageTexture);
        entry.GpuCoverageView = null;
        entry.GpuCoverageTexture = null;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint AlignTo256(uint value) => (value + 255U) & ~255U;

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

    private void ReleaseTextureViewLocked(TextureView* textureView)
    {
        if (textureView is null || this.webGpu is null)
        {
            return;
        }

        this.webGpu.TextureViewRelease(textureView);
    }

    private void ReleaseTextureLocked(Texture* texture)
    {
        if (texture is null || this.webGpu is null)
        {
            return;
        }

        this.webGpu.TextureRelease(texture);
    }

    private void ReleaseBufferLocked(WgpuBuffer* buffer)
    {
        if (buffer is null || this.webGpu is null)
        {
            return;
        }

        this.webGpu.BufferRelease(buffer);
    }

    private void TryDestroyAndDrainDeviceLocked()
    {
        if (this.webGpu is null || this.device is null)
        {
            return;
        }

        this.webGpu.DeviceDestroy(this.device);

        if (this.wgpuExtension is not null)
        {
            // Drain native callbacks/work queues before releasing the device and unloading.
            _ = this.wgpuExtension.DevicePoll(this.device, true, (WrappedSubmissionIndex*)null);
            _ = this.wgpuExtension.DevicePoll(this.device, true, (WrappedSubmissionIndex*)null);
            return;
        }

        if (this.instance is not null)
        {
            this.webGpu.InstanceProcessEvents(this.instance);
            this.webGpu.InstanceProcessEvents(this.instance);
        }
    }

    private void ReleaseGpuResourcesLocked()
    {
        Trace("ReleaseGpuResourcesLocked: begin");
        this.ResetCompositeSessionStateLocked();
        this.ReleaseCompositeSessionResourcesLocked();

        if (this.webGpu is not null)
        {
            if (this.coverageCoverPipeline is not null)
            {
                this.webGpu.RenderPipelineRelease(this.coverageCoverPipeline);
                this.coverageCoverPipeline = null;
            }

            if (this.coverageStencilNonZeroDecrementPipeline is not null)
            {
                this.webGpu.RenderPipelineRelease(this.coverageStencilNonZeroDecrementPipeline);
                this.coverageStencilNonZeroDecrementPipeline = null;
            }

            if (this.coverageStencilNonZeroIncrementPipeline is not null)
            {
                this.webGpu.RenderPipelineRelease(this.coverageStencilNonZeroIncrementPipeline);
                this.coverageStencilNonZeroIncrementPipeline = null;
            }

            if (this.coverageStencilEvenOddPipeline is not null)
            {
                this.webGpu.RenderPipelineRelease(this.coverageStencilEvenOddPipeline);
                this.coverageStencilEvenOddPipeline = null;
            }

            if (this.coveragePipelineLayout is not null)
            {
                this.webGpu.PipelineLayoutRelease(this.coveragePipelineLayout);
                this.coveragePipelineLayout = null;
            }

            if (this.compositePipeline is not null)
            {
                this.webGpu.RenderPipelineRelease(this.compositePipeline);
                this.compositePipeline = null;
            }

            if (this.compositePipelineLayout is not null)
            {
                this.webGpu.PipelineLayoutRelease(this.compositePipelineLayout);
                this.compositePipelineLayout = null;
            }

            if (this.compositeBindGroupLayout is not null)
            {
                this.webGpu.BindGroupLayoutRelease(this.compositeBindGroupLayout);
                this.compositeBindGroupLayout = null;
            }

            if (this.device is not null)
            {
                this.TryDestroyAndDrainDeviceLocked();
            }

            if (this.queue is not null)
            {
                this.webGpu.QueueRelease(this.queue);
                this.queue = null;
            }

            if (this.device is not null)
            {
                this.webGpu.DeviceRelease(this.device);
                this.device = null;
            }

            if (this.adapter is not null)
            {
                this.webGpu.AdapterRelease(this.adapter);
                this.adapter = null;
            }

            if (this.instance is not null)
            {
                this.webGpu.InstanceRelease(this.instance);
                this.instance = null;
            }

            this.webGpu = null;
        }

        this.wgpuExtension = null;
        this.runtimeLease?.Dispose();
        this.runtimeLease = null;
        this.IsGpuReady = false;
        this.compositeSessionGpuActive = false;
        this.compositeSessionDepth = 0;
        Trace("ReleaseGpuResourcesLocked: end");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(this.isDisposed, this);

    [StructLayout(LayoutKind.Sequential)]
    private struct CompositeParams
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

    [StructLayout(LayoutKind.Sequential)]
    private struct StencilVertex
    {
        public float X;
        public float Y;
    }

    private readonly struct CoverageSegment
    {
        public CoverageSegment(float fromX, float fromY, float toX, float toY)
        {
            this.FromX = fromX;
            this.FromY = fromY;
            this.ToX = toX;
            this.ToY = toY;
        }

        public float FromX { get; }

        public float FromY { get; }

        public float ToX { get; }

        public float ToY { get; }
    }

    private readonly struct CoverageTriangleData
    {
        public CoverageTriangleData(StencilVertex[] vertices)
        {
            this.Vertices = vertices;
        }

        public StencilVertex[] Vertices { get; }
    }

    private sealed class CoverageEntry : IDisposable
    {
        public CoverageEntry(int width, int height)
        {
            this.Width = width;
            this.Height = height;
        }

        public int Width { get; }

        public int Height { get; }

        public DrawingCoverageHandle FallbackCoverageHandle { get; set; }

        public bool IsFallback => this.FallbackCoverageHandle.IsValid;

        public Texture* GpuCoverageTexture { get; set; }

        public TextureView* GpuCoverageView { get; set; }

        public void Dispose()
        {
        }
    }
}
