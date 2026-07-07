// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Silk.NET.WebGPU;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

internal static unsafe partial class WebGPURuntime
{
    /// <summary>
    /// Maps raw native device pointers to their shared state. Keyed by the raw pointer so
    /// distinct wrapper handles over the same native device resolve to one shared state.
    /// </summary>
    private static readonly ConcurrentDictionary<nint, DeviceSharedState> DeviceStateCache = new();

    /// <summary>
    /// Serializes creation and teardown of cache entries so a state cannot be created
    /// concurrently with <see cref="ClearDeviceStateCache"/> disposing the cache.
    /// </summary>
    private static readonly object DeviceStateCacheSync = new();

    /// <summary>
    /// Gets or creates process-scoped shared resources for the specified device.
    /// </summary>
    /// <param name="api">The WebGPU API facade used to manage native resources.</param>
    /// <param name="deviceHandle">The device key and owner for the shared state.</param>
    /// <returns>The shared device state instance for <paramref name="deviceHandle"/>.</returns>
    internal static DeviceSharedState GetOrCreateDeviceState(WebGPU api, WebGPUDeviceHandle deviceHandle)
    {
        nint cacheKey = deviceHandle.DangerousGetHandle();

        lock (DeviceStateCacheSync)
        {
            if (DeviceStateCache.TryGetValue(cacheKey, out DeviceSharedState? existing))
            {
                return existing;
            }

            DeviceSharedState created = new(api, deviceHandle);
            DeviceStateCache[cacheKey] = created;

            // First-ever compilation of the staged pipeline set costs multi-second driver work
            // on a cold driver shader cache. Warming in the background at device creation moves
            // that cost off the first flush; the pipeline caches are thread-safe, so an early
            // flush simply blocks on the specific pipelines it needs.
            WebGPUSceneDispatch.BeginPipelineWarmup(created);
            return created;
        }
    }

    /// <summary>
    /// Disposes all cached device-scoped shared state. Called from process-exit teardown before
    /// the runtime-owned device handles are disposed, because each state holds a reference on
    /// its device handle.
    /// </summary>
    private static void ClearDeviceStateCache()
    {
        lock (DeviceStateCacheSync)
        {
            foreach (DeviceSharedState state in DeviceStateCache.Values)
            {
                state.Dispose();
            }

            DeviceStateCache.Clear();
        }
    }

    /// <summary>
    /// Shared device-scoped caches for pipelines and pipeline layouts.
    /// </summary>
    internal sealed class DeviceSharedState : IDisposable
    {
        /// <summary>
        /// Fallback storage-buffer binding ceiling (WebGPU's guaranteed minimum of 128 MiB)
        /// used when the device limit cannot be queried.
        /// </summary>
        private const nuint DefaultMaxStorageBufferBindingSize = 128U * 1024U * 1024U;

        /// <summary>
        /// Cached graphics-pipeline families keyed by pipeline key. Creation within one family
        /// is serialized by locking the family instance itself.
        /// </summary>
        private readonly ConcurrentDictionary<string, CompositePipelineInfrastructure> compositePipelines = new(StringComparer.Ordinal);

        /// <summary>
        /// Cached compute-pipeline families keyed by pipeline key. Creation within one family
        /// is serialized by locking the family instance itself.
        /// </summary>
        private readonly ConcurrentDictionary<string, CompositeComputePipelineInfrastructure> compositeComputePipelines = new(StringComparer.Ordinal);

        /// <summary>
        /// Pool of map-readable status buffers reused by deferred overflow readbacks. One buffer
        /// is needed per deferred flush; creating it fresh each time is a measurable per-frame
        /// driver cost, so retired buffers are recycled here.
        /// </summary>
        private readonly Stack<StatusReadbackBufferEntry> statusReadbackBuffers = new();

        /// <summary>
        /// Guards <see cref="statusReadbackBuffers"/>; flushes on different threads can rent
        /// and return concurrently.
        /// </summary>
        private readonly object statusReadbackSync = new();

        /// <summary>
        /// Upper bound on pooled status readback buffers; returns beyond it release instead.
        /// </summary>
        private const int MaxPooledStatusReadbackBuffers = 16;

        /// <summary>
        /// Snapshot of the device features taken at construction time.
        /// </summary>
        private readonly HashSet<FeatureName> deviceFeatures;

