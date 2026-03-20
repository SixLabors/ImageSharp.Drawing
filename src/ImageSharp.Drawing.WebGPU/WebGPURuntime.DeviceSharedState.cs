// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Collections.Concurrent;
using Silk.NET.WebGPU;
using WgpuBuffer = Silk.NET.WebGPU.Buffer;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

internal static unsafe partial class WebGPURuntime
{
    private static readonly ConcurrentDictionary<nint, DeviceSharedState> DeviceStateCache = new();

    /// <summary>
    /// Gets or creates process-scoped shared resources for the specified device.
    /// </summary>
    /// <param name="api">The WebGPU API facade used to manage native resources.</param>
    /// <param name="device">The device key and owner for the shared state.</param>
    /// <returns>The shared device state instance for <paramref name="device"/>.</returns>
    internal static DeviceSharedState GetOrCreateDeviceState(WebGPU api, Device* device)
    {
        nint cacheKey = (nint)device;
        if (DeviceStateCache.TryGetValue(cacheKey, out DeviceSharedState? existing))
        {
            return existing;
        }

        DeviceSharedState created = new(api, device);
        DeviceSharedState winner = DeviceStateCache.GetOrAdd(cacheKey, created);
        if (!ReferenceEquals(winner, created))
        {
            created.Dispose();
        }

        return winner;
    }

    /// <summary>
    /// Disposes all cached device-scoped shared state.
    /// </summary>
    private static void ClearDeviceStateCache()
    {
        foreach (DeviceSharedState state in DeviceStateCache.Values)
        {
            state.Dispose();
        }

        DeviceStateCache.Clear();
    }

