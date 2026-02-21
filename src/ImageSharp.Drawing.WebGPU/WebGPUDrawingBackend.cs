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
/// The public flow mirrors <see cref="DefaultDrawingBackend"/>:
/// <list type="number">
/// <item><description><c>FillPath</c> enqueues normalized composition commands.</description></item>
/// <item><description><c>FlushCompositions</c> executes the queued commands in order.</description></item>
/// </list>
/// GPU execution prepares coverage once (stencil-and-cover into R8 coverage), then composites all
/// queued commands against the active target session. If the pixel type is unsupported for GPU,
/// the whole flush delegates to <see cref="DefaultDrawingBackend"/>.
/// </remarks>
internal sealed unsafe partial class WebGPUDrawingBackend : IDrawingBackend, IDisposable
{
    private const uint CompositeVertexCount = 6;
    private const uint CompositeUniformAlignment = 256;
    private const uint CompositeUniformBufferSize = 256 * 1024;
    private const int CallbackTimeoutMilliseconds = 10_000;

    private static ReadOnlySpan<byte> CompositeVertexEntryPoint => "vs_main\0"u8;

    private static ReadOnlySpan<byte> CompositeFragmentEntryPoint => "fs_main\0"u8;

    private readonly object gpuSync = new();
    private readonly ConcurrentDictionary<int, CoverageEntry> preparedCoverage = new();
    private readonly DefaultDrawingBackend fallbackBackend;
    private WebGPURasterizer? coverageRasterizer;

    private int nextCoverageHandleId;
    private bool isDisposed;
    private WebGPURuntime.Lease? runtimeLease;
    private WebGPU? webGPU;
    private Wgpu? wgpuExtension;
    private Instance* instance;
    private Adapter* adapter;
    private Device* device;
    private Queue* queue;
    private BindGroupLayout* compositeBindGroupLayout;
    private PipelineLayout* compositePipelineLayout;
    private readonly ConcurrentDictionary<TextureFormat, nint> compositePipelines = new();

    private int compositeSessionDepth;
    private bool compositeSessionGPUActive;
    private bool compositeSessionDirty;
    private readonly List<GPUCompositeCommand> compositeSessionCommands = [];
    private RenderPassEncoder* compositeSessionPassEncoder;
    private Rectangle compositeSessionTargetRectangle;
    private Texture* compositeSessionTargetTexture;
    private TextureView* compositeSessionTargetView;
    private WgpuBuffer* compositeSessionReadbackBuffer;
    private WgpuBuffer* compositeSessionUniformBuffer;
    private uint compositeSessionUniformWriteOffset;
    private CommandEncoder* compositeSessionCommandEncoder;
    private uint compositeSessionReadbackBytesPerRow;
    private ulong compositeSessionReadbackByteCount;
    private int compositeSessionResourceWidth;
    private int compositeSessionResourceHeight;
    private TextureFormat compositeSessionResourceTextureFormat;
    private bool compositeSessionRequiresReadback;
    private bool compositeSessionOwnsTargetView;
    private static readonly Dictionary<Type, CompositePixelRegistration> CompositePixelHandlers = CreateCompositePixelHandlers();
    private static readonly bool TraceEnabled = string.Equals(
        Environment.GetEnvironmentVariable("IMAGESHARP_WEBGPU_TRACE"),
        "1",
        StringComparison.Ordinal);

    public WebGPUDrawingBackend()
    {
        this.fallbackBackend = DefaultDrawingBackend.Instance;
        lock (this.gpuSync)
        {
            this.GPUInitializationAttempted = true;
            this.LastGPUInitializationFailure = null;
            this.IsGPUReady = this.TryInitializeGPULocked();
        }
    }

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
    public int GPUPrepareCoverageCallCount { get; private set; }

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
    public int GPUCompositeCoverageCallCount { get; private set; }

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
    public bool IsGPUReady { get; private set; }

    /// <summary>
    /// Gets a value indicating whether GPU initialization has been attempted.
    /// </summary>
    public bool GPUInitializationAttempted { get; private set; }

    /// <summary>
    /// Gets the last GPU initialization failure reason, if any.
    /// </summary>
    public string? LastGPUInitializationFailure { get; private set; }

    /// <summary>
    /// Gets the number of prepared coverage entries currently cached by handle.
    /// </summary>
    public int LiveCoverageCount => this.preparedCoverage.Count;

    /// <summary>
    /// Begins a composite session for a target region.
    /// </summary>
    /// <remarks>
    /// Nested calls are reference-counted. CPU targets are uploaded to a GPU session texture.
    /// Native-surface targets bind directly to the surface view.
    /// </remarks>
    public void BeginCompositeSession<TPixel>(Configuration configuration, ICanvasFrame<TPixel> target)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        this.ThrowIfDisposed();
        Guard.NotNull(configuration, nameof(configuration));
        Guard.NotNull(target, nameof(target));

        if (this.compositeSessionDepth > 0)
        {
            this.compositeSessionDepth++;
            return;
        }

        this.compositeSessionDepth = 1;
        this.compositeSessionGPUActive = false;
        this.compositeSessionDirty = false;
        this.compositeSessionCommands.Clear();

        if (!CompositePixelHandlers.TryGetValue(typeof(TPixel), out CompositePixelRegistration pixelHandler) ||
            !this.IsGPUReady)
        {
            return;
        }

        lock (this.gpuSync)
        {
            if (!this.TryGetOrCreateCompositePipelineLocked(pixelHandler.TextureFormat, out _))
            {
                return;
            }
        }

