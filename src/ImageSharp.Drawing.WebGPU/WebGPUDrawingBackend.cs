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
internal sealed unsafe class WebGPUDrawingBackend : IDrawingBackend, IDisposable
{
    private const uint CompositeVertexCount = 6;
    private const uint CoverageVertexCount = 3;
    private const int CallbackTimeoutMilliseconds = 10_000;

    private static ReadOnlySpan<byte> EntryPointVertex => "vs_main\0"u8;

    private static ReadOnlySpan<byte> EntryPointFragment => "fs_main\0"u8;

    private readonly object gpuSync = new();
    private readonly ConcurrentDictionary<int, CoverageEntry> preparedCoverage = new();
    private readonly DefaultDrawingBackend fallbackBackend;

    private int nextCoverageHandleId;
    private bool isDisposed;
    private WebGPU? webGpu;
    private Wgpu? wgpuExtension;
    private Instance* instance;
    private Adapter* adapter;
    private Device* device;
    private Queue* queue;
    private BindGroupLayout* compositeBindGroupLayout;
    private PipelineLayout* compositePipelineLayout;
    private RenderPipeline* compositePipeline;
    private BindGroupLayout* coverageBindGroupLayout;
    private PipelineLayout* coveragePipelineLayout;
    private RenderPipeline* coveragePipeline;

    private int compositeSessionDepth;
    private bool compositeSessionGpuActive;
    private bool compositeSessionDirty;
    private Buffer2DRegion<Rgba32> compositeSessionTarget;
    private Texture* compositeSessionTargetTexture;
    private TextureView* compositeSessionTargetView;
    private WgpuBuffer* compositeSessionReadbackBuffer;
    private uint compositeSessionReadbackBytesPerRow;
    private ulong compositeSessionReadbackByteCount;
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

    public int PrepareCoverageCallCount { get; private set; }

    public int GpuPrepareCoverageCallCount { get; private set; }

    public int FallbackPrepareCoverageCallCount { get; private set; }

    public int CompositeCoverageCallCount { get; private set; }

    public int GpuCompositeCoverageCallCount { get; private set; }

    public int CpuCompositeCoverageCallCount { get; private set; }

    public int ReleaseCoverageCallCount { get; private set; }

    public bool IsGpuReady { get; private set; }

    public bool GpuInitializationAttempted { get; private set; }

    public string? LastGpuInitializationFailure { get; private set; }

    public int LiveCoverageCount => this.preparedCoverage.Count;

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