    /// <summary>
    /// Shared device-scoped caches for pipelines, bind groups, and reusable GPU resources.
    /// </summary>
    internal sealed class DeviceSharedState : IDisposable
    {
        private readonly ConcurrentDictionary<string, CompositePipelineInfrastructure> compositePipelines = new(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, CompositeComputePipelineInfrastructure> compositeComputePipelines = new(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, SharedBufferInfrastructure> sharedBuffers = new(StringComparer.Ordinal);
        private readonly HashSet<FeatureName> deviceFeatures;
        private bool disposed;

        internal DeviceSharedState(WebGPU api, Device* device)
        {
            this.Api = api;
            this.Device = device;
            this.deviceFeatures = EnumerateDeviceFeatures(api, device);
        }

        private static ReadOnlySpan<byte> CompositeVertexEntryPoint => "vs_main\0"u8;

        private static ReadOnlySpan<byte> CompositeFragmentEntryPoint => "fs_main\0"u8;

        /// <summary>
        /// Gets the synchronization object used for shared state mutation.
        /// </summary>
        public object SyncRoot { get; } = new();

        /// <summary>
        /// Gets the WebGPU API instance used by this shared state.
        /// </summary>
        public WebGPU Api { get; }

        /// <summary>
        /// Gets the device associated with this shared state.
        /// </summary>
        public Device* Device { get; }

        /// <summary>
        /// Returns whether the device has the specified feature.
        /// </summary>
        /// <param name="feature">The feature to check.</param>
        /// <returns><see langword="true"/> when the device has the feature; otherwise <see langword="false"/>.</returns>
        public bool HasFeature(FeatureName feature)
            => this.deviceFeatures.Contains(feature);

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
        /// Gets or creates a graphics pipeline used for composite rendering.
        /// </summary>
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

            if (this.disposed)
            {
                error = "WebGPU device state is disposed.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(pipelineKey))
            {
                error = "Composite pipeline key cannot be empty.";
                return false;
            }

            if (shaderCode.IsEmpty)
            {
                error = $"Composite shader code is missing for pipeline '{pipelineKey}'.";
                return false;
            }

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
        /// Gets or creates a compute pipeline used for composite execution.
        /// </summary>
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

            if (this.disposed)
            {
                error = "WebGPU device state is disposed.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(pipelineKey))
            {
                error = "Composite compute pipeline key cannot be empty.";
                return false;
            }

            if (shaderCode.IsEmpty)
            {
                error = $"Composite compute shader code is missing for pipeline '{pipelineKey}'.";
                return false;
            }

            if (entryPoint.IsEmpty)
            {
                error = $"Composite compute entry point is missing for pipeline '{pipelineKey}'.";
                return false;
            }

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
        /// Gets or creates a reusable shared buffer for device-scoped operations.
        /// </summary>
        public bool TryGetOrCreateSharedBuffer(
            string bufferKey,
            BufferUsage usage,
            nuint requiredSize,
            out WgpuBuffer* buffer,
            out nuint capacity,
            out string? error)
        {
            buffer = null;
            capacity = 0;

            if (this.disposed)
            {
                error = "WebGPU device state is disposed.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(bufferKey))
            {
                error = "Shared buffer key cannot be empty.";
                return false;
            }

            if (requiredSize == 0)
            {
                error = $"Shared buffer '{bufferKey}' requires a non-zero size.";
                return false;
            }

            SharedBufferInfrastructure infrastructure = this.sharedBuffers.GetOrAdd(
                bufferKey,
                static _ => new SharedBufferInfrastructure());
            lock (infrastructure)
            {
                if (infrastructure.Buffer is not null &&
                    infrastructure.Capacity >= requiredSize &&
                    infrastructure.Usage == usage)
                {
                    buffer = infrastructure.Buffer;
                    capacity = infrastructure.Capacity;
                    error = null;
                    return true;
                }

                if (infrastructure.Buffer is not null)
                {
                    this.Api.BufferRelease(infrastructure.Buffer);
                    infrastructure.Buffer = null;
                    infrastructure.Capacity = 0;
                }

                BufferDescriptor descriptor = new()
                {
                    Usage = usage,
                    Size = requiredSize
                };

                WgpuBuffer* createdBuffer = this.Api.DeviceCreateBuffer(this.Device, in descriptor);
                if (createdBuffer is null)
                {
                    error = $"Failed to create shared buffer '{bufferKey}'.";
                    return false;
                }

                infrastructure.Buffer = createdBuffer;
                infrastructure.Capacity = requiredSize;
                infrastructure.Usage = usage;
                buffer = createdBuffer;
                capacity = requiredSize;
                error = null;
                return true;
            }
        }

        /// <summary>
        /// Releases all cached pipelines and buffers owned by this state.
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

            foreach (SharedBufferInfrastructure infrastructure in this.sharedBuffers.Values)
            {
                lock (infrastructure)
                {
                    if (infrastructure.Buffer is not null)
                    {
                        this.Api.BufferRelease(infrastructure.Buffer);
                        infrastructure.Buffer = null;
                        infrastructure.Capacity = 0;
                    }
                }
            }

            this.sharedBuffers.Clear();

            this.disposed = true;
        }

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
        /// Shared render-pipeline infrastructure for compositing variants.
        /// </summary>
        private sealed class CompositePipelineInfrastructure
        {
            public Dictionary<(TextureFormat TextureFormat, CompositePipelineBlendMode BlendMode), nint> Pipelines { get; } = [];

            public BindGroupLayout* BindGroupLayout { get; set; }

            public PipelineLayout* PipelineLayout { get; set; }

            public ShaderModule* ShaderModule { get; set; }
        }

        private sealed class CompositeComputePipelineInfrastructure
        {
            public BindGroupLayout* BindGroupLayout { get; set; }

            public PipelineLayout* PipelineLayout { get; set; }

            public ShaderModule* ShaderModule { get; set; }

            public ComputePipeline* Pipeline { get; set; }
        }

        private sealed class SharedBufferInfrastructure
        {
            public WgpuBuffer* Buffer { get; set; }

            public nuint Capacity { get; set; }

            public BufferUsage Usage { get; set; }
        }
    }
}