        this.ActivateCompositeSession(target, pixelHandler);
    }

    /// <summary>
    /// Ends a previously started composite session.
    /// </summary>
    /// <remarks>
    /// When this is the outermost session and GPU work has modified the active target, the
    /// method either reads back into the CPU region (CPU session) or submits recorded commands
    /// directly to the native surface (native session), then clears active session state.
    /// </remarks>
    public void EndCompositeSession<TPixel>(Configuration configuration, ICanvasFrame<TPixel> target)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        this.ThrowIfDisposed();
        Guard.NotNull(configuration, nameof(configuration));
        Guard.NotNull(target, nameof(target));

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
            Trace($"EndCompositeSession: gpuActive={this.compositeSessionGPUActive} dirty={this.compositeSessionDirty}");
            if (this.compositeSessionGPUActive &&
                this.compositeSessionDirty)
            {
                if (!this.TryDrainQueuedCompositeCommandsLocked())
                {
                    throw new InvalidOperationException("Failed to encode queued GPU composite commands.");
                }
                else if (this.compositeSessionRequiresReadback &&
                    target.TryGetCpuRegion(out Buffer2DRegion<TPixel> cpuTarget))
                {
                    this.TryFlushCompositeSessionLocked(cpuTarget);
                }
                else if (!this.compositeSessionRequiresReadback)
                {
                    this.TrySubmitCompositeSessionLocked();
                }
                else
                {
                    Trace("EndCompositeSession: skipped flush because CPU target was unavailable.");
                }
            }

            this.ResetCompositeSessionStateLocked();
        }

        this.compositeSessionGPUActive = false;
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
        ICanvasFrame<TPixel> target,
        IPath path,
        Brush brush,
        GraphicsOptions graphicsOptions,
        in RasterizerOptions rasterizerOptions,
        DrawingCanvasBatcher<TPixel> batcher)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        this.ThrowIfDisposed();
        batcher.AddComposition(CompositionCommand.Create(path, brush, graphicsOptions, rasterizerOptions));
    }

    /// <inheritdoc />
    public void FlushCompositions<TPixel>(
        Configuration configuration,
        ICanvasFrame<TPixel> target,
        IReadOnlyList<CompositionCommand> compositions)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        this.ThrowIfDisposed();
        if (compositions.Count == 0)
        {
            return;
        }

        CompositionCommand coverageDefinition = compositions[0];
        ICanvasFrame<TPixel> compositeFrame = new CanvasRegionFrame<TPixel>(target, coverageDefinition.RasterizerOptions.Interest);
        bool useGPUPath = this.TryResolveGPUFlush<TPixel>(out CompositePixelRegistration pixelHandler);
        bool openedCompositeSession = false;
        DrawingCoverageHandle coverageHandle = default;

        if (useGPUPath)
        {
            if (this.compositeSessionDepth == 0)
            {
                this.compositeSessionDepth = 1;
                this.compositeSessionGPUActive = false;
                this.compositeSessionDirty = false;
                this.compositeSessionCommands.Clear();

                useGPUPath = this.ActivateCompositeSession(compositeFrame, pixelHandler);
                openedCompositeSession = true;
            }
            else
            {
                useGPUPath = this.compositeSessionGPUActive;
            }
        }

        if (useGPUPath)
        {
            coverageHandle = this.PrepareCoverage(
                coverageDefinition.Path,
                coverageDefinition.RasterizerOptions,
                configuration.MemoryAllocator,
                CoveragePreparationMode.Default);
            useGPUPath = coverageHandle.IsValid;
        }

        if (!useGPUPath)
        {
            if (openedCompositeSession)
            {
                this.EndCompositeSession(configuration, compositeFrame);
            }

            this.FlushCompositionsFallback(configuration, target, compositions);
            return;
        }

        try
        {
            for (int i = 0; i < compositions.Count; i++)
            {
                CompositionCommand command = compositions[i];
                this.CompositeCoverage(
                    configuration,
                    compositeFrame,
                    coverageHandle,
                    Point.Empty,
                    command.Brush,
                    command.GraphicsOptions,
                    command.BrushBounds);
            }
        }
        finally
        {
            if (openedCompositeSession)
            {
                this.EndCompositeSession(configuration, compositeFrame);
            }

            this.ReleaseCoverage(coverageHandle);
        }
    }

    /// <summary>
    /// Determines whether this backend can composite coverage with the given brush/options.
    /// </summary>
    public bool SupportsCoverageComposition<TPixel>(Brush brush, in GraphicsOptions graphicsOptions)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        Guard.NotNull(brush, nameof(brush));
        if (!CompositePixelHandlers.TryGetValue(typeof(TPixel), out CompositePixelRegistration pixelHandler) ||
            !this.IsGPUReady)
        {
            return false;
        }

        lock (this.gpuSync)
        {
            return this.TryGetOrCreateCompositePipelineLocked(pixelHandler.TextureFormat, out _);
        }
    }

    private void FlushCompositionsFallback<TPixel>(
        Configuration configuration,
        ICanvasFrame<TPixel> target,
        IReadOnlyList<CompositionCommand> compositions)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        if (target.TryGetCpuRegion(out _))
        {
            this.fallbackBackend.FlushCompositions(configuration, target, compositions);
            return;
        }

        Rectangle targetBounds = target.Bounds;
        using Buffer2D<TPixel> stagingBuffer = configuration.MemoryAllocator.Allocate2D<TPixel>(
            new Size(targetBounds.Width, targetBounds.Height),
            AllocationOptions.Clean);
        Buffer2DRegion<TPixel> stagingRegion = new(stagingBuffer, targetBounds);
        CpuCanvasFrame<TPixel> stagingFrame = new(stagingRegion);
        this.fallbackBackend.FlushCompositions(configuration, stagingFrame, compositions);

        if (!target.TryGetNativeSurface(out NativeSurface? nativeSurface) ||
            nativeSurface is null ||
            !nativeSurface.TryGetCapability(out WebGPUSurfaceCapability? surfaceCapability) ||
            surfaceCapability is null ||
            surfaceCapability.TargetTexture == 0)
        {
            throw new NotSupportedException(
                "Fallback composition requires either a CPU destination region or a native WebGPU surface exposing a writable texture handle.");
        }

        lock (this.gpuSync)
        {
            if (!this.QueueWriteTextureFromRegionLocked((Texture*)surfaceCapability.TargetTexture, stagingRegion))
            {
                throw new NotSupportedException(
                    "Fallback composition could not upload to the native WebGPU target texture.");
            }
        }
    }

    private bool TryResolveGPUFlush<TPixel>(out CompositePixelRegistration pixelHandler)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        pixelHandler = default;
        if (!CompositePixelHandlers.TryGetValue(typeof(TPixel), out pixelHandler) ||
            !this.IsGPUReady)
        {
            return false;
        }

        lock (this.gpuSync)
        {
            return this.TryGetOrCreateCompositePipelineLocked(pixelHandler.TextureFormat, out _);
        }
    }

    /// <summary>
    /// Prepares coverage for a path and returns an opaque reusable handle.
    /// </summary>
    /// <remarks>
    /// GPU preparation flattens path edges into local-interest coordinates, builds a tiled edge index,
    /// and rasterizes the coverage texture. When GPU preparation is unavailable this returns an invalid handle.
    /// </remarks>
    public DrawingCoverageHandle PrepareCoverage(
        IPath path,
        in RasterizerOptions rasterizerOptions,
        MemoryAllocator allocator,
        CoveragePreparationMode preparationMode)
    {
        this.ThrowIfDisposed();
        Guard.NotNull(path, nameof(path));
        _ = allocator;
        _ = preparationMode;

        this.PrepareCoverageCallCount++;
        Size size = rasterizerOptions.Interest.Size;

        Texture* coverageTexture = null;
        TextureView* coverageView = null;
        lock (this.gpuSync)
        {
            WebGPURasterizer? rasterizer = this.coverageRasterizer;
            if (rasterizer is null)
            {
                this.FallbackPrepareCoverageCallCount++;
                return default;
            }

            if (!rasterizer.TryCreateCoverageTexture(path, in rasterizerOptions, out coverageTexture, out coverageView))
            {
                this.FallbackPrepareCoverageCallCount++;
                return default;
            }
        }

        int handleId = Interlocked.Increment(ref this.nextCoverageHandleId);
        CoverageEntry entry = new(size.Width, size.Height)
        {
            GPUCoverageTexture = coverageTexture,
            GPUCoverageView = coverageView
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

        this.GPUPrepareCoverageCallCount++;
        return new DrawingCoverageHandle(handleId);
    }

    /// <summary>
    /// Composes prepared coverage into a target region using the provided brush.
    /// </summary>
    /// <remarks>
    /// Coverage handles are GPU-prepared and must be composed on the active GPU session.
    /// </remarks>
    public void CompositeCoverage<TPixel>(
        Configuration configuration,
        ICanvasFrame<TPixel> target,
        DrawingCoverageHandle coverageHandle,
        Point sourceOffset,
        Brush brush,
        in GraphicsOptions graphicsOptions,
        Rectangle brushBounds)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        this.ThrowIfDisposed();
        this.CompositeCoverageCallCount++;

        if (!WebGPUBrushData.TryCreate(brush, brushBounds, out WebGPUBrushData brushData))
        {
            throw new InvalidOperationException("Unsupported brush for WebGPU composition.");
        }

        if (!this.TryCompositeCoverageGPU(
            target,
            coverageHandle,
            sourceOffset,
            brushData,
            graphicsOptions.BlendPercentage))
        {
            throw new InvalidOperationException(
                "Accelerated coverage composition failed for a handle prepared for accelerated mode.");
        }

        this.GPUCompositeCoverageCallCount++;
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
            this.ResetCompositeSessionStateLocked();
            this.ReleaseCompositeSessionResourcesLocked();

            foreach (KeyValuePair<int, CoverageEntry> kv in this.preparedCoverage)
            {
                this.ReleaseCoverageTextureLocked(kv.Value);
                kv.Value.Dispose();
            }

            this.preparedCoverage.Clear();
            this.ReleaseGPUResourcesLocked();
        }

        this.isDisposed = true;
        Trace("Dispose: end");
    }

    private bool ActivateCompositeSession<TPixel>(
        ICanvasFrame<TPixel> target,
        in CompositePixelRegistration pixelHandler)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        lock (this.gpuSync)
        {
            bool started = false;
            if (target.TryGetCpuRegion(out Buffer2DRegion<TPixel> cpuTarget))
            {
                started = this.BeginCompositeSessionCoreLocked(
                    cpuTarget,
                    pixelHandler.TextureFormat,
                    pixelHandler.PixelSizeInBytes);
            }
            else if (TryGetNativeSurfaceCapability(target, pixelHandler.TextureFormat, out WebGPUSurfaceCapability? nativeSurfaceCapability) &&
                     nativeSurfaceCapability is not null &&
                     this.BeginCompositeSurfaceSessionCoreLocked(target, nativeSurfaceCapability))
            {
                started = true;
            }

            if (!started)
            {
                return false;
            }

            this.compositeSessionGPUActive = true;
            return true;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryGetNativeSurfaceCapability<TPixel>(
        ICanvasFrame<TPixel> target,
        TextureFormat expectedTargetFormat,
        out WebGPUSurfaceCapability? capability)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        if (!target.TryGetNativeSurface(out NativeSurface? nativeSurface) || nativeSurface is null)
        {
            capability = null;
            return false;
        }

        if (!nativeSurface.TryGetCapability(out WebGPUSurfaceCapability? surfaceCapability) ||
            surfaceCapability is null ||
            surfaceCapability.TargetTextureView == 0 ||
            surfaceCapability.TargetFormat != expectedTargetFormat)
        {
            capability = null;
            return false;
        }

        capability = surfaceCapability;
        return true;
    }

    /// <summary>
    /// Performs one-time GPU initialization while <see cref="gpuSync"/> is held.
    /// </summary>
    private bool TryInitializeGPULocked()
    {
        Trace("TryInitializeGPULocked: begin");
        try
        {
            this.runtimeLease = WebGPURuntime.Acquire();
            this.webGPU = this.runtimeLease.Api;
            this.wgpuExtension = this.runtimeLease.WgpuExtension;
            Trace($"TryInitializeGPULocked: extension={(this.wgpuExtension is null ? "none" : "wgpu.h")}");
            this.instance = this.webGPU.CreateInstance((InstanceDescriptor*)null);
            if (this.instance is null)
            {
                this.LastGPUInitializationFailure = "WebGPU.CreateInstance returned null.";
                Trace("TryInitializeGPULocked: CreateInstance returned null");
                return false;
            }

            Trace("TryInitializeGPULocked: created instance");
            if (!this.TryRequestAdapterLocked(out this.adapter) || this.adapter is null)
            {
                this.LastGPUInitializationFailure ??= "Failed to request WebGPU adapter.";
                Trace($"TryInitializeGPULocked: request adapter failed ({this.LastGPUInitializationFailure})");
                return false;
            }

            Trace("TryInitializeGPULocked: adapter acquired");
            if (!this.TryRequestDeviceLocked(out this.device) || this.device is null)
            {
                this.LastGPUInitializationFailure ??= "Failed to request WebGPU device.";
                Trace($"TryInitializeGPULocked: request device failed ({this.LastGPUInitializationFailure})");
                return false;
            }

            this.queue = this.webGPU.DeviceGetQueue(this.device);
            if (this.queue is null)
            {
                this.LastGPUInitializationFailure = "WebGPU.DeviceGetQueue returned null.";
                Trace("TryInitializeGPULocked: DeviceGetQueue returned null");
                return false;
            }

            Trace("TryInitializeGPULocked: queue acquired");
            if (!this.TryCreateCompositePipelineLocked())
            {
                this.LastGPUInitializationFailure = "Failed to create WebGPU composite pipeline.";
                Trace("TryInitializeGPULocked: composite pipeline creation failed");
                return false;
            }

            Trace("TryInitializeGPULocked: composite pipeline ready");
            this.coverageRasterizer = new WebGPURasterizer(this.webGPU, this.device, this.queue);
            if (!this.coverageRasterizer.Initialize())
            {
                this.LastGPUInitializationFailure = "Failed to create WebGPU coverage pipeline.";
                Trace("TryInitializeGPULocked: coverage pipeline creation failed");
                return false;
            }

            Trace("TryInitializeGPULocked: coverage pipeline ready");
            return true;
        }
        catch (Exception ex)
        {
            this.LastGPUInitializationFailure = $"WebGPU initialization threw: {ex.Message}";
            Trace($"TryInitializeGPULocked: exception {ex}");
            return false;
        }
        finally
        {
            if (!this.IsGPUReady &&
                (this.compositePipelineLayout is null ||
                 this.compositeBindGroupLayout is null ||
                 this.coverageRasterizer is null ||
                 !this.coverageRasterizer.IsInitialized ||
                 this.device is null ||
                 this.queue is null))
            {
                this.LastGPUInitializationFailure ??= "WebGPU initialization left required resources unavailable.";
                this.ReleaseGPUResourcesLocked();
            }

            Trace($"TryInitializeGPULocked: end ready={this.IsGPUReady} error={this.LastGPUInitializationFailure ?? "<none>"}");
        }
    }

    private bool TryRequestAdapterLocked(out Adapter* resultAdapter)
    {
        resultAdapter = null;
        if (this.webGPU is null || this.instance is null)
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

        this.webGPU.InstanceRequestAdapter(this.instance, in options, callbackPtr, null);
        if (!this.WaitForSignalLocked(callbackReady))
        {
            this.LastGPUInitializationFailure = "Timed out while waiting for WebGPU adapter request callback.";
            Trace("TryRequestAdapterLocked: timeout waiting for callback");
            return false;
        }

        resultAdapter = callbackAdapter;
        if (callbackStatus != RequestAdapterStatus.Success || callbackAdapter is null)
        {
            this.LastGPUInitializationFailure = $"WebGPU adapter request failed with status '{callbackStatus}'.";
            Trace($"TryRequestAdapterLocked: callback status={callbackStatus} adapter={(nint)callbackAdapter:X}");
            return false;
        }

        return true;
    }

    private bool TryRequestDeviceLocked(out Device* resultDevice)
    {
        resultDevice = null;
        if (this.webGPU is null || this.adapter is null)
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
        this.webGPU.AdapterRequestDevice(this.adapter, in descriptor, callbackPtr, null);

        if (!this.WaitForSignalLocked(callbackReady))
        {
            this.LastGPUInitializationFailure = "Timed out while waiting for WebGPU device request callback.";
            Trace("TryRequestDeviceLocked: timeout waiting for callback");
            return false;
        }

        resultDevice = callbackDevice;
        if (callbackStatus != RequestDeviceStatus.Success || callbackDevice is null)
        {
            this.LastGPUInitializationFailure = $"WebGPU device request failed with status '{callbackStatus}'.";
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
        if (this.webGPU is null || this.device is null)
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
                HasDynamicOffset = true,
                MinBindingSize = (ulong)Unsafe.SizeOf<CompositeParams>()
            }
        };

        BindGroupLayoutDescriptor layoutDescriptor = new()
        {
            EntryCount = 2,
            Entries = layoutEntries
        };

        this.compositeBindGroupLayout = this.webGPU.DeviceCreateBindGroupLayout(this.device, in layoutDescriptor);
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

        this.compositePipelineLayout = this.webGPU.DeviceCreatePipelineLayout(this.device, in pipelineLayoutDescriptor);
        if (this.compositePipelineLayout is null)
        {
            return false;
        }

        // Validate that at least the baseline RGBA target format can create a pipeline.
        if (!this.TryGetOrCreateCompositePipelineLocked(TextureFormat.Rgba8Unorm, out _))
        {
            return false;
        }

        // BGRA is optional and can fail on specific adapters/drivers.
        _ = this.TryGetOrCreateCompositePipelineLocked(TextureFormat.Bgra8Unorm, out _);
        return true;
    }

    private bool TryGetOrCreateCompositePipelineLocked(TextureFormat textureFormat, out RenderPipeline* pipeline)
    {
        pipeline = null;
        if (textureFormat == TextureFormat.Undefined ||
            this.webGPU is null ||
            this.device is null ||
            this.compositePipelineLayout is null)
        {
            return false;
        }

        if (this.compositePipelines.TryGetValue(textureFormat, out nint existingPipelineHandle) &&
            existingPipelineHandle != 0)
        {
            pipeline = (RenderPipeline*)existingPipelineHandle;
            return true;
        }

        RenderPipeline* createdPipeline = this.CreateCompositePipelineForFormatLocked(textureFormat);
        if (createdPipeline is null)
        {
            return false;
        }

        nint createdPipelineHandle = (nint)createdPipeline;
        nint cachedPipelineHandle = this.compositePipelines.GetOrAdd(textureFormat, createdPipelineHandle);
        if (cachedPipelineHandle != createdPipelineHandle)
        {
            this.webGPU.RenderPipelineRelease(createdPipeline);
        }

        pipeline = (RenderPipeline*)cachedPipelineHandle;
        return pipeline is not null;
    }

    private RenderPipeline* CreateCompositePipelineForFormatLocked(TextureFormat textureFormat)
    {
        if (this.webGPU is null || this.device is null)
        {
            return null;
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

                shaderModule = this.webGPU.DeviceCreateShaderModule(this.device, in shaderDescriptor);
            }

            if (shaderModule is null)
            {
                return null;
            }

            ReadOnlySpan<byte> vertexEntryPoint = CompositeVertexEntryPoint;
            ReadOnlySpan<byte> fragmentEntryPoint = CompositeFragmentEntryPoint;
            fixed (byte* vertexEntryPointPtr = vertexEntryPoint)
            {
                fixed (byte* fragmentEntryPointPtr = fragmentEntryPoint)
                {
                    return this.CreateCompositePipelineLocked(
                        shaderModule,
                        vertexEntryPointPtr,
                        fragmentEntryPointPtr,
                        textureFormat);
                }
            }
        }
        finally
        {
            if (shaderModule is not null)
            {
                this.webGPU.ShaderModuleRelease(shaderModule);
            }
        }
    }

    private RenderPipeline* CreateCompositePipelineLocked(
        ShaderModule* shaderModule,
        byte* vertexEntryPointPtr,
        byte* fragmentEntryPointPtr,
        TextureFormat textureFormat)
    {
        if (this.webGPU is null || this.device is null || this.compositePipelineLayout is null)
        {
            return null;
        }

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
            Format = textureFormat,
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

        return this.webGPU.DeviceCreateRenderPipeline(this.device, in pipelineDescriptor);
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

            if (this.instance is not null && this.webGPU is not null)
            {
                this.webGPU.InstanceProcessEvents(this.instance);
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

    private bool QueueWriteTextureFromRegionLocked<TPixel>(Texture* destinationTexture, Buffer2DRegion<TPixel> sourceRegion)
        where TPixel : unmanaged
    {
        if (!this.TryGetGPUState(out GPUState gpuState))
        {
            return false;
        }

        WebGPU api = gpuState.Api;
        Queue* queue = gpuState.Queue;
        int pixelSizeInBytes = Unsafe.SizeOf<TPixel>();
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
    private bool BeginCompositeSessionCoreLocked<TPixel>(
        Buffer2DRegion<TPixel> target,
        TextureFormat textureFormat,
        int pixelSizeInBytes)
        where TPixel : unmanaged
    {
        if (!this.EnsureCompositeSessionResourcesLocked(target.Width, target.Height, textureFormat, pixelSizeInBytes) ||
            this.compositeSessionTargetTexture is null)
        {
            return false;
        }

        this.ResetCompositeSessionStateLocked();
        if (!this.QueueWriteTextureFromRegionLocked(this.compositeSessionTargetTexture, target))
        {
            return false;
        }

        this.compositeSessionTargetRectangle = target.Rectangle;
        this.compositeSessionRequiresReadback = true;
        this.compositeSessionOwnsTargetView = true;
        this.compositeSessionUniformWriteOffset = 0;
        this.compositeSessionDirty = false;
        return true;
    }

    private bool BeginCompositeSurfaceSessionCoreLocked<TPixel>(
        ICanvasFrame<TPixel> target,
        WebGPUSurfaceCapability nativeSurfaceCapability)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        if (nativeSurfaceCapability.TargetTextureView == 0 ||
            target.Bounds.Width <= 0 ||
            target.Bounds.Height <= 0)
        {
            return false;
        }

        if (target.Bounds.Right > nativeSurfaceCapability.Width ||
            target.Bounds.Bottom > nativeSurfaceCapability.Height)
        {
            return false;
        }

        if (!this.TryGetOrCreateCompositePipelineLocked(nativeSurfaceCapability.TargetFormat, out _))
        {
            return false;
        }

        this.ResetCompositeSessionStateLocked();
        if (this.compositeSessionOwnsTargetView)
        {
            this.ReleaseTextureViewLocked(this.compositeSessionTargetView);
        }

        this.ReleaseTextureLocked(this.compositeSessionTargetTexture);
        this.compositeSessionTargetTexture = null;
        this.ReleaseBufferLocked(this.compositeSessionReadbackBuffer);
        this.compositeSessionReadbackBuffer = null;
        this.compositeSessionReadbackBytesPerRow = 0;
        this.compositeSessionReadbackByteCount = 0;
        this.compositeSessionResourceWidth = 0;
        this.compositeSessionResourceHeight = 0;
        this.compositeSessionResourceTextureFormat = nativeSurfaceCapability.TargetFormat;
        this.compositeSessionTargetView = (TextureView*)nativeSurfaceCapability.TargetTextureView;
        this.compositeSessionOwnsTargetView = false;
        this.compositeSessionRequiresReadback = false;
        this.compositeSessionTargetRectangle = target.Bounds;
        this.compositeSessionUniformWriteOffset = 0;
        this.compositeSessionDirty = false;
        return this.TryEnsureCompositeSessionUniformBufferLocked();
    }

    private bool EnsureCompositeSessionResourcesLocked(
        int width,
        int height,
        TextureFormat textureFormat,
        int pixelSizeInBytes)
    {
        if (!this.TryGetGPUState(out GPUState gpuState))
        {
            return false;
        }

        if (this.compositeSessionTargetTexture is not null &&
            this.compositeSessionTargetView is not null &&
            this.compositeSessionReadbackBuffer is not null &&
            this.compositeSessionUniformBuffer is not null &&
            this.compositeSessionResourceWidth == width &&
            this.compositeSessionResourceHeight == height &&
            this.compositeSessionResourceTextureFormat == textureFormat)
        {
            return true;
        }

        this.ReleaseCompositeSessionResourcesLocked();

        uint textureRowBytes = checked((uint)width * (uint)pixelSizeInBytes);
        uint readbackRowBytes = AlignTo256(textureRowBytes);
        ulong readbackByteCount = (ulong)readbackRowBytes * (uint)height;

        TextureDescriptor targetTextureDescriptor = new()
        {
            Usage = TextureUsage.RenderAttachment | TextureUsage.CopySrc | TextureUsage.CopyDst,
            Dimension = TextureDimension.Dimension2D,
            Size = new Extent3D((uint)width, (uint)height, 1),
            Format = textureFormat,
            MipLevelCount = 1,
            SampleCount = 1
        };

        Texture* targetTexture = gpuState.Api.DeviceCreateTexture(gpuState.Device, in targetTextureDescriptor);
        if (targetTexture is null)
        {
            return false;
        }

        TextureViewDescriptor targetViewDescriptor = new()
        {
            Format = textureFormat,
            Dimension = TextureViewDimension.Dimension2D,
            BaseMipLevel = 0,
            MipLevelCount = 1,
            BaseArrayLayer = 0,
            ArrayLayerCount = 1,
            Aspect = TextureAspect.All
        };

        TextureView* targetView = gpuState.Api.TextureCreateView(targetTexture, in targetViewDescriptor);
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

        WgpuBuffer* readbackBuffer = gpuState.Api.DeviceCreateBuffer(gpuState.Device, in readbackBufferDescriptor);
        if (readbackBuffer is null)
        {
            this.ReleaseTextureViewLocked(targetView);
            this.ReleaseTextureLocked(targetTexture);
            return false;
        }

        BufferDescriptor uniformBufferDescriptor = new()
        {
            Usage = BufferUsage.Uniform | BufferUsage.CopyDst,
            Size = CompositeUniformBufferSize
        };

        WgpuBuffer* uniformBuffer = gpuState.Api.DeviceCreateBuffer(gpuState.Device, in uniformBufferDescriptor);
        if (uniformBuffer is null)
        {
            this.ReleaseBufferLocked(readbackBuffer);
            this.ReleaseTextureViewLocked(targetView);
            this.ReleaseTextureLocked(targetTexture);
            return false;
        }

        this.compositeSessionTargetTexture = targetTexture;
        this.compositeSessionTargetView = targetView;
        this.compositeSessionReadbackBuffer = readbackBuffer;
        this.compositeSessionUniformBuffer = uniformBuffer;
        this.compositeSessionUniformWriteOffset = 0;
        this.compositeSessionReadbackBytesPerRow = readbackRowBytes;
        this.compositeSessionReadbackByteCount = readbackByteCount;
        this.compositeSessionResourceWidth = width;
        this.compositeSessionResourceHeight = height;
        this.compositeSessionResourceTextureFormat = textureFormat;
        this.compositeSessionRequiresReadback = true;
        this.compositeSessionOwnsTargetView = true;
        return true;
    }

    private bool TryEnsureCompositeSessionUniformBufferLocked()
    {
        if (this.compositeSessionUniformBuffer is not null)
        {
            return true;
        }

        if (!this.TryGetGPUState(out GPUState gpuState))
        {
            return false;
        }

        BufferDescriptor uniformBufferDescriptor = new()
        {
            Usage = BufferUsage.Uniform | BufferUsage.CopyDst,
            Size = CompositeUniformBufferSize
        };

        this.compositeSessionUniformBuffer = gpuState.Api.DeviceCreateBuffer(gpuState.Device, in uniformBufferDescriptor);
        return this.compositeSessionUniformBuffer is not null;
    }

    /// <summary>
    /// Reads the session target texture back into the canvas region.
    /// </summary>
    private bool TryFlushCompositeSessionLocked<TPixel>(Buffer2DRegion<TPixel> target)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        if (!this.TryGetGPUState(out GPUState gpuState))
        {
            return false;
        }

        Trace("TryFlushCompositeSessionLocked: begin");
        int targetWidth = this.compositeSessionTargetRectangle.Width;
        int targetHeight = this.compositeSessionTargetRectangle.Height;
        if (this.compositeSessionTargetTexture is null ||
            this.compositeSessionReadbackBuffer is null ||
            targetWidth <= 0 ||
            targetHeight <= 0 ||
            this.compositeSessionReadbackByteCount == 0 ||
            this.compositeSessionReadbackBytesPerRow == 0)
        {
            return false;
        }

        if (target.Width != targetWidth || target.Height != targetHeight)
        {
            return false;
        }

        CommandEncoder* commandEncoder = this.compositeSessionCommandEncoder;
        bool usingSessionCommandEncoder = commandEncoder is not null;
        CommandBuffer* commandBuffer = null;
        try
        {
            this.TryCloseCompositeSessionPassLocked();

            if (commandEncoder is null)
            {
                CommandEncoderDescriptor commandEncoderDescriptor = default;
                commandEncoder = gpuState.Api.DeviceCreateCommandEncoder(gpuState.Device, in commandEncoderDescriptor);
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
                    RowsPerImage = (uint)targetHeight
                }
            };

            Extent3D copySize = new((uint)targetWidth, (uint)targetHeight, 1);
            gpuState.Api.CommandEncoderCopyTextureToBuffer(commandEncoder, in source, in destination, in copySize);

            CommandBufferDescriptor commandBufferDescriptor = default;
            commandBuffer = gpuState.Api.CommandEncoderFinish(commandEncoder, in commandBufferDescriptor);
            if (commandBuffer is null)
            {
                return false;
            }

            this.compositeSessionCommandEncoder = null;

            gpuState.Api.QueueSubmit(gpuState.Queue, 1, ref commandBuffer);
            gpuState.Api.CommandBufferRelease(commandBuffer);
            commandBuffer = null;

            bool readbackSuccess = this.TryReadBackBufferToRegionLocked(
                this.compositeSessionReadbackBuffer,
                checked((int)this.compositeSessionReadbackBytesPerRow),
                target);

            if (!readbackSuccess)
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
                gpuState.Api.CommandBufferRelease(commandBuffer);
            }

            if (commandEncoder is not null)
            {
                if (this.compositeSessionCommandEncoder == commandEncoder)
                {
                    this.compositeSessionCommandEncoder = null;
                }

                gpuState.Api.CommandEncoderRelease(commandEncoder);
            }
        }
    }

    private bool TrySubmitCompositeSessionLocked()
    {
        if (!this.TryGetGPUState(out GPUState gpuState))
        {
            return false;
        }

        CommandEncoder* commandEncoder = this.compositeSessionCommandEncoder;
        CommandBuffer* commandBuffer = null;
        try
        {
            this.TryCloseCompositeSessionPassLocked();

            if (commandEncoder is null)
            {
                return true;
            }

            CommandBufferDescriptor commandBufferDescriptor = default;
            commandBuffer = gpuState.Api.CommandEncoderFinish(commandEncoder, in commandBufferDescriptor);
            if (commandBuffer is null)
            {
                return false;
            }

            this.compositeSessionCommandEncoder = null;
            gpuState.Api.QueueSubmit(gpuState.Queue, 1, ref commandBuffer);
            gpuState.Api.CommandBufferRelease(commandBuffer);
            commandBuffer = null;
            return true;
        }
        finally
        {
            if (commandBuffer is not null)
            {
                gpuState.Api.CommandBufferRelease(commandBuffer);
            }

            if (commandEncoder is not null)
            {
                gpuState.Api.CommandEncoderRelease(commandEncoder);
            }
        }
    }

    private void ResetCompositeSessionStateLocked()
    {
        this.TryCloseCompositeSessionPassLocked();

        if (this.compositeSessionCommandEncoder is not null && this.webGPU is not null)
        {
            this.webGPU.CommandEncoderRelease(this.compositeSessionCommandEncoder);
            this.compositeSessionCommandEncoder = null;
        }

        this.compositeSessionTargetRectangle = default;
        this.compositeSessionRequiresReadback = false;
        this.compositeSessionDirty = false;
        this.compositeSessionCommands.Clear();
    }

    private void ReleaseCompositeSessionResourcesLocked()
    {
        if (this.compositeSessionPassEncoder is not null && this.webGPU is not null)
        {
            this.webGPU.RenderPassEncoderRelease(this.compositeSessionPassEncoder);
            this.compositeSessionPassEncoder = null;
        }

        if (this.compositeSessionCommandEncoder is not null && this.webGPU is not null)
        {
            this.webGPU.CommandEncoderRelease(this.compositeSessionCommandEncoder);
            this.compositeSessionCommandEncoder = null;
        }

        this.ReleaseAllCoverageCompositeBindGroupsLocked();
        this.ReleaseBufferLocked(this.compositeSessionUniformBuffer);
        this.ReleaseBufferLocked(this.compositeSessionReadbackBuffer);
        if (this.compositeSessionOwnsTargetView)
        {
            this.ReleaseTextureViewLocked(this.compositeSessionTargetView);
        }

        this.ReleaseTextureLocked(this.compositeSessionTargetTexture);
        this.compositeSessionUniformBuffer = null;
        this.compositeSessionUniformWriteOffset = 0;
        this.compositeSessionReadbackBuffer = null;
        this.compositeSessionTargetTexture = null;
        this.compositeSessionTargetView = null;
        this.compositeSessionRequiresReadback = false;
        this.compositeSessionOwnsTargetView = false;
        this.compositeSessionReadbackBytesPerRow = 0;
        this.compositeSessionReadbackByteCount = 0;
        this.compositeSessionResourceWidth = 0;
        this.compositeSessionResourceHeight = 0;
        this.compositeSessionResourceTextureFormat = TextureFormat.Undefined;
        this.compositeSessionCommands.Clear();
    }

    private bool TryCompositeCoverageGPU<TPixel>(
        ICanvasFrame<TPixel> target,
        DrawingCoverageHandle coverageHandle,
        Point sourceOffset,
        WebGPUBrushData brushData,
        float blendPercentage)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        if (!this.preparedCoverage.TryGetValue(coverageHandle.Value, out CoverageEntry? entry))
        {
            throw new InvalidOperationException($"Prepared coverage handle '{coverageHandle.Value}' is not valid.");
        }

        if (target.Bounds.Width <= 0 || target.Bounds.Height <= 0)
        {
            return true;
        }

        if ((uint)sourceOffset.X >= (uint)entry.Width || (uint)sourceOffset.Y >= (uint)entry.Height)
        {
            return true;
        }

        int compositeWidth = Math.Min(target.Bounds.Width, entry.Width - sourceOffset.X);
        int compositeHeight = Math.Min(target.Bounds.Height, entry.Height - sourceOffset.Y);
        if (compositeWidth <= 0 || compositeHeight <= 0)
        {
            return true;
        }

        lock (this.gpuSync)
        {
            if (!this.compositeSessionGPUActive ||
                this.compositeSessionDepth <= 0 ||
                this.compositeSessionTargetView is null)
            {
                return false;
            }

            if (!TryEnsureCoverageTextureLocked(entry))
            {
                return false;
            }

            int sessionTargetWidth = this.compositeSessionTargetRectangle.Width;
            int sessionTargetHeight = this.compositeSessionTargetRectangle.Height;
            int destinationX = target.Bounds.X - this.compositeSessionTargetRectangle.X;
            int destinationY = target.Bounds.Y - this.compositeSessionTargetRectangle.Y;
            if ((uint)destinationX >= (uint)sessionTargetWidth ||
                (uint)destinationY >= (uint)sessionTargetHeight)
            {
                return false;
            }

            int sessionCompositeWidth = Math.Min(compositeWidth, sessionTargetWidth - destinationX);
            int sessionCompositeHeight = Math.Min(compositeHeight, sessionTargetHeight - destinationY);
            if (sessionCompositeWidth <= 0 || sessionCompositeHeight <= 0)
            {
                return true;
            }

            this.compositeSessionCommands.Add(new GPUCompositeCommand(
                coverageHandle.Value,
                sourceOffset,
                brushData,
                blendPercentage,
                destinationX,
                destinationY,
                sessionCompositeWidth,
                sessionCompositeHeight));
            this.compositeSessionDirty = true;
            return true;
        }
    }

    private bool TryDrainQueuedCompositeCommandsLocked()
    {
        if (!this.compositeSessionGPUActive || this.compositeSessionCommands.Count == 0)
        {
            return true;
        }

        if (!this.TryEnsureCompositeSessionCommandEncoderLocked())
        {
            return false;
        }

        RenderPipeline* compositePipeline = this.GetCompositeSessionPipelineLocked();
        if (compositePipeline is null || this.compositeSessionTargetView is null)
        {
            return false;
        }

        int sessionTargetWidth = this.compositeSessionTargetRectangle.Width;
        int sessionTargetHeight = this.compositeSessionTargetRectangle.Height;

        for (int i = 0; i < this.compositeSessionCommands.Count; i++)
        {
            GPUCompositeCommand command = this.compositeSessionCommands[i];
            if (!this.preparedCoverage.TryGetValue(command.CoverageHandleValue, out CoverageEntry? entry) ||
                !TryEnsureCoverageTextureLocked(entry))
            {
                return false;
            }

            if (!this.TryRunCompositePassLocked(
                this.compositeSessionCommandEncoder,
                compositePipeline,
                entry,
                command.SourceOffset,
                command.BrushData,
                command.BlendPercentage,
                this.compositeSessionTargetView,
                sessionTargetWidth,
                sessionTargetHeight,
                command.DestinationX,
                command.DestinationY,
                command.CompositeWidth,
                command.CompositeHeight))
            {
                return false;
            }
        }

        this.compositeSessionCommands.Clear();
        return true;
    }

    private bool TryEnsureCompositeSessionCommandEncoderLocked()
    {
        if (this.compositeSessionCommandEncoder is not null)
        {
            return true;
        }

        if (!this.TryGetGPUState(out GPUState gpuState))
        {
            return false;
        }

        CommandEncoderDescriptor commandEncoderDescriptor = default;
        this.compositeSessionCommandEncoder = gpuState.Api.DeviceCreateCommandEncoder(gpuState.Device, in commandEncoderDescriptor);
        return this.compositeSessionCommandEncoder is not null;
    }

    private void TryCloseCompositeSessionPassLocked()
    {
        if (this.compositeSessionPassEncoder is null)
        {
            return;
        }

        if (!this.TryGetGPUState(out GPUState gpuState))
        {
            return;
        }

        gpuState.Api.RenderPassEncoderEnd(this.compositeSessionPassEncoder);
        gpuState.Api.RenderPassEncoderRelease(this.compositeSessionPassEncoder);
        this.compositeSessionPassEncoder = null;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private RenderPipeline* GetCompositeSessionPipelineLocked()
    {
        if (this.compositeSessionResourceTextureFormat == TextureFormat.Undefined)
        {
            return null;
        }

        return this.TryGetOrCreateCompositePipelineLocked(this.compositeSessionResourceTextureFormat, out RenderPipeline* pipeline)
            ? pipeline
            : null;
    }

    private static bool TryEnsureCoverageTextureLocked(CoverageEntry entry)
    {
        if (entry.GPUCoverageTexture is not null && entry.GPUCoverageView is not null)
        {
            return true;
        }

        return false;
    }

    private BindGroup* GetOrCreateCoverageBindGroupLocked(
        CoverageEntry coverageEntry,
        WgpuBuffer* uniformBuffer,
        uint uniformDataSize)
    {
        if (!this.TryGetGPUState(out GPUState gpuState))
        {
            return null;
        }

        if (this.compositeBindGroupLayout is null ||
            coverageEntry.GPUCoverageView is null ||
            uniformBuffer is null ||
            uniformDataSize == 0)
        {
            return null;
        }

        if (coverageEntry.GPUCompositeBindGroup is not null &&
            coverageEntry.GPUCompositeUniformBuffer == uniformBuffer)
        {
            return coverageEntry.GPUCompositeBindGroup;
        }

        this.ReleaseCoverageCompositeBindGroupLocked(coverageEntry);

        BindGroupEntry* bindGroupEntries = stackalloc BindGroupEntry[2];
        bindGroupEntries[0] = new BindGroupEntry
        {
            Binding = 0,
            TextureView = coverageEntry.GPUCoverageView
        };
        bindGroupEntries[1] = new BindGroupEntry
        {
            Binding = 1,
            Buffer = uniformBuffer,
            Offset = 0,
            Size = uniformDataSize
        };

        BindGroupDescriptor bindGroupDescriptor = new()
        {
            Layout = this.compositeBindGroupLayout,
            EntryCount = 2,
            Entries = bindGroupEntries
        };

        BindGroup* bindGroup = gpuState.Api.DeviceCreateBindGroup(gpuState.Device, in bindGroupDescriptor);
        if (bindGroup is null)
        {
            return null;
        }

        coverageEntry.GPUCompositeBindGroup = bindGroup;
        coverageEntry.GPUCompositeUniformBuffer = uniformBuffer;
        return bindGroup;
    }

    /// <summary>
    /// Executes one composition draw call into the session target texture.
    /// </summary>
    private bool TryRunCompositePassLocked(
        CommandEncoder* commandEncoder,
        RenderPipeline* compositePipeline,
        CoverageEntry coverageEntry,
        Point sourceOffset,
        WebGPUBrushData brushData,
        float blendPercentage,
        TextureView* targetView,
        int targetWidth,
        int targetHeight,
        int destinationX,
        int destinationY,
        int compositeWidth,
        int compositeHeight)
    {
        if (!this.TryGetGPUState(out GPUState gpuState))
        {
            return false;
        }

        if (compositePipeline is null ||
            this.compositeBindGroupLayout is null ||
            coverageEntry.GPUCoverageView is null ||
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

        if (this.compositeSessionUniformBuffer is null)
        {
            return false;
        }

        uint uniformDataSize = (uint)Unsafe.SizeOf<CompositeParams>();
        uint uniformStride = AlignTo256(uniformDataSize);
        if (uniformStride == 0 ||
            this.compositeSessionUniformWriteOffset > CompositeUniformBufferSize ||
            this.compositeSessionUniformWriteOffset + uniformStride > CompositeUniformBufferSize)
        {
            return false;
        }

        uint uniformOffset = this.compositeSessionUniformWriteOffset;
        this.compositeSessionUniformWriteOffset += uniformStride;

        BindGroup* bindGroup = this.GetOrCreateCoverageBindGroupLocked(coverageEntry, this.compositeSessionUniformBuffer, uniformDataSize);
        if (bindGroup is null)
        {
            return false;
        }

        if (commandEncoder is null)
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

        gpuState.Api.QueueWriteBuffer(
            gpuState.Queue,
            this.compositeSessionUniformBuffer,
            uniformOffset,
            ref parameters,
            (nuint)Unsafe.SizeOf<CompositeParams>());

        if (this.compositeSessionPassEncoder is null)
        {
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

            this.compositeSessionPassEncoder = gpuState.Api.CommandEncoderBeginRenderPass(commandEncoder, in renderPassDescriptor);
            if (this.compositeSessionPassEncoder is null)
            {
                return false;
            }
        }

        uint dynamicOffset = uniformOffset;
        uint* dynamicOffsets = &dynamicOffset;

        gpuState.Api.RenderPassEncoderSetPipeline(this.compositeSessionPassEncoder, compositePipeline);
        gpuState.Api.RenderPassEncoderSetBindGroup(this.compositeSessionPassEncoder, 0, bindGroup, 1, dynamicOffsets);
        gpuState.Api.RenderPassEncoderDraw(this.compositeSessionPassEncoder, CompositeVertexCount, 1, 0, 0);
        return true;
    }

    private bool TryMapReadBufferLocked(WgpuBuffer* readbackBuffer, nuint byteCount, out byte* mappedData)
    {
        mappedData = null;

        if (!this.TryGetGPUState(out GPUState gpuState))
        {
            return false;
        }

        if (readbackBuffer is null)
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
        gpuState.Api.BufferMapAsync(readbackBuffer, MapMode.Read, 0, byteCount, callbackPtr, null);

        if (!this.WaitForSignalLocked(callbackReady) || mapStatus != BufferMapAsyncStatus.Success)
        {
            Trace($"TryReadBackBufferLocked: map failed status={mapStatus}");
            return false;
        }

        Trace("TryReadBackBufferLocked: map callback success");
        void* rawMappedData = gpuState.Api.BufferGetConstMappedRange(readbackBuffer, 0, byteCount);
        if (rawMappedData is null)
        {
            gpuState.Api.BufferUnmap(readbackBuffer);
            Trace("TryReadBackBufferLocked: mapped range null");
            return false;
        }

        mappedData = (byte*)rawMappedData;
        return true;
    }

    private bool TryReadBackBufferToRegionLocked<TPixel>(
        WgpuBuffer* readbackBuffer,
        int sourceRowBytes,
        Buffer2DRegion<TPixel> destinationRegion)
        where TPixel : unmanaged
    {
        if (destinationRegion.Width <= 0 || destinationRegion.Height <= 0)
        {
            return true;
        }

        int destinationRowBytes = checked(destinationRegion.Width * Unsafe.SizeOf<TPixel>());
        int readbackByteCount = checked(sourceRowBytes * destinationRegion.Height);
        if (!this.TryMapReadBufferLocked(readbackBuffer, (nuint)readbackByteCount, out byte* mappedData))
        {
            return false;
        }

        try
        {
            ReadOnlySpan<byte> sourceData = new(mappedData, readbackByteCount);
            int destinationStrideBytes = checked(destinationRegion.Buffer.Width * Unsafe.SizeOf<TPixel>());

            // If the target region spans full rows in a contiguous backing buffer we can copy
            // the mapped data in one block instead of per-row.
            if (destinationRegion.Rectangle.X == 0 &&
                sourceRowBytes == destinationStrideBytes &&
                TryGetSingleMemory(destinationRegion.Buffer, out Memory<TPixel> contiguousDestination))
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
                MemoryMarshal.Cast<byte, TPixel>(sourceRow).CopyTo(destinationRegion.DangerousGetRowSpan(y));
            }

            return true;
        }
        finally
        {
            if (this.TryGetGPUState(out GPUState gpuState))
            {
                gpuState.Api.BufferUnmap(readbackBuffer);
            }

            Trace("TryReadBackBufferLocked: completed");
        }
    }

    private void ReleaseCoverageTextureLocked(CoverageEntry entry)
    {
        this.ReleaseCoverageCompositeBindGroupLocked(entry);
        Trace($"ReleaseCoverageTextureLocked: tex={(nint)entry.GPUCoverageTexture:X} view={(nint)entry.GPUCoverageView:X}");
        this.ReleaseTextureViewLocked(entry.GPUCoverageView);
        this.ReleaseTextureLocked(entry.GPUCoverageTexture);
        entry.GPUCoverageView = null;
        entry.GPUCoverageTexture = null;
    }

    private void ReleaseCoverageCompositeBindGroupLocked(CoverageEntry entry)
    {
        if (entry.GPUCompositeBindGroup is not null && this.TryGetGPUState(out GPUState gpuState))
        {
            gpuState.Api.BindGroupRelease(entry.GPUCompositeBindGroup);
        }

        entry.GPUCompositeBindGroup = null;
        entry.GPUCompositeUniformBuffer = null;
    }

    private void ReleaseAllCoverageCompositeBindGroupsLocked()
    {
        foreach (KeyValuePair<int, CoverageEntry> kv in this.preparedCoverage)
        {
            this.ReleaseCoverageCompositeBindGroupLocked(kv.Value);
        }
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryGetGPUState(out GPUState state)
    {
        if (this.webGPU is null || this.device is null || this.queue is null)
        {
            state = default;
            return false;
        }

        state = new GPUState(this.webGPU, this.device, this.queue);
        return true;
    }

    private void ReleaseTextureViewLocked(TextureView* textureView)
    {
        if (textureView is null || !this.TryGetGPUState(out GPUState gpuState))
        {
            return;
        }

        gpuState.Api.TextureViewRelease(textureView);
    }

    private void ReleaseTextureLocked(Texture* texture)
    {
        if (texture is null || !this.TryGetGPUState(out GPUState gpuState))
        {
            return;
        }

        gpuState.Api.TextureRelease(texture);
    }

    private void ReleaseBufferLocked(WgpuBuffer* buffer)
    {
        if (buffer is null || !this.TryGetGPUState(out GPUState gpuState))
        {
            return;
        }

        gpuState.Api.BufferRelease(buffer);
    }

    private void TryDestroyAndDrainDeviceLocked()
    {
        if (this.webGPU is null || this.device is null)
        {
            return;
        }

        this.webGPU.DeviceDestroy(this.device);

        if (this.wgpuExtension is not null)
        {
            // Drain native callbacks/work queues before releasing the device and unloading.
            _ = this.wgpuExtension.DevicePoll(this.device, true, (WrappedSubmissionIndex*)null);
            _ = this.wgpuExtension.DevicePoll(this.device, true, (WrappedSubmissionIndex*)null);
            return;
        }

        if (this.instance is not null)
        {
            this.webGPU.InstanceProcessEvents(this.instance);
            this.webGPU.InstanceProcessEvents(this.instance);
        }
    }

    private void ReleaseGPUResourcesLocked()
    {
        Trace("ReleaseGPUResourcesLocked: begin");
        this.ResetCompositeSessionStateLocked();
        this.ReleaseCompositeSessionResourcesLocked();

        if (this.webGPU is not null)
        {
            this.coverageRasterizer?.Release();
            this.coverageRasterizer = null;

            foreach (KeyValuePair<TextureFormat, nint> compositePipelineEntry in this.compositePipelines)
            {
                if (compositePipelineEntry.Value != 0)
                {
                    this.webGPU.RenderPipelineRelease((RenderPipeline*)compositePipelineEntry.Value);
                }
            }

            this.compositePipelines.Clear();

            if (this.compositePipelineLayout is not null)
            {
                this.webGPU.PipelineLayoutRelease(this.compositePipelineLayout);
                this.compositePipelineLayout = null;
            }

            if (this.compositeBindGroupLayout is not null)
            {
                this.webGPU.BindGroupLayoutRelease(this.compositeBindGroupLayout);
                this.compositeBindGroupLayout = null;
            }

            if (this.device is not null)
            {
                this.TryDestroyAndDrainDeviceLocked();
            }

            if (this.queue is not null)
            {
                this.webGPU.QueueRelease(this.queue);
                this.queue = null;
            }

            if (this.device is not null)
            {
                this.webGPU.DeviceRelease(this.device);
                this.device = null;
            }

            if (this.adapter is not null)
            {
                this.webGPU.AdapterRelease(this.adapter);
                this.adapter = null;
            }

            if (this.instance is not null)
            {
                this.webGPU.InstanceRelease(this.instance);
                this.instance = null;
            }

            this.webGPU = null;
        }

        this.wgpuExtension = null;
        this.runtimeLease?.Dispose();
        this.runtimeLease = null;
        this.IsGPUReady = false;
        this.compositeSessionGPUActive = false;
        this.compositeSessionDepth = 0;
        Trace("ReleaseGPUResourcesLocked: end");
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
    private readonly struct GPUCompositeCommand
    {
        public GPUCompositeCommand(
            int coverageHandleValue,
            Point sourceOffset,
            WebGPUBrushData brushData,
            float blendPercentage,
            int destinationX,
            int destinationY,
            int compositeWidth,
            int compositeHeight)
        {
            this.CoverageHandleValue = coverageHandleValue;
            this.SourceOffset = sourceOffset;
            this.BrushData = brushData;
            this.BlendPercentage = blendPercentage;
            this.DestinationX = destinationX;
            this.DestinationY = destinationY;
            this.CompositeWidth = compositeWidth;
            this.CompositeHeight = compositeHeight;
        }

        public int CoverageHandleValue { get; }

        public Point SourceOffset { get; }

        public WebGPUBrushData BrushData { get; }

        public float BlendPercentage { get; }

        public int DestinationX { get; }

        public int DestinationY { get; }

        public int CompositeWidth { get; }

        public int CompositeHeight { get; }
    }

    private readonly struct GPUState
    {
        public GPUState(WebGPU api, Device* device, Queue* queue)
        {
            this.Api = api;
            this.Device = device;
            this.Queue = queue;
        }

        public WebGPU Api { get; }

        public Device* Device { get; }

        public Queue* Queue { get; }
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

        public Texture* GPUCoverageTexture { get; set; }

        public TextureView* GPUCoverageView { get; set; }

        public BindGroup* GPUCompositeBindGroup { get; set; }

        public WgpuBuffer* GPUCompositeUniformBuffer { get; set; }

        public void Dispose()
        {
        }
    }
}