            this.ReleaseCompositeSessionLocked();
        }

        this.compositeSessionGpuActive = false;
        this.compositeSessionDirty = false;
    }

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

        DrawingCoverageHandle coverageHandle = this.PrepareCoverage(
            path,
            clippedOptions,
            configuration.MemoryAllocator,
            preparationMode);
        if (!coverageHandle.IsValid)
        {
            return;
        }

        try
        {
            Buffer2DRegion<TPixel> compositeTarget = target.GetSubRegion(clippedInterest);
            Rectangle brushBounds = Rectangle.Ceiling(path.Bounds);

            this.CompositeCoverage(
                configuration,
                compositeTarget,
                coverageHandle,
                Point.Empty,
                brush,
                graphicsOptions,
                brushBounds);
        }
        finally
        {
            this.ReleaseCoverage(coverageHandle);
        }
    }

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

    public bool SupportsCoverageComposition<TPixel>(Brush brush, in GraphicsOptions graphicsOptions)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        Guard.NotNull(brush, nameof(brush));

        return CanUseGpuComposite<TPixel>(graphicsOptions)
            && WebGpuBrushData.TryCreate(brush, out _)
            && this.TryEnsureGpuReady()
            && this.compositeSessionGpuActive;
    }

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

        if (!TryBuildEdges(path, rasterizerOptions.Interest.Location, out EdgeData[]? edges) || edges.Length == 0)
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
                this.coveragePipeline is null ||
                this.coverageBindGroupLayout is null ||
                !this.TryRasterizeCoverageTextureLocked(edges, in rasterizerOptions, out coverageTexture, out coverageView))
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
            this.CpuCompositeCoverageCallCount++;
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

        if (!CanUseGpuComposite<TPixel>(graphicsOptions) ||
            !WebGpuBrushData.TryCreate(brush, out WebGpuBrushData brushData) ||
            !this.TryEnsureGpuReady())
        {
            throw new InvalidOperationException(
                "Mixed-mode coverage composition is disabled. Coverage was prepared for accelerated composition, but the current composite settings are not GPU-supported.");
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

            this.ReleaseCompositeSessionLocked();

            foreach (KeyValuePair<int, CoverageEntry> kv in this.preparedCoverage)
            {
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

    private bool TryInitializeGpuLocked()
    {
        Trace("TryInitializeGpuLocked: begin");
        try
        {
            this.webGpu = WebGPU.GetApi();
            _ = this.webGpu.TryGetDeviceExtension<Wgpu>(null, out this.wgpuExtension);
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
                 this.coveragePipeline is null ||
                 this.coveragePipelineLayout is null ||
                 this.coverageBindGroupLayout is null ||
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

            ReadOnlySpan<byte> vertexEntryPoint = EntryPointVertex;
            ReadOnlySpan<byte> fragmentEntryPoint = EntryPointFragment;
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

    private bool TryCreateCoveragePipelineLocked()
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
            Buffer = new BufferBindingLayout
            {
                Type = BufferBindingType.ReadOnlyStorage,
                MinBindingSize = 16
            }
        };
        layoutEntries[1] = new BindGroupLayoutEntry
        {
            Binding = 1,
            Visibility = ShaderStage.Fragment,
            Buffer = new BufferBindingLayout
            {
                Type = BufferBindingType.Uniform,
                MinBindingSize = (ulong)Unsafe.SizeOf<CoverageParams>()
            }
        };

        BindGroupLayoutDescriptor layoutDescriptor = new()
        {
            EntryCount = 2,
            Entries = layoutEntries
        };

        this.coverageBindGroupLayout = this.webGpu.DeviceCreateBindGroupLayout(this.device, in layoutDescriptor);
        if (this.coverageBindGroupLayout is null)
        {
            return false;
        }

        BindGroupLayout** bindGroupLayouts = stackalloc BindGroupLayout*[1];
        bindGroupLayouts[0] = this.coverageBindGroupLayout;
        PipelineLayoutDescriptor pipelineLayoutDescriptor = new()
        {
            BindGroupLayoutCount = 1,
            BindGroupLayouts = bindGroupLayouts
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

            ReadOnlySpan<byte> vertexEntryPoint = EntryPointVertex;
            ReadOnlySpan<byte> fragmentEntryPoint = EntryPointFragment;
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

                    ColorTargetState* colorTargets = stackalloc ColorTargetState[1];
                    colorTargets[0] = new ColorTargetState
                    {
                        Format = TextureFormat.R8Unorm,
                        Blend = null,
                        WriteMask = ColorWriteMask.Red
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
                        Layout = this.coveragePipelineLayout,
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

                    this.coveragePipeline = this.webGpu.DeviceCreateRenderPipeline(this.device, in pipelineDescriptor);
                }
            }

            return this.coveragePipeline is not null;
        }
        finally
        {
            if (shaderModule is not null)
            {
                this.webGpu.ShaderModuleRelease(shaderModule);
            }
        }
    }

    private bool TryRasterizeCoverageTextureLocked(
        ReadOnlySpan<EdgeData> edges,
        in RasterizerOptions rasterizerOptions,
        out Texture* coverageTexture,
        out TextureView* coverageView)
    {
        Trace($"TryRasterizeCoverageTextureLocked: begin edges={edges.Length} size={rasterizerOptions.Interest.Width}x{rasterizerOptions.Interest.Height}");
        coverageTexture = null;
        coverageView = null;

        if (this.webGpu is null ||
            this.device is null ||
            this.queue is null ||
            this.coveragePipeline is null ||
            this.coverageBindGroupLayout is null ||
            edges.Length == 0 ||
            rasterizerOptions.Interest.Width <= 0 ||
            rasterizerOptions.Interest.Height <= 0)
        {
            return false;
        }

        TextureDescriptor coverageTextureDescriptor = new()
        {
            Usage = TextureUsage.RenderAttachment | TextureUsage.TextureBinding | TextureUsage.CopySrc,
            Dimension = TextureDimension.Dimension2D,
            Size = new Extent3D((uint)rasterizerOptions.Interest.Width, (uint)rasterizerOptions.Interest.Height, 1),
            Format = TextureFormat.R8Unorm,
            MipLevelCount = 1,
            SampleCount = 1
        };

        coverageTexture = this.webGpu.DeviceCreateTexture(this.device, in coverageTextureDescriptor);
        if (coverageTexture is null)
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

        coverageView = this.webGpu.TextureCreateView(coverageTexture, in coverageViewDescriptor);
        if (coverageView is null)
        {
            this.ReleaseTextureLocked(coverageTexture);
            coverageTexture = null;
            return false;
        }

        ulong edgesBufferSize = checked((ulong)edges.Length * (ulong)Unsafe.SizeOf<EdgeData>());
        ulong paramsBufferSize = (ulong)Unsafe.SizeOf<CoverageParams>();
        WgpuBuffer* edgesBuffer = null;
        WgpuBuffer* paramsBuffer = null;
        BindGroup* bindGroup = null;
        CommandEncoder* commandEncoder = null;
        RenderPassEncoder* passEncoder = null;
        CommandBuffer* commandBuffer = null;
        try
        {
            BufferDescriptor edgesBufferDescriptor = new()
            {
                Usage = BufferUsage.Storage | BufferUsage.CopyDst,
                Size = edgesBufferSize
            };
            edgesBuffer = this.webGpu.DeviceCreateBuffer(this.device, in edgesBufferDescriptor);
            if (edgesBuffer is null)
            {
                return false;
            }

            BufferDescriptor paramsBufferDescriptor = new()
            {
                Usage = BufferUsage.Uniform | BufferUsage.CopyDst,
                Size = paramsBufferSize
            };
            paramsBuffer = this.webGpu.DeviceCreateBuffer(this.device, in paramsBufferDescriptor);
            if (paramsBuffer is null)
            {
                return false;
            }

            fixed (EdgeData* edgesPtr = edges)
            {
                this.webGpu.QueueWriteBuffer(this.queue, edgesBuffer, 0, edgesPtr, (nuint)edgesBufferSize);
            }

            CoverageParams coverageParams = new()
            {
                EdgeCount = (uint)edges.Length,
                IntersectionRule = rasterizerOptions.IntersectionRule == IntersectionRule.EvenOdd ? 0U : 1U,
                Antialias = rasterizerOptions.RasterizationMode == RasterizationMode.Antialiased ? 1U : 0U,
                SampleOriginX = rasterizerOptions.SamplingOrigin == RasterizerSamplingOrigin.PixelCenter ? 0.5F : 0F,
                SampleOriginY = rasterizerOptions.SamplingOrigin == RasterizerSamplingOrigin.PixelCenter ? 0.5F : 0F
            };
            this.webGpu.QueueWriteBuffer(
                this.queue,
                paramsBuffer,
                0,
                ref coverageParams,
                (nuint)Unsafe.SizeOf<CoverageParams>());

            BindGroupEntry* bindEntries = stackalloc BindGroupEntry[2];
            bindEntries[0] = new BindGroupEntry
            {
                Binding = 0,
                Buffer = edgesBuffer,
                Offset = 0,
                Size = edgesBufferSize
            };
            bindEntries[1] = new BindGroupEntry
            {
                Binding = 1,
                Buffer = paramsBuffer,
                Offset = 0,
                Size = paramsBufferSize
            };

            BindGroupDescriptor bindGroupDescriptor = new()
            {
                Layout = this.coverageBindGroupLayout,
                EntryCount = 2,
                Entries = bindEntries
            };
            bindGroup = this.webGpu.DeviceCreateBindGroup(this.device, in bindGroupDescriptor);
            if (bindGroup is null)
            {
                return false;
            }

            CommandEncoderDescriptor commandEncoderDescriptor = default;
            commandEncoder = this.webGpu.DeviceCreateCommandEncoder(this.device, in commandEncoderDescriptor);
            if (commandEncoder is null)
            {
                return false;
            }

            RenderPassColorAttachment colorAttachment = new()
            {
                View = coverageView,
                ResolveTarget = null,
                LoadOp = LoadOp.Clear,
                StoreOp = StoreOp.Store,
                ClearValue = default
            };

            RenderPassDescriptor renderPassDescriptor = new()
            {
                ColorAttachmentCount = 1,
                ColorAttachments = &colorAttachment
            };

            passEncoder = this.webGpu.CommandEncoderBeginRenderPass(commandEncoder, in renderPassDescriptor);
            if (passEncoder is null)
            {
                return false;
            }

            this.webGpu.RenderPassEncoderSetPipeline(passEncoder, this.coveragePipeline);
            this.webGpu.RenderPassEncoderSetBindGroup(passEncoder, 0, bindGroup, 0, (uint*)null);
            this.webGpu.RenderPassEncoderDraw(passEncoder, CoverageVertexCount, 1, 0, 0);
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
            if (this.wgpuExtension is not null)
            {
                _ = this.wgpuExtension.DevicePoll(this.device, true, (WrappedSubmissionIndex*)null);
            }

            this.webGpu.CommandBufferRelease(commandBuffer);
            commandBuffer = null;
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

            if (bindGroup is not null)
            {
                this.webGpu.BindGroupRelease(bindGroup);
            }

            this.ReleaseBufferLocked(paramsBuffer);
            this.ReleaseBufferLocked(edgesBuffer);
        }
    }

    private static bool TryBuildEdges(IPath path, Point interestLocation, [NotNullWhen(true)] out EdgeData[]? edges)
    {
        List<EdgeData> edgeList = [];
        float offsetX = -interestLocation.X;
        float offsetY = -interestLocation.Y;

        foreach (ISimplePath simplePath in path.Flatten())
        {
            ReadOnlySpan<PointF> points = simplePath.Points.Span;
            if (points.Length < 2)
            {
                continue;
            }

            for (int i = 1; i < points.Length; i++)
            {
                AddEdge(points[i - 1], points[i], offsetX, offsetY, edgeList);
            }

            if (simplePath.IsClosed)
            {
                AddEdge(points[^1], points[0], offsetX, offsetY, edgeList);
            }
        }

        if (edgeList.Count == 0)
        {
            edges = null;
            return false;
        }

        edges = [.. edgeList];
        return true;
    }

    private static void AddEdge(PointF from, PointF to, float offsetX, float offsetY, List<EdgeData> destination)
    {
        if (from.Equals(to))
        {
            return;
        }

        destination.Add(new EdgeData
        {
            X0 = from.X + offsetX,
            Y0 = from.Y + offsetY,
            X1 = to.X + offsetX,
            Y1 = to.Y + offsetY
        });
    }

    private bool WaitForSignalLocked(ManualResetEventSlim signal)
    {
        Stopwatch timer = Stopwatch.StartNew();
        while (!signal.Wait(1))
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

        if (IsSingleMemory(sourceRegion.Buffer))
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

    private bool TryBeginCompositeSessionLocked(Buffer2DRegion<Rgba32> target)
    {
        this.ReleaseCompositeSessionLocked();

        if (!this.IsGpuReady ||
            this.webGpu is null ||
            this.device is null ||
            this.queue is null ||
            target.Width <= 0 ||
            target.Height <= 0)
        {
            return false;
        }

        uint textureRowBytes = checked((uint)target.Width * (uint)Unsafe.SizeOf<Rgba32>());
        uint readbackRowBytes = AlignTo256(textureRowBytes);
        ulong readbackByteCount = (ulong)readbackRowBytes * (uint)target.Height;

        TextureDescriptor targetTextureDescriptor = new()
        {
            Usage = TextureUsage.RenderAttachment | TextureUsage.CopySrc | TextureUsage.CopyDst,
            Dimension = TextureDimension.Dimension2D,
            Size = new Extent3D((uint)target.Width, (uint)target.Height, 1),
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
            this.ReleaseBufferLocked(readbackBuffer);
            this.ReleaseTextureViewLocked(targetView);
            this.ReleaseTextureLocked(targetTexture);
            return false;
        }

        if (!this.TryQueueWriteTextureFromRgbaRegionLocked(targetTexture, target))
        {
            this.ReleaseBufferLocked(readbackBuffer);
            this.ReleaseTextureViewLocked(targetView);
            this.ReleaseTextureLocked(targetTexture);
            return false;
        }

        this.compositeSessionTarget = target;
        this.compositeSessionTargetTexture = targetTexture;
        this.compositeSessionTargetView = targetView;
        this.compositeSessionReadbackBuffer = readbackBuffer;
        this.compositeSessionReadbackBytesPerRow = readbackRowBytes;
        this.compositeSessionReadbackByteCount = readbackByteCount;
        return true;
    }

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

        CommandEncoder* commandEncoder = null;
        CommandBuffer* commandBuffer = null;
        try
        {
            CommandEncoderDescriptor commandEncoderDescriptor = default;
            commandEncoder = this.webGpu.DeviceCreateCommandEncoder(this.device, in commandEncoderDescriptor);
            if (commandEncoder is null)
            {
                return false;
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

    private void ReleaseCompositeSessionLocked()
    {
        this.ReleaseBufferLocked(this.compositeSessionReadbackBuffer);
        this.ReleaseTextureViewLocked(this.compositeSessionTargetView);
        this.ReleaseTextureLocked(this.compositeSessionTargetTexture);
        this.compositeSessionReadbackBuffer = null;
        this.compositeSessionTargetTexture = null;
        this.compositeSessionTargetView = null;
        this.compositeSessionReadbackBytesPerRow = 0;
        this.compositeSessionReadbackByteCount = 0;
        this.compositeSessionTarget = default;
        this.compositeSessionDirty = false;
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
                this.compositePipeline is null || this.compositeBindGroupLayout is null || this.compositeSessionTargetView is null)
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

                if (this.TryRunCompositePassInSessionLocked(
                    entry,
                    sourceOffset,
                    brushData,
                    blendPercentage,
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

                this.ReleaseCompositeSessionLocked();
                this.compositeSessionGpuActive = false;
                return false;
            }

            return false;
        }
    }

    private static bool TryEnsureCoverageTextureLocked(CoverageEntry entry)
    {
        if (entry.GpuCoverageTexture is not null && entry.GpuCoverageView is not null)
        {
            return true;
        }

        return false;
    }

    private bool TryRunCompositePassInSessionLocked(
        CoverageEntry coverageEntry,
        Point sourceOffset,
        WebGpuBrushData brushData,
        float blendPercentage,
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
            this.compositeSessionTargetView is null)
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
        CommandEncoder* commandEncoder = null;
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
                TargetWidth = (uint)this.compositeSessionTarget.Width,
                TargetHeight = (uint)this.compositeSessionTarget.Height,
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

            CommandEncoderDescriptor commandEncoderDescriptor = default;
            commandEncoder = this.webGpu.DeviceCreateCommandEncoder(this.device, in commandEncoderDescriptor);
            if (commandEncoder is null)
            {
                return false;
            }

            RenderPassColorAttachment colorAttachment = new()
            {
                View = this.compositeSessionTargetView,
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

            passEncoder = this.webGpu.CommandEncoderBeginRenderPass(commandEncoder, in renderPassDescriptor);
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

            CommandBufferDescriptor commandBufferDescriptor = default;
            commandBuffer = this.webGpu.CommandEncoderFinish(commandEncoder, in commandBufferDescriptor);
            if (commandBuffer is null)
            {
                return false;
            }

            this.webGpu.QueueSubmit(this.queue, 1, ref commandBuffer);
            if (this.wgpuExtension is not null)
            {
                _ = this.wgpuExtension.DevicePoll(this.device, true, (WrappedSubmissionIndex*)null);
            }

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

            if (commandEncoder is not null)
            {
                this.webGpu.CommandEncoderRelease(commandEncoder);
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

    private void ReleaseGpuResourcesLocked()
    {
        Trace("ReleaseGpuResourcesLocked: begin");
        this.ReleaseCompositeSessionLocked();

        if (this.webGpu is not null)
        {
            if (this.coveragePipeline is not null)
            {
                this.webGpu.RenderPipelineRelease(this.coveragePipeline);
                this.coveragePipeline = null;
            }

            if (this.coveragePipelineLayout is not null)
            {
                this.webGpu.PipelineLayoutRelease(this.coveragePipelineLayout);
                this.coveragePipelineLayout = null;
            }

            if (this.coverageBindGroupLayout is not null)
            {
                this.webGpu.BindGroupLayoutRelease(this.coverageBindGroupLayout);
                this.coverageBindGroupLayout = null;
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

            this.webGpu.Dispose();
            this.webGpu = null;
        }

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
    private struct CoverageParams
    {
        public uint EdgeCount;
        public uint IntersectionRule;
        public uint Antialias;
        public uint Padding0;
        public float SampleOriginX;
        public float SampleOriginY;
        public float Padding1;
        public float Padding2;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct EdgeData
    {
        public float X0;
        public float Y0;
        public float X1;
        public float Y1;
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