        /// <summary>
        /// Holds one reference on the device handle for the lifetime of this state so the
        /// cached pipelines can never outlive the device pointer they were created from.
        /// </summary>
        private WebGPUHandle.HandleReference deviceReference;

        /// <summary>
        /// Rooted native callback thunk; kept alive while it is registered on the device.
        /// </summary>
        private readonly PfnErrorCallback uncapturedErrorCallback;

        /// <summary>
        /// Tracks whether <see cref="Dispose"/> has run.
        /// </summary>
        private bool disposed;

        /// <summary>
        /// Initializes a new instance of the <see cref="DeviceSharedState"/> class, acquiring a
        /// device reference, installing the uncaptured-error callback, and snapshotting the
        /// device features and limits.
        /// </summary>
        /// <param name="api">The WebGPU API facade used to manage native resources.</param>
        /// <param name="deviceHandle">The device this state is scoped to.</param>
        internal DeviceSharedState(WebGPU api, WebGPUDeviceHandle deviceHandle)
        {
            this.Api = api;
            this.deviceReference = deviceHandle.AcquireReference();

            try
            {
                this.uncapturedErrorCallback = PfnErrorCallback.From(HandleUncapturedError);
                this.Device = (Device*)this.deviceReference.Handle;
                this.Api.DeviceSetUncapturedErrorCallback(this.Device, this.uncapturedErrorCallback, null);
                this.deviceFeatures = EnumerateDeviceFeatures(api, this.Device);
                this.MaxStorageBufferBindingSize = QueryMaxStorageBufferBindingSize(api, this.Device);
            }
            catch
            {
                // Construction failed part-way; undo the callback thunk and the device
                // reference so the handle refcount is not left permanently elevated.
                this.uncapturedErrorCallback.Dispose();
                this.deviceReference.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Gets the fixed entry point used by all cached composite vertex shaders.
        /// </summary>
        private static ReadOnlySpan<byte> CompositeVertexEntryPoint => "vs_main\0"u8;

        /// <summary>
        /// Gets the fixed entry point used by all cached composite fragment shaders.
        /// </summary>
        private static ReadOnlySpan<byte> CompositeFragmentEntryPoint => "fs_main\0"u8;

        /// <summary>
        /// Gets the WebGPU API instance used by this shared state.
        /// </summary>
        public WebGPU Api { get; }

        /// <summary>
        /// Gets the device associated with this shared state. The pointer stays valid for the
        /// lifetime of this instance because <see cref="deviceReference"/> pins the handle open.
        /// </summary>
        public Device* Device { get; }

        /// <summary>
        /// Gets the maximum size, in bytes, of one usable storage scratch buffer on this device: the
        /// smaller of the queried maxStorageBufferBindingSize and maxBufferSize.
        /// </summary>
        /// <remarks>
        /// A scratch buffer must be both creatable (within maxBufferSize) and bindable in full (within
        /// maxStorageBufferBindingSize), so the effective ceiling that drives the chunking decision is the
        /// minimum of both. Using the (often larger) binding size alone lets a scene skip chunking and then
        /// fail buffer creation, surfacing as an invalid-buffer validation error and a blank frame.
        /// </remarks>
        public nuint MaxStorageBufferBindingSize { get; }

        /// <summary>
        /// Returns whether the device has the specified feature.
        /// </summary>
        /// <param name="feature">The feature to check.</param>
        /// <returns><see langword="true"/> when the device has the feature; otherwise <see langword="false"/>.</returns>
        public bool HasFeature(FeatureName feature)
            => this.deviceFeatures.Contains(feature);

        /// <summary>
        /// Rents a pooled map-readable status buffer, or creates one when the pool has no buffer
        /// large enough for the requested status data.
        /// </summary>
        /// <param name="byteLength">The required scheduling-status byte length.</param>
        /// <returns>The rented buffer, or <see langword="null"/> when creation failed.</returns>
        public Silk.NET.WebGPU.Buffer* RentStatusReadbackBuffer(nuint byteLength)
        {
            lock (this.statusReadbackSync)
            {
                Span<StatusReadbackBufferEntry> retained = stackalloc StatusReadbackBufferEntry[MaxPooledStatusReadbackBuffers];
                int retainedIndex = 0;

                while (this.statusReadbackBuffers.Count > 0)
                {
                    StatusReadbackBufferEntry entry = this.statusReadbackBuffers.Pop();
                    if (entry.ByteLength >= byteLength)
                    {
                        while (retainedIndex > 0)
                        {
                            this.statusReadbackBuffers.Push(retained[--retainedIndex]);
                        }

                        return (Silk.NET.WebGPU.Buffer*)entry.Buffer;
                    }

                    retained[retainedIndex++] = entry;
                }

                while (retainedIndex > 0)
                {
                    this.statusReadbackBuffers.Push(retained[--retainedIndex]);
                }
            }

            BufferDescriptor descriptor = new()
            {
                Usage = BufferUsage.CopyDst | BufferUsage.MapRead,
                Size = byteLength,
                MappedAtCreation = false
            };

            return this.Api.DeviceCreateBuffer(this.Device, in descriptor);
        }

        /// <summary>
        /// Returns a status readback buffer to the pool. The buffer must be unmapped with no
        /// map pending; a buffer whose map state is unknown must be released, not returned.
        /// </summary>
        /// <param name="buffer">The buffer to recycle.</param>
        /// <param name="byteLength">The byte capacity of <paramref name="buffer"/>.</param>
        public void ReturnStatusReadbackBuffer(Silk.NET.WebGPU.Buffer* buffer, nuint byteLength)
        {
            if (buffer is null)
            {
                return;
            }

            lock (this.statusReadbackSync)
            {
                if (!this.disposed && this.statusReadbackBuffers.Count < MaxPooledStatusReadbackBuffers)
                {
                    this.statusReadbackBuffers.Push(new StatusReadbackBufferEntry((nint)buffer, byteLength));
                    return;
                }
            }

            this.Api.BufferRelease(buffer);
        }

        /// <summary>
        /// Forwards uncaptured native WebGPU errors through the public environment callback.
        /// </summary>
        /// <param name="errorType">The native error classification.</param>
        /// <param name="message">The native UTF-8 error message, or <see langword="null"/>.</param>
        /// <param name="userData">Unused native user-data pointer.</param>
        private static void HandleUncapturedError(ErrorType errorType, byte* message, void* userData)
        {
            _ = userData;

            string errorMessage = message is null
                ? string.Empty
                : Marshal.PtrToStringUTF8((nint)message) ?? string.Empty;

            WebGPUEnvironment.ReportUncapturedError(ToPublicErrorType(errorType), errorMessage);
        }

        /// <summary>
        /// Maps Silk's native error enum to the public API enum without exposing Silk types.
        /// </summary>
        /// <param name="errorType">The native error classification.</param>
        /// <returns>The equivalent public error type.</returns>
        private static WebGPUErrorType ToPublicErrorType(ErrorType errorType)
            => errorType switch
            {
                ErrorType.NoError => WebGPUErrorType.NoError,
                ErrorType.Validation => WebGPUErrorType.Validation,
                ErrorType.OutOfMemory => WebGPUErrorType.OutOfMemory,
                ErrorType.Internal => WebGPUErrorType.Internal,
                ErrorType.DeviceLost => WebGPUErrorType.DeviceLost,
                _ => WebGPUErrorType.Unknown
            };

        /// <summary>
        /// Snapshots the feature set currently reported by the native device.
        /// </summary>
        /// <param name="api">The WebGPU API facade.</param>
        /// <param name="device">The device to query.</param>
        /// <returns>The set of features the device reports; empty when the device is <see langword="null"/> or reports none.</returns>
        private static HashSet<FeatureName> EnumerateDeviceFeatures(WebGPU api, Device* device)
        {
            if (device is null)
            {
                return [];
            }

            int count = (int)api.DeviceEnumerateFeatures(device, (FeatureName*)null);
            if (count <= 0)
            {
                return [];
            }

            FeatureName* features = stackalloc FeatureName[count];
            api.DeviceEnumerateFeatures(device, features);

            HashSet<FeatureName> result = new(count);
            for (int i = 0; i < count; i++)
            {
                result.Add(features[i]);
            }

            return result;
        }

        /// <summary>
        /// Queries the device's storage-buffer binding ceiling, falling back to WebGPU's guaranteed minimum when unavailable.
        /// </summary>
        /// <param name="api">The WebGPU API facade.</param>
        /// <param name="device">The device to query.</param>
        /// <returns>The maximum storage-buffer binding size in bytes.</returns>
        private static nuint QueryMaxStorageBufferBindingSize(WebGPU api, Device* device)
        {
            if (device is null)
            {
                return DefaultMaxStorageBufferBindingSize;
            }

            SupportedLimits supportedLimits = default;
            if (!api.DeviceGetLimits(device, ref supportedLimits))
            {
                return DefaultMaxStorageBufferBindingSize;
            }

            // A scratch buffer must be both creatable (<= maxBufferSize) and bindable in full
            // (<= maxStorageBufferBindingSize). maxBufferSize is frequently the smaller of the two
            // (256 MiB by default versus a multi-gigabyte binding size), so the effective per-buffer
            // ceiling that the chunking decision must respect is the minimum of both. Using the binding
            // size alone lets a large scene skip chunking and then fail buffer creation, which surfaces
            // as an invalid-buffer validation error and a blank frame.
            ulong binding = supportedLimits.Limits.MaxStorageBufferBindingSize;
            ulong bufferSize = supportedLimits.Limits.MaxBufferSize;
            ulong reported = Math.Min(binding == 0 ? ulong.MaxValue : binding, bufferSize == 0 ? ulong.MaxValue : bufferSize);
            if (reported == 0 || reported == ulong.MaxValue || reported > nuint.MaxValue)
            {
                return DefaultMaxStorageBufferBindingSize;
            }

            return (nuint)reported;
        }

        /// <summary>
        /// Gets or creates a graphics pipeline used for composite rendering. Pipeline variants
        /// are cached per (texture format, blend mode) within the family identified by
        /// <paramref name="pipelineKey"/>.
        /// </summary>
        /// <param name="pipelineKey">The stable key identifying the pipeline family.</param>
        /// <param name="shaderCode">Null-terminated WGSL source, used only when the family's shader module has not been created yet.</param>
        /// <param name="bindGroupLayoutFactory">Creates the family's bind-group layout on first use.</param>
        /// <param name="textureFormat">The color-target format for the requested variant.</param>
        /// <param name="blendMode">The blend mode for the requested variant.</param>
        /// <param name="bindGroupLayout">Receives the family's shared bind-group layout on success.</param>
        /// <param name="pipeline">Receives the cached or newly created render pipeline on success.</param>
        /// <param name="error">Receives the failure description when creation fails.</param>
        /// <returns><see langword="true"/> when the pipeline is available; otherwise <see langword="false"/>.</returns>
        public bool TryGetOrCreateCompositePipeline(
            string pipelineKey,
            ReadOnlySpan<byte> shaderCode,
            WebGPUCompositeBindGroupLayoutFactory bindGroupLayoutFactory,
            TextureFormat textureFormat,
            CompositePipelineBlendMode blendMode,
            out BindGroupLayout* bindGroupLayout,
            out RenderPipeline* pipeline,
            out string? error)
        {
            bindGroupLayout = null;
            pipeline = null;

            ObjectDisposedException.ThrowIf(this.disposed, this);

            CompositePipelineInfrastructure infrastructure = this.compositePipelines.GetOrAdd(
                pipelineKey,
                static _ => new CompositePipelineInfrastructure());

            lock (infrastructure)
            {
                if (infrastructure.BindGroupLayout is null ||
                    infrastructure.PipelineLayout is null ||
                    infrastructure.ShaderModule is null)
                {
                    if (!this.TryCreateCompositeInfrastructure(
                            shaderCode,
                            bindGroupLayoutFactory,
                            out BindGroupLayout* createdBindGroupLayout,
                            out PipelineLayout* createdPipelineLayout,
                            out ShaderModule* createdShaderModule,
                            out error))
                    {
                        return false;
                    }

                    infrastructure.BindGroupLayout = createdBindGroupLayout;
                    infrastructure.PipelineLayout = createdPipelineLayout;
                    infrastructure.ShaderModule = createdShaderModule;
                }

                bindGroupLayout = infrastructure.BindGroupLayout;
                (TextureFormat TextureFormat, CompositePipelineBlendMode BlendMode) variantKey = (textureFormat, blendMode);
                if (infrastructure.Pipelines.TryGetValue(variantKey, out nint cachedPipelineHandle) && cachedPipelineHandle != 0)
                {
                    pipeline = (RenderPipeline*)cachedPipelineHandle;
                    error = null;
                    return true;
                }

                RenderPipeline* createdPipeline = this.CreateCompositePipeline(
                    infrastructure.PipelineLayout,
                    infrastructure.ShaderModule,
                    textureFormat,
                    blendMode);
                if (createdPipeline is null)
                {
                    error = $"Failed to create composite pipeline '{pipelineKey}' for format '{textureFormat}'.";
                    return false;
                }

                infrastructure.Pipelines[variantKey] = (nint)createdPipeline;
                pipeline = createdPipeline;
                error = null;
                return true;
            }
        }

        /// <summary>
        /// Gets or creates a compute pipeline used for composite execution. One pipeline is
        /// cached per family, so <paramref name="shaderCode"/> and <paramref name="entryPoint"/>
        /// are only consumed on first creation; later calls must pass the same values for the
        /// same <paramref name="pipelineKey"/>.
        /// </summary>
        /// <param name="pipelineKey">The stable key identifying the pipeline family.</param>
        /// <param name="shaderCode">Null-terminated WGSL source, used only when the family's shader module has not been created yet.</param>
        /// <param name="entryPoint">Null-terminated compute entry-point name, used only on first creation.</param>
        /// <param name="bindGroupLayoutFactory">Creates the family's bind-group layout on first use.</param>
        /// <param name="bindGroupLayout">Receives the family's shared bind-group layout on success.</param>
        /// <param name="pipeline">Receives the cached or newly created compute pipeline on success.</param>
        /// <param name="error">Receives the failure description when creation fails.</param>
        /// <returns><see langword="true"/> when the pipeline is available; otherwise <see langword="false"/>.</returns>
        public bool TryGetOrCreateCompositeComputePipeline(
            string pipelineKey,
            ReadOnlySpan<byte> shaderCode,
            ReadOnlySpan<byte> entryPoint,
            WebGPUCompositeBindGroupLayoutFactory bindGroupLayoutFactory,
            out BindGroupLayout* bindGroupLayout,
            out ComputePipeline* pipeline,
            out string? error)
        {
            bindGroupLayout = null;
            pipeline = null;

            ObjectDisposedException.ThrowIf(this.disposed, this);

            CompositeComputePipelineInfrastructure infrastructure = this.compositeComputePipelines.GetOrAdd(
                pipelineKey,
                static _ => new CompositeComputePipelineInfrastructure());

            lock (infrastructure)
            {
                if (infrastructure.BindGroupLayout is null ||
                    infrastructure.PipelineLayout is null ||
                    infrastructure.ShaderModule is null)
                {
                    if (!this.TryCreateCompositeInfrastructure(
                            shaderCode,
                            bindGroupLayoutFactory,
                            out BindGroupLayout* createdBindGroupLayout,
                            out PipelineLayout* createdPipelineLayout,
                            out ShaderModule* createdShaderModule,
                            out error))
                    {
                        return false;
                    }

                    infrastructure.BindGroupLayout = createdBindGroupLayout;
                    infrastructure.PipelineLayout = createdPipelineLayout;
                    infrastructure.ShaderModule = createdShaderModule;
                }

                bindGroupLayout = infrastructure.BindGroupLayout;
                if (infrastructure.Pipeline is not null)
                {
                    pipeline = infrastructure.Pipeline;
                    error = null;
                    return true;
                }

                ComputePipeline* createdPipeline = this.CreateCompositeComputePipeline(
                    infrastructure.PipelineLayout,
                    infrastructure.ShaderModule,
                    entryPoint);
                if (createdPipeline is null)
                {
                    error = $"Failed to create composite compute pipeline '{pipelineKey}'.";
                    return false;
                }

                infrastructure.Pipeline = createdPipeline;
                pipeline = createdPipeline;
                error = null;
                return true;
            }
        }

        /// <summary>
        /// Releases all cached pipelines owned by this state, unregisters the uncaptured-error
        /// callback, and drops the device reference. Invoked under the runtime's cache lock via
        /// <see cref="ClearDeviceStateCache"/>; not safe to run concurrently with pipeline creation.
        /// </summary>
        public void Dispose()
        {
            if (this.disposed)
            {
                return;
            }

            foreach (CompositePipelineInfrastructure infrastructure in this.compositePipelines.Values)
            {
                this.ReleaseCompositeInfrastructure(infrastructure);
            }

            this.compositePipelines.Clear();

            foreach (CompositeComputePipelineInfrastructure infrastructure in this.compositeComputePipelines.Values)
            {
                this.ReleaseCompositeComputeInfrastructure(infrastructure);
            }

            this.compositeComputePipelines.Clear();

            lock (this.statusReadbackSync)
            {
                while (this.statusReadbackBuffers.Count > 0)
                {
                    this.Api.BufferRelease((Silk.NET.WebGPU.Buffer*)this.statusReadbackBuffers.Pop().Buffer);
                }
            }

            // Clear the native callback slot before freeing Silk's delegate thunk; otherwise the
            // device could still invoke a callback whose managed thunk has been reclaimed.
            this.Api.DeviceSetUncapturedErrorCallback(this.Device, default, null);
            this.uncapturedErrorCallback.Dispose();

            // Drop the device reference last: every release above calls into the device.
            this.deviceReference.Dispose();
            this.disposed = true;
        }

        /// <summary>
        /// Creates the shared bind-group layout, pipeline layout, and shader module for one cached pipeline family.
        /// On failure every partially created object is released before returning.
        /// </summary>
        /// <param name="shaderCode">Null-terminated WGSL source for the family's shader module.</param>
        /// <param name="bindGroupLayoutFactory">Creates the family's bind-group layout.</param>
        /// <param name="bindGroupLayout">Receives the created bind-group layout on success.</param>
        /// <param name="pipelineLayout">Receives the created pipeline layout on success.</param>
        /// <param name="shaderModule">Receives the created shader module on success.</param>
        /// <param name="error">Receives the failure description when creation fails.</param>
        /// <returns><see langword="true"/> when all three objects were created; otherwise <see langword="false"/>.</returns>
        private bool TryCreateCompositeInfrastructure(
            ReadOnlySpan<byte> shaderCode,
            WebGPUCompositeBindGroupLayoutFactory bindGroupLayoutFactory,
            out BindGroupLayout* bindGroupLayout,
            out PipelineLayout* pipelineLayout,
            out ShaderModule* shaderModule,
            out string? error)
        {
            bindGroupLayout = null;
            pipelineLayout = null;
            shaderModule = null;

            if (!bindGroupLayoutFactory(this.Api, this.Device, out bindGroupLayout, out error))
            {
                return false;
            }

            BindGroupLayout** bindGroupLayouts = stackalloc BindGroupLayout*[1];
            bindGroupLayouts[0] = bindGroupLayout;
            PipelineLayoutDescriptor pipelineLayoutDescriptor = new()
            {
                BindGroupLayoutCount = 1,
                BindGroupLayouts = bindGroupLayouts
            };

            pipelineLayout = this.Api.DeviceCreatePipelineLayout(this.Device, in pipelineLayoutDescriptor);
            if (pipelineLayout is null)
            {
                this.Api.BindGroupLayoutRelease(bindGroupLayout);
                error = "Failed to create composite pipeline layout.";
                return false;
            }

            shaderModule = this.CreateShaderModule(shaderCode);

            if (shaderModule is null)
            {
                this.Api.PipelineLayoutRelease(pipelineLayout);
                this.Api.BindGroupLayoutRelease(bindGroupLayout);
                error = "Failed to create composite shader module.";
                return false;
            }

            error = null;
            return true;
        }

        /// <summary>
        /// Creates one graphics pipeline variant for the specified output format and blend mode.
        /// </summary>
        /// <param name="pipelineLayout">The family's shared pipeline layout.</param>
        /// <param name="shaderModule">The family's shared shader module.</param>
        /// <param name="textureFormat">The color-target format for the variant.</param>
        /// <param name="blendMode">The blend mode for the variant.</param>
        /// <returns>The created render pipeline, or <see langword="null"/> on failure.</returns>
        private RenderPipeline* CreateCompositePipeline(
            PipelineLayout* pipelineLayout,
            ShaderModule* shaderModule,
            TextureFormat textureFormat,
            CompositePipelineBlendMode blendMode)
        {
            ReadOnlySpan<byte> vertexEntryPoint = CompositeVertexEntryPoint;
            ReadOnlySpan<byte> fragmentEntryPoint = CompositeFragmentEntryPoint;
            fixed (byte* vertexEntryPointPtr = vertexEntryPoint)
            {
                fixed (byte* fragmentEntryPointPtr = fragmentEntryPoint)
                {
                    return this.CreateCompositePipelineCore(
                        pipelineLayout,
                        shaderModule,
                        vertexEntryPointPtr,
                        fragmentEntryPointPtr,
                        textureFormat,
                        blendMode);
                }
            }
        }

        /// <summary>
        /// Creates the underlying render pipeline once the shared shader module and entry points are fixed.
        /// </summary>
        /// <param name="pipelineLayout">The family's shared pipeline layout.</param>
        /// <param name="shaderModule">The family's shared shader module.</param>
        /// <param name="vertexEntryPointPtr">Pinned pointer to the null-terminated vertex entry-point name.</param>
        /// <param name="fragmentEntryPointPtr">Pinned pointer to the null-terminated fragment entry-point name.</param>
        /// <param name="textureFormat">The color-target format for the variant.</param>
        /// <param name="blendMode">The blend mode for the variant.</param>
        /// <returns>The created render pipeline, or <see langword="null"/> on failure.</returns>
        private RenderPipeline* CreateCompositePipelineCore(
            PipelineLayout* pipelineLayout,
            ShaderModule* shaderModule,
            byte* vertexEntryPointPtr,
            byte* fragmentEntryPointPtr,
            TextureFormat textureFormat,
            CompositePipelineBlendMode blendMode)
        {
            _ = blendMode;
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
                Format = textureFormat,
                Blend = null,
                WriteMask = ColorWriteMask.All
            };

            FragmentState fragmentState = new()
            {
                Module = shaderModule,
                EntryPoint = fragmentEntryPointPtr,
                TargetCount = 1,
                Targets = colorTargets
            };

            RenderPipelineDescriptor descriptor = new()
            {
                Layout = pipelineLayout,
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

            return this.Api.DeviceCreateRenderPipeline(this.Device, in descriptor);
        }

        /// <summary>
        /// Creates the compute pipeline used by one cached composite compute shader.
        /// </summary>
        /// <param name="pipelineLayout">The family's shared pipeline layout.</param>
        /// <param name="shaderModule">The family's shared shader module.</param>
        /// <param name="entryPoint">Null-terminated compute entry-point name.</param>
        /// <returns>The created compute pipeline, or <see langword="null"/> on failure.</returns>
        private ComputePipeline* CreateCompositeComputePipeline(
            PipelineLayout* pipelineLayout,
            ShaderModule* shaderModule,
            ReadOnlySpan<byte> entryPoint)
        {
            fixed (byte* entryPointPtr = entryPoint)
            {
                ProgrammableStageDescriptor computeState = new()
                {
                    Module = shaderModule,
                    EntryPoint = entryPointPtr
                };

                ComputePipelineDescriptor descriptor = new()
                {
                    Layout = pipelineLayout,
                    Compute = computeState
                };

                return this.Api.DeviceCreateComputePipeline(this.Device, in descriptor);
            }
        }

        /// <summary>
        /// Creates a shader module from null-terminated WGSL source bytes.
        /// </summary>
        /// <param name="shaderCode">Null-terminated WGSL source bytes.</param>
        /// <returns>The created shader module, or <see langword="null"/> on failure.</returns>
        private ShaderModule* CreateShaderModule(ReadOnlySpan<byte> shaderCode)
        {
            fixed (byte* shaderCodePtr = shaderCode)
            {
                ShaderModuleWGSLDescriptor wgslDescriptor = new()
                {
                    Chain = new ChainedStruct { SType = SType.ShaderModuleWgslDescriptor },
                    Code = shaderCodePtr
                };

                ShaderModuleDescriptor shaderDescriptor = new()
                {
                    NextInChain = (ChainedStruct*)&wgslDescriptor
                };

                return this.Api.DeviceCreateShaderModule(this.Device, in shaderDescriptor);
            }
        }

        /// <summary>
        /// Releases one cached graphics-pipeline family and every render pipeline variant it owns.
        /// </summary>
        /// <param name="infrastructure">The pipeline family to release.</param>
        private void ReleaseCompositeInfrastructure(CompositePipelineInfrastructure infrastructure)
        {
            foreach (nint pipelineHandle in infrastructure.Pipelines.Values)
            {
                if (pipelineHandle != 0)
                {
                    this.Api.RenderPipelineRelease((RenderPipeline*)pipelineHandle);
                }
            }

            infrastructure.Pipelines.Clear();

            if (infrastructure.PipelineLayout is not null)
            {
                this.Api.PipelineLayoutRelease(infrastructure.PipelineLayout);
                infrastructure.PipelineLayout = null;
            }

            if (infrastructure.ShaderModule is not null)
            {
                this.Api.ShaderModuleRelease(infrastructure.ShaderModule);
                infrastructure.ShaderModule = null;
            }

            if (infrastructure.BindGroupLayout is not null)
            {
                this.Api.BindGroupLayoutRelease(infrastructure.BindGroupLayout);
                infrastructure.BindGroupLayout = null;
            }
        }

        /// <summary>
        /// Releases one cached compute-pipeline family and the shared resources behind it.
        /// </summary>
        /// <param name="infrastructure">The pipeline family to release.</param>
        private void ReleaseCompositeComputeInfrastructure(CompositeComputePipelineInfrastructure infrastructure)
        {
            if (infrastructure.Pipeline is not null)
            {
                this.Api.ComputePipelineRelease(infrastructure.Pipeline);
                infrastructure.Pipeline = null;
            }

            if (infrastructure.PipelineLayout is not null)
            {
                this.Api.PipelineLayoutRelease(infrastructure.PipelineLayout);
                infrastructure.PipelineLayout = null;
            }

            if (infrastructure.ShaderModule is not null)
            {
                this.Api.ShaderModuleRelease(infrastructure.ShaderModule);
                infrastructure.ShaderModule = null;
            }

            if (infrastructure.BindGroupLayout is not null)
            {
                this.Api.BindGroupLayoutRelease(infrastructure.BindGroupLayout);
                infrastructure.BindGroupLayout = null;
            }
        }

        /// <summary>
        /// Stores a pooled status-readback buffer with its byte capacity so larger chunked
        /// readbacks never receive a smaller single-status buffer.
        /// </summary>
        private readonly struct StatusReadbackBufferEntry
        {
            /// <summary>
            /// Initializes a new instance of the <see cref="StatusReadbackBufferEntry"/> struct.
            /// </summary>
            /// <param name="buffer">The native buffer pointer stored as an integer.</param>
            /// <param name="byteLength">The byte capacity of <paramref name="buffer"/>.</param>
            public StatusReadbackBufferEntry(nint buffer, nuint byteLength)
            {
                this.Buffer = buffer;
                this.ByteLength = byteLength;
            }

            /// <summary>
            /// Gets the native buffer pointer stored as an integer.
            /// </summary>
            public nint Buffer { get; }

            /// <summary>
            /// Gets the byte capacity of <see cref="Buffer"/>.
            /// </summary>
            public nuint ByteLength { get; }
        }

        /// <summary>
        /// Shared render-pipeline infrastructure for compositing variants. Instances double as
        /// the per-family lock object that serializes lazy creation of their contents.
        /// </summary>
        private sealed class CompositePipelineInfrastructure
        {
            /// <summary>
            /// Gets the cached pipeline variants keyed by (texture format, blend mode).
            /// Values are stored as <see cref="nint"/> because pointer types cannot be
            /// dictionary type arguments.
            /// </summary>
            public Dictionary<(TextureFormat TextureFormat, CompositePipelineBlendMode BlendMode), nint> Pipelines { get; } = [];

            /// <summary>
            /// Gets or sets the bind-group layout shared by all variants in this family.
            /// </summary>
            public BindGroupLayout* BindGroupLayout { get; set; }

            /// <summary>
            /// Gets or sets the pipeline layout shared by all variants in this family.
            /// </summary>
            public PipelineLayout* PipelineLayout { get; set; }

            /// <summary>
            /// Gets or sets the shader module shared by all variants in this family.
            /// </summary>
            public ShaderModule* ShaderModule { get; set; }
        }

        /// <summary>
        /// Shared compute-pipeline infrastructure for one cached compute shader. Instances
        /// double as the per-family lock object that serializes lazy creation of their contents.
        /// </summary>
        private sealed class CompositeComputePipelineInfrastructure
        {
            /// <summary>
            /// Gets or sets the bind-group layout for this family.
            /// </summary>
            public BindGroupLayout* BindGroupLayout { get; set; }

            /// <summary>
            /// Gets or sets the pipeline layout for this family.
            /// </summary>
            public PipelineLayout* PipelineLayout { get; set; }

            /// <summary>
            /// Gets or sets the shader module for this family.
            /// </summary>
            public ShaderModule* ShaderModule { get; set; }

            /// <summary>
            /// Gets or sets the single cached compute pipeline for this family.
            /// </summary>
            public ComputePipeline* Pipeline { get; set; }
        }
    }
}
