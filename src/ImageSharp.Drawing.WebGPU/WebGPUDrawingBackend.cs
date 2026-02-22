// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
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
    private const nuint CompositeInstanceBufferSize = 256 * 1024;
    private const int CallbackTimeoutMilliseconds = 10_000;

    private static ReadOnlySpan<byte> CompositeVertexEntryPoint => "vs_main\0"u8;

    private static ReadOnlySpan<byte> CompositeFragmentEntryPoint => "fs_main\0"u8;

    private readonly object gpuSync = new();
    private readonly ConcurrentDictionary<int, CoverageEntry> coverageCache = new();
    private readonly DefaultDrawingBackend fallbackBackend;
    private WebGPURasterizer? coverageRasterizer;

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
    private WgpuBuffer* compositeSessionInstanceBuffer;
    private nuint compositeSessionInstanceBufferCapacity;
    private CompositeInstanceData[]? compositeSessionInstanceScratch;
    private CommandEncoder* compositeSessionCommandEncoder;
    private uint compositeSessionReadbackBytesPerRow;
    private ulong compositeSessionReadbackByteCount;
    private int compositeSessionResourceWidth;
    private int compositeSessionResourceHeight;
    private TextureFormat compositeSessionResourceTextureFormat;
    private bool compositeSessionRequiresReadback;
    private bool compositeSessionOwnsTargetView;
    private int liveCoverageCount;
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
    /// Gets the number of completed prepared-coverage uses.
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
    /// Gets the number of live per-flush prepared coverage handles.
    /// </summary>
    public int LiveCoverageCount => this.liveCoverageCount;

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

        if (!this.TryBeginGPUFlush(target, out bool openedCompositeSession))
        {
            this.FlushCompositionsFallback(configuration, target, compositionBatch);
            return;
        }

        CompositionCoverageDefinition definition = compositionBatch.Definition;
        RasterizerOptions rasterizerOptions = definition.RasterizerOptions;
        CoverageEntry? coverageEntry = this.PrepareCoverageEntry(
            definition.Path,
            in rasterizerOptions);
        if (coverageEntry is null)
        {
            if (openedCompositeSession)
            {
                this.EndCompositeSession(configuration, target);
            }

            this.FlushCompositionsFallback(configuration, target, compositionBatch);
            return;
        }

        this.liveCoverageCount++;
        this.ReleaseCoverageCallCount++;
        try
        {
            IReadOnlyList<PreparedCompositionCommand> commands = compositionBatch.Commands;
            Rectangle targetBounds = target.Bounds;
            lock (this.gpuSync)
            {
                this.compositeSessionCommands.EnsureCapacity(this.compositeSessionCommands.Count + commands.Count);
                for (int i = 0; i < commands.Count; i++)
                {
                    PreparedCompositionCommand command = commands[i];
                    this.CompositeCoverageCallCount++;

                    if (!WebGPUBrushData.TryCreate(command.Brush, command.BrushBounds, out WebGPUBrushData brushData))
                    {
                        throw new InvalidOperationException("Unsupported brush for WebGPU composition.");
                    }

                    this.QueueCompositeCoverageLocked(
                        coverageEntry,
                        targetBounds,
                        command.DestinationRegion,
                        command.SourceOffset,
                        brushData,
                        command.GraphicsOptions.BlendPercentage);

                    this.GPUCompositeCoverageCallCount++;
                }
            }
        }
        finally
        {
            this.liveCoverageCount--;

            if (openedCompositeSession)
            {
                this.EndCompositeSession(configuration, target);
            }
        }
    }

    private void FlushCompositionsFallback<TPixel>(
        Configuration configuration,
        ICanvasFrame<TPixel> target,
        CompositionBatch compositionBatch)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        this.PrepareCoverageCallCount++;
        this.FallbackPrepareCoverageCallCount++;
        this.ReleaseCoverageCallCount++;
        this.CompositeCoverageCallCount += compositionBatch.Commands.Count;
        this.FallbackCompositeCoverageCallCount += compositionBatch.Commands.Count;

        if (target.TryGetCpuRegion(out _))
        {
            this.fallbackBackend.FlushCompositions(configuration, target, compositionBatch);
            return;
        }

        if (!TryGetNativeSurfaceCapability(
                target,
                expectedTargetFormat: null,
                requireWritableTexture: true,
                out WebGPUSurfaceCapability? surfaceCapability))
        {
            throw new NotSupportedException(
                "Fallback composition requires either a CPU destination region or a native WebGPU surface exposing a writable texture handle.");
        }

        Rectangle targetBounds = target.Bounds;
        using Buffer2D<TPixel> stagingBuffer = configuration.MemoryAllocator.Allocate2D<TPixel>(
            new Size(targetBounds.Width, targetBounds.Height),
            AllocationOptions.Clean);
        Buffer2DRegion<TPixel> stagingRegion = new(stagingBuffer, targetBounds);
        ICanvasFrame<TPixel> stagingFrame = new CpuCanvasFrame<TPixel>(stagingRegion);
        this.fallbackBackend.FlushCompositions(configuration, stagingFrame, compositionBatch);

        lock (this.gpuSync)
        {
            if (!this.QueueWriteTextureFromRegionLocked((Texture*)surfaceCapability.TargetTexture, stagingRegion))
            {
                throw new NotSupportedException(
                    "Fallback composition could not upload to the native WebGPU target texture.");
            }
        }
    }

    private bool TryBeginGPUFlush<TPixel>(ICanvasFrame<TPixel> target, out bool openedCompositeSession)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        openedCompositeSession = false;
        if (this.compositeSessionDepth > 0)
        {
            return this.compositeSessionGPUActive;
        }

        if (!this.IsGPUReady ||
            !CompositePixelHandlers.TryGetValue(typeof(TPixel), out CompositePixelRegistration pixelHandler))
        {
            return false;
        }

        lock (this.gpuSync)
        {
            if (!this.TryGetOrCreateCompositePipelineLocked(pixelHandler.TextureFormat, out _))
            {
                return false;
            }
        }

        this.compositeSessionDepth = 1;
        this.compositeSessionGPUActive = false;
        this.compositeSessionDirty = false;
        this.compositeSessionCommands.Clear();
        if (!this.ActivateCompositeSession(target, pixelHandler))
        {
            this.compositeSessionDepth = 0;
            this.compositeSessionGPUActive = false;
            this.compositeSessionDirty = false;
            this.compositeSessionCommands.Clear();
            return false;
        }

        openedCompositeSession = true;
        return true;
    }

    private CoverageEntry? PrepareCoverageEntry(
        IPath path,
        in RasterizerOptions rasterizerOptions)
    {
        this.ThrowIfDisposed();
        Guard.NotNull(path, nameof(path));

        this.PrepareCoverageCallCount++;
        int definitionKey = CompositionCommand.ComputeCoverageDefinitionKey(path, in rasterizerOptions);
        CoverageEntry? entry = this.GetOrCreateCoverageEntry(definitionKey, path, in rasterizerOptions);
        if (entry is null)
        {
            this.FallbackPrepareCoverageCallCount++;
            return null;
        }

        this.GPUPrepareCoverageCallCount++;
        return entry;
    }

    private CoverageEntry? GetOrCreateCoverageEntry(
        int definitionKey,
        IPath path,
        in RasterizerOptions rasterizerOptions)
    {
        if (this.coverageCache.TryGetValue(definitionKey, out CoverageEntry? cached))
        {
            return cached;
        }

        CoverageEntry? created = this.CreateCoverageEntry(path, in rasterizerOptions);
        if (created is null)
        {
            return null;
        }

        CoverageEntry winner = this.coverageCache.GetOrAdd(definitionKey, created);
        if (!ReferenceEquals(winner, created))
        {
            lock (this.gpuSync)
            {
                this.ReleaseCoverageTextureLocked(created);
            }

            created.Dispose();
        }

        return winner;
    }

    private CoverageEntry? CreateCoverageEntry(IPath path, in RasterizerOptions rasterizerOptions)
    {
        Texture* coverageTexture = null;
        TextureView* coverageView = null;
        lock (this.gpuSync)
        {
            WebGPURasterizer? rasterizer = this.coverageRasterizer;
            if (rasterizer is null ||
                !rasterizer.TryCreateCoverageTexture(path, in rasterizerOptions, out coverageTexture, out coverageView))
            {
                return null;
            }
        }

        Size size = rasterizerOptions.Interest.Size;
        return new CoverageEntry(size.Width, size.Height)
        {
            GPUCoverageTexture = coverageTexture,
            GPUCoverageView = coverageView
        };
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
            else if (TryGetNativeSurfaceCapability(
                         target,
                         expectedTargetFormat: pixelHandler.TextureFormat,
                         requireWritableTexture: false,
                         out WebGPUSurfaceCapability? nativeSurfaceCapability) &&
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
        TextureFormat? expectedTargetFormat,
        bool requireWritableTexture,
        [NotNullWhen(true)] out WebGPUSurfaceCapability? capability)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        if (!target.TryGetNativeSurface(out NativeSurface? nativeSurface) ||
            !nativeSurface.TryGetCapability(out WebGPUSurfaceCapability? surfaceCapability))
        {
            capability = null;
            return false;
        }

        if (expectedTargetFormat is TextureFormat requiredFormat)
        {
            if (surfaceCapability.TargetTextureView == 0 ||
                surfaceCapability.TargetFormat != requiredFormat)
            {
                capability = null;
                return false;
            }
        }

        if (requireWritableTexture && surfaceCapability.TargetTexture == 0)
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
                Type = BufferBindingType.ReadOnlyStorage,
                HasDynamicOffset = false,
                MinBindingSize = (ulong)Unsafe.SizeOf<CompositeInstanceData>()
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
        this.compositeSessionDirty = false;
        return true;
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
            this.compositeSessionResourceWidth == width &&
            this.compositeSessionResourceHeight == height &&
            this.compositeSessionResourceTextureFormat == textureFormat)
        {
            return this.TryEnsureCompositeSessionInstanceBufferCapacityLocked(
                in gpuState,
                (nuint)Unsafe.SizeOf<CompositeInstanceData>());
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

        this.compositeSessionTargetTexture = targetTexture;
        this.compositeSessionTargetView = targetView;
        this.compositeSessionReadbackBuffer = readbackBuffer;
        this.compositeSessionReadbackBytesPerRow = readbackRowBytes;
        this.compositeSessionReadbackByteCount = readbackByteCount;
        this.compositeSessionResourceWidth = width;
        this.compositeSessionResourceHeight = height;
        this.compositeSessionResourceTextureFormat = textureFormat;
        this.compositeSessionRequiresReadback = true;
        this.compositeSessionOwnsTargetView = true;
        return this.TryEnsureCompositeSessionInstanceBufferCapacityLocked(
            in gpuState,
            (nuint)Unsafe.SizeOf<CompositeInstanceData>());
    }

    private bool TryEnsureCompositeSessionInstanceBufferCapacityLocked(in GPUState gpuState, nuint requiredBytes)
    {
        if (requiredBytes == 0)
        {
            return true;
        }

        if (this.compositeSessionInstanceBuffer is not null &&
            this.compositeSessionInstanceBufferCapacity >= requiredBytes)
        {
            return true;
        }

        this.ReleaseAllCoverageCompositeBindGroupsLocked();
        this.ReleaseBufferLocked(this.compositeSessionInstanceBuffer);

        nuint targetSize = requiredBytes > CompositeInstanceBufferSize
            ? requiredBytes
            : CompositeInstanceBufferSize;

        BufferDescriptor instanceBufferDescriptor = new()
        {
            Usage = BufferUsage.Storage | BufferUsage.CopyDst,
            Size = targetSize
        };

        this.compositeSessionInstanceBuffer = gpuState.Api.DeviceCreateBuffer(gpuState.Device, in instanceBufferDescriptor);
        if (this.compositeSessionInstanceBuffer is null)
        {
            this.compositeSessionInstanceBufferCapacity = 0;
            return false;
        }

        this.compositeSessionInstanceBufferCapacity = targetSize;
        return true;
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
        this.ReleaseBufferLocked(this.compositeSessionInstanceBuffer);
        this.ReleaseBufferLocked(this.compositeSessionReadbackBuffer);
        if (this.compositeSessionOwnsTargetView)
        {
            this.ReleaseTextureViewLocked(this.compositeSessionTargetView);
        }

        this.ReleaseTextureLocked(this.compositeSessionTargetTexture);
        this.compositeSessionInstanceBuffer = null;
        this.compositeSessionInstanceBufferCapacity = 0;
        this.compositeSessionInstanceScratch = null;
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

    private void QueueCompositeCoverageLocked(
        CoverageEntry entry,
        in Rectangle targetBounds,
        in Rectangle destinationRegion,
        Point sourceOffset,
        WebGPUBrushData brushData,
        float blendPercentage)
    {
        int destinationX = targetBounds.X + destinationRegion.X - this.compositeSessionTargetRectangle.X;
        int destinationY = targetBounds.Y + destinationRegion.Y - this.compositeSessionTargetRectangle.Y;

        this.compositeSessionCommands.Add(new GPUCompositeCommand(
            entry,
            sourceOffset,
            brushData,
            blendPercentage,
            destinationX,
            destinationY,
            destinationRegion.Width,
            destinationRegion.Height));
        this.compositeSessionDirty = true;
    }

    private bool TryDrainQueuedCompositeCommandsLocked()
    {
        if (!this.compositeSessionGPUActive || this.compositeSessionCommands.Count == 0)
        {
            return true;
        }

        if (!this.TryGetGPUState(out GPUState gpuState))
        {
            return false;
        }

        if (!this.TryEnsureCompositeSessionCommandEncoderLocked(in gpuState))
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

        int i = 0;
        while (i < this.compositeSessionCommands.Count)
        {
            GPUCompositeCommand firstCommand = this.compositeSessionCommands[i];
            CoverageEntry entry = firstCommand.Coverage;

            int runStart = i;
            i++;
            while (i < this.compositeSessionCommands.Count &&
                   ReferenceEquals(this.compositeSessionCommands[i].Coverage, entry))
            {
                i++;
            }

            int runCount = i - runStart;
            nuint instanceDataSize = (nuint)(runCount * Unsafe.SizeOf<CompositeInstanceData>());
            if (!this.TryEnsureCompositeSessionInstanceBufferCapacityLocked(in gpuState, instanceDataSize))
            {
                return false;
            }

            Span<CompositeInstanceData> instances = this.GetCompositeInstanceScratch(runCount);
            for (int instanceIndex = 0; instanceIndex < runCount; instanceIndex++)
            {
                GPUCompositeCommand command = this.compositeSessionCommands[runStart + instanceIndex];
                instances[instanceIndex] = new CompositeInstanceData
                {
                    SourceOffsetX = (uint)command.SourceOffset.X,
                    SourceOffsetY = (uint)command.SourceOffset.Y,
                    DestinationX = (uint)command.DestinationX,
                    DestinationY = (uint)command.DestinationY,
                    DestinationWidth = (uint)command.CompositeWidth,
                    DestinationHeight = (uint)command.CompositeHeight,
                    TargetWidth = (uint)sessionTargetWidth,
                    TargetHeight = (uint)sessionTargetHeight,
                    BrushKind = (uint)command.BrushData.Kind,
                    SolidBrushColor = command.BrushData.SolidColor,
                    BlendPercentage = command.BlendPercentage
                };
            }

            fixed (CompositeInstanceData* instancePtr = instances)
            {
                gpuState.Api.QueueWriteBuffer(
                    gpuState.Queue,
                    this.compositeSessionInstanceBuffer,
                    0,
                    instancePtr,
                    instanceDataSize);
            }

            if (!this.TryRunCompositePassLocked(
                in gpuState,
                this.compositeSessionCommandEncoder,
                compositePipeline,
                entry,
                this.compositeSessionTargetView,
                (uint)runCount))
            {
                return false;
            }
        }

        this.compositeSessionCommands.Clear();
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Span<CompositeInstanceData> GetCompositeInstanceScratch(int count)
    {
        if (this.compositeSessionInstanceScratch is null || this.compositeSessionInstanceScratch.Length < count)
        {
            this.compositeSessionInstanceScratch = new CompositeInstanceData[Math.Max(256, count)];
        }

        return this.compositeSessionInstanceScratch.AsSpan(0, count);
    }

    private bool TryEnsureCompositeSessionCommandEncoderLocked(in GPUState gpuState)
    {
        if (this.compositeSessionCommandEncoder is not null)
        {
            return true;
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

    private BindGroup* GetOrCreateCoverageBindGroupLocked(
        in GPUState gpuState,
        CoverageEntry coverageEntry,
        WgpuBuffer* instanceBuffer,
        nuint instanceBufferSize)
    {
        if (this.compositeBindGroupLayout is null ||
            coverageEntry.GPUCoverageView is null ||
            instanceBuffer is null ||
            instanceBufferSize == 0)
        {
            return null;
        }

        if (coverageEntry.GPUCompositeBindGroup is not null &&
            coverageEntry.GPUCompositeInstanceBuffer == instanceBuffer)
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
            Buffer = instanceBuffer,
            Offset = 0,
            Size = instanceBufferSize
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
        coverageEntry.GPUCompositeInstanceBuffer = instanceBuffer;
        return bindGroup;
    }

    /// <summary>
    /// Executes one composition draw call into the session target texture.
    /// </summary>
    private bool TryRunCompositePassLocked(
        in GPUState gpuState,
        CommandEncoder* commandEncoder,
        RenderPipeline* compositePipeline,
        CoverageEntry coverageEntry,
        TextureView* targetView,
        uint instanceCount)
    {
        if (compositePipeline is null ||
            this.compositeBindGroupLayout is null ||
            coverageEntry.GPUCoverageView is null ||
            targetView is null)
        {
            return false;
        }

        if (instanceCount == 0)
        {
            return true;
        }

        if (this.compositeSessionInstanceBuffer is null)
        {
            return false;
        }

        BindGroup* bindGroup = this.GetOrCreateCoverageBindGroupLocked(
            in gpuState,
            coverageEntry,
            this.compositeSessionInstanceBuffer,
            this.compositeSessionInstanceBufferCapacity);
        if (bindGroup is null)
        {
            return false;
        }

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

        gpuState.Api.RenderPassEncoderSetPipeline(this.compositeSessionPassEncoder, compositePipeline);
        gpuState.Api.RenderPassEncoderSetBindGroup(this.compositeSessionPassEncoder, 0, bindGroup, 0, null);
        gpuState.Api.RenderPassEncoderDraw(this.compositeSessionPassEncoder, CompositeVertexCount, instanceCount, 0, 0);
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
        entry.GPUCompositeInstanceBuffer = null;
    }

    private void ReleaseAllCoverageCompositeBindGroupsLocked()
    {
        foreach (KeyValuePair<int, CoverageEntry> kv in this.coverageCache)
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

        foreach (KeyValuePair<int, CoverageEntry> kv in this.coverageCache)
        {
            this.ReleaseCoverageTextureLocked(kv.Value);
            kv.Value.Dispose();
        }

        this.coverageCache.Clear();

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
        this.liveCoverageCount = 0;
        this.IsGPUReady = false;
        this.compositeSessionGPUActive = false;
        this.compositeSessionDepth = 0;
        Trace("ReleaseGPUResourcesLocked: end");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(this.isDisposed, this);

    [StructLayout(LayoutKind.Sequential)]
    private struct CompositeInstanceData
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
            CoverageEntry coverage,
            Point sourceOffset,
            WebGPUBrushData brushData,
            float blendPercentage,
            int destinationX,
            int destinationY,
            int compositeWidth,
            int compositeHeight)
        {
            this.Coverage = coverage;
            this.SourceOffset = sourceOffset;
            this.BrushData = brushData;
            this.BlendPercentage = blendPercentage;
            this.DestinationX = destinationX;
            this.DestinationY = destinationY;
            this.CompositeWidth = compositeWidth;
            this.CompositeHeight = compositeHeight;
        }

        public CoverageEntry Coverage { get; }

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

        public WgpuBuffer* GPUCompositeInstanceBuffer { get; set; }

        public void Dispose()
        {
        }
    }
}
