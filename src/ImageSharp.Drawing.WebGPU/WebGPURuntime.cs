// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using SixLabors.ImageSharp.Drawing.Processing.Backends.Native;
using FilePath = System.IO.Path;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Process-level WebGPU API runtime.
/// </summary>
/// <remarks>
/// <para>
/// This type owns the process-level <see cref="WebGPU"/> API facade and a lazily provisioned
/// default device/queue pair used by the GPU backend when no native surface is available.
/// </para>
/// <para>
/// Backends use <see cref="GetApi"/> to access the shared WebGPU loader and
/// <see cref="TryGetOrCreateDevice"/> to use the cached default device/queue pair.
/// </para>
/// <para>
/// Runtime cleanup happens automatically on process exit.
/// </para>
/// </remarks>
internal static unsafe partial class WebGPURuntime
{
    /// <summary>
    /// Serializes probe execution and guards the cached probe results.
    /// Probes hold this lock while provisioning runs, so a probe may acquire
    /// <see cref="Sync"/> while holding <see cref="ProbeSync"/>. The global lock
    /// order is therefore <see cref="ProbeSync"/> first, then <see cref="Sync"/>;
    /// every path that needs both must acquire them in that order.
    /// </summary>
    private static readonly object ProbeSync = new();

    /// <summary>
    /// Synchronizes all runtime state transitions: API/extension loading, the cached
    /// default device/queue pair, and process-exit teardown.
    /// </summary>
    private static readonly object Sync = new();

    /// <summary>
    /// Process-level WebGPU API loader.
    /// </summary>
    private static WebGPU? api;

    /// <summary>
    /// Lazily provisioned device handle for CPU-backed frames. Owned by the runtime and
    /// disposed during process-exit teardown; callers must never dispose it.
    /// </summary>
    private static WebGPUDeviceHandle? autoDeviceHandle;

    /// <summary>
    /// Lazily provisioned queue handle for CPU-backed frames. Owned by the runtime and
    /// disposed during process-exit teardown; callers must never dispose it.
    /// </summary>
    private static WebGPUQueueHandle? autoQueueHandle;

    /// <summary>
    /// Process-shared WebGPU instance used by every native surface. Owned by the runtime and
    /// released during process-exit teardown; callers must never release it.
    /// </summary>
    private static WGPUInstanceImpl* sharedInstance;

    /// <summary>
    /// Tracks whether the process-exit hook has been installed. Guarded by <see cref="Sync"/>.
    /// </summary>
    private static bool processExitHooked;

    /// <summary>
    /// Cached result of <see cref="ProbeAvailability"/>; <see langword="null"/> until the first
    /// probe runs. Guarded by <see cref="ProbeSync"/>.
    /// </summary>
    private static WebGPUEnvironmentError? availabilityProbeResult;

    /// <summary>
    /// Cached result of <see cref="ProbeComputePipelineSupport"/>; <see langword="null"/> until
    /// the first probe runs. Guarded by <see cref="ProbeSync"/>.
    /// </summary>
    private static WebGPUEnvironmentError? computePipelineProbeResult;

    /// <summary>
    /// Timeout for asynchronous WebGPU callbacks.
    /// </summary>
    private const int CallbackTimeoutMilliseconds = 10_000;

    /// <summary>
    /// Gets the shared WebGPU API loader, initializing the runtime on first use.
    /// </summary>
    /// <returns>The shared WebGPU API loader.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the WebGPU API cannot be initialized.</exception>
    public static WebGPU GetApi()
    {
        lock (Sync)
        {
            EnsureInitialized();
            if (api is null)
            {
                throw new InvalidOperationException("WebGPU.GetApi returned null.");
            }

            return api;
        }
    }

    /// <summary>
    /// Gets the process-shared WebGPU instance, creating it on first use.
    /// </summary>
    /// <remarks>
    /// wgpu-native keeps a single global backend registry, so every native surface shares one
    /// instance for the process lifetime. Creating and destroying an instance per surface (per
    /// window) churns that global state and, under rapid window reopen, leaves a freshly created
    /// surface invalid, which wgpu reports by aborting the process. The runtime owns the instance
    /// and releases it during process-exit teardown; callers must never release it.
    /// </remarks>
    /// <returns>The shared native instance pointer.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the WebGPU API or instance cannot be initialized.</exception>
    public static WGPUInstanceImpl* GetOrCreateSharedInstance()
    {
        lock (Sync)
        {
            EnsureInitialized();
            if (api is null)
            {
                throw new InvalidOperationException("WebGPU.GetApi returned null.");
            }

            if (sharedInstance is null)
            {
                if (!TryGetDxcPaths(out _, out _))
                {
                    throw new InvalidOperationException("The packaged DirectX Shader Compiler runtime is unavailable.");
                }

                WGPUInstanceImpl* created = CreateConfiguredInstance(api);
                if (created is null)
                {
                    throw new InvalidOperationException("WebGPU instance creation failed.");
                }

                sharedInstance = created;
            }

            return sharedInstance;
        }
    }

    /// <summary>
    /// Lazily provisions and caches a default device/queue pair for CPU-backed frames.
    /// Returns cached handles on subsequent calls.
    /// </summary>
    /// <param name="device">Receives the device pointer on success.</param>
    /// <param name="queue">Receives the queue pointer on success.</param>
    /// <param name="errorCode">Receives the stable failure code on error.</param>
    /// <returns><see langword="true"/> when handles are available; otherwise <see langword="false"/>.</returns>
    /// <remarks>
    /// Thread safe; all provisioning happens under <see cref="Sync"/>. The returned handles stay
    /// owned by the runtime and are disposed at process exit, so callers must not dispose them.
    /// </remarks>
    public static bool TryGetOrCreateDevice(
        out WebGPUDeviceHandle? device,
        out WebGPUQueueHandle? queue,
        out WebGPUEnvironmentError errorCode)
    {
        lock (Sync)
        {
            // Handles are published only after runtime initialization has acquired the required
            // extension. Teardown clears the handles and extension together under this same lock,
            // so cached handles already prove the complete environment invariant.
            if (autoDeviceHandle is not null && autoQueueHandle is not null)
            {
                device = autoDeviceHandle;
                queue = autoQueueHandle;
                errorCode = WebGPUEnvironmentError.Success;
                return true;
            }

            if (!TryGetDxcPaths(out _, out _))
            {
                device = null;
                queue = null;
                errorCode = WebGPUEnvironmentError.DxcUnavailable;
                return false;
            }

            try
            {
                EnsureInitialized();
            }
            catch
            {
                device = null;
                queue = null;
                errorCode = WebGPUEnvironmentError.ApiInitializationFailed;
                return false;
            }

            if (api is null)
            {
                device = null;
                queue = null;
                errorCode = WebGPUEnvironmentError.ApiInitializationFailed;
                return false;
            }

            // Provision: instance -> adapter -> device -> queue.
            // The instance and adapter are transient; only the device and queue are cached.
            WGPUInstanceImpl* instance = CreateConfiguredInstance(api);
            if (instance is null)
            {
                device = null;
                queue = null;
                errorCode = WebGPUEnvironmentError.InstanceCreationFailed;
                return false;
            }

            WGPUAdapterImpl* adapter = null;
            WGPUDeviceImpl* requestedDevice = null;
            WGPUQueueImpl* requestedQueue = null;
            bool initialized = false;
            try
            {
                if (!TryRequestAdapter(api, instance, null, out adapter, out errorCode))
                {
                    device = null;
                    queue = null;
                    return false;
                }

                if (!TryRequestDevice(api, adapter, out requestedDevice, out errorCode))
                {
                    device = null;
                    queue = null;
                    return false;
                }

                requestedQueue = api.DeviceGetQueue(requestedDevice);
                if (requestedQueue is null)
                {
                    device = null;
                    queue = null;
                    errorCode = WebGPUEnvironmentError.QueueAcquisitionFailed;
                    return false;
                }

                // DevicePoll is a wgpu-native extension entry point required by every asynchronous
                // readback path. Probe it before publishing the device so availability cannot report
                // success against an incompatible native library found by the operating system.
                try
                {
                    _ = api.DevicePoll(requestedDevice, false, null);
                }
                catch (EntryPointNotFoundException)
                {
                    device = null;
                    queue = null;
                    errorCode = WebGPUEnvironmentError.WgpuExtensionUnavailable;
                    return false;
                }

                // Cache for subsequent calls.
                autoDeviceHandle = new WebGPUDeviceHandle(api, (nint)requestedDevice, ownsHandle: true);
                autoQueueHandle = new WebGPUQueueHandle(api, (nint)requestedQueue, ownsHandle: true);
                device = autoDeviceHandle;
                queue = autoQueueHandle;
                errorCode = WebGPUEnvironmentError.Success;
                initialized = true;
                return true;
            }
            finally
            {
                // Always release transient handles.
                if (adapter is not null)
                {
                    api.AdapterRelease(adapter);
                }

                api.InstanceRelease(instance);

                // On failure, release any partially provisioned handles.
                if (!initialized)
                {
                    if (requestedQueue is not null)
                    {
                        api.QueueRelease(requestedQueue);
                    }

                    if (requestedDevice is not null)
                    {
                        api.DeviceRelease(requestedDevice);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Probes whether the current process can initialize WebGPU with the required WGPU extension
    /// and provision a device/queue pair.
    /// </summary>
    /// <returns>
    /// <see cref="WebGPUEnvironmentError.Success"/> when the required WGPU extension and basic WebGPU device acquisition
    /// are available; otherwise, the failure code.
    /// </returns>
    /// <remarks>
    /// This is the broad availability check. It answers only whether the process can initialize the required native
    /// WebGPU entry points and acquire a device and queue. It deliberately stops before shader-module or pipeline creation.
    /// Callers that only need to know whether native WebGPU interop exists should use this probe. Callers that need the staged compute backend must additionally use
    /// <see cref="WebGPUEnvironment.ProbeComputePipelineSupport()"/>, because successful device acquisition does not guarantee
    /// that compute-pipeline creation is actually usable on the active runtime/driver stack.
    /// </remarks>
    public static WebGPUEnvironmentError ProbeAvailability()
    {
        lock (ProbeSync)
        {
            if (availabilityProbeResult.HasValue)
            {
                return availabilityProbeResult.Value;
            }

            try
            {
                availabilityProbeResult = TryGetOrCreateDevice(out _, out _, out WebGPUEnvironmentError errorCode)
                    ? WebGPUEnvironmentError.Success
                    : errorCode;
            }
            catch (InvalidOperationException)
            {
                availabilityProbeResult = WebGPUEnvironmentError.ApiInitializationFailed;
            }
            catch
            {
                availabilityProbeResult = WebGPUEnvironmentError.DeviceAcquisitionFailed;
            }

            return availabilityProbeResult.Value;
        }
    }

    /// <summary>
    /// Probes whether the staged WebGPU backend can create a trivial compute pipeline.
    /// </summary>
    /// <returns><see cref="WebGPUEnvironmentError.Success"/> when the compute path is usable; otherwise the failure code.</returns>
    /// <remarks>
    /// This probe is intentionally separate from <see cref="WebGPUEnvironment.ProbeAvailability()"/>. Some environments can
    /// create a device successfully and still fail, or even crash natively, when the first compute pipeline is created.
    /// The availability probe remains the cheaper prerequisite check, while this method performs the stronger staged-backend
    /// validation and isolates the actual pipeline creation in a remote process when possible so a native failure becomes
    /// a probe result instead of taking down the caller.
    /// </remarks>
    public static WebGPUEnvironmentError ProbeComputePipelineSupport()
    {
        lock (ProbeSync)
        {
            if (computePipelineProbeResult.HasValue)
            {
                return computePipelineProbeResult.Value;
            }

            WebGPUEnvironmentError availabilityResult = ProbeAvailability();
            if (availabilityResult != WebGPUEnvironmentError.Success)
            {
                computePipelineProbeResult = availabilityResult;
                return computePipelineProbeResult.Value;
            }

            // Without process isolation the pipeline-creation attempt could crash natively and
            // take the caller down with it, so skip the risky step and report success based on
            // the availability probe alone.
            if (!RemoteExecutor.IsSupported)
            {
                computePipelineProbeResult = WebGPUEnvironmentError.Success;
                return computePipelineProbeResult.Value;
            }

            int exitCode = RemoteExecutor.Invoke(RunComputePipelineSupportProbe);
            computePipelineProbeResult = exitCode switch
            {
                0 => WebGPUEnvironmentError.Success,

                // Codes one through nine are reserved for probe stages that completed and
                // reported the adapter unsupported; everything else means the isolated
                // process died mid-probe.
                >= 1 and <= 9 => WebGPUEnvironmentError.ComputePipelineCreationFailed,
                _ => WebGPUEnvironmentError.ComputePipelineProbeProcessFailed,
            };
            return computePipelineProbeResult.Value;
        }
    }

    /// <summary>
    /// Executes one isolated compute-pipeline creation and submission-readback probe for
    /// <see cref="WebGPUEnvironment.ProbeComputePipelineSupport()"/>.
    /// </summary>
    /// <returns>
    /// <c>0</c> when pipeline creation and an indexed submission with buffer readback succeeded;
    /// <c>1</c> when the probe completed and reported failure.
    /// Any other value means the isolated probe process terminated before the probe could return normally.
    /// </returns>
    public static int RunComputePipelineSupportProbe()
    {
        try
        {
            if (!TryGetOrCreateDevice(out WebGPUDeviceHandle? deviceHandle, out WebGPUQueueHandle? queueHandle, out _)
                || deviceHandle is null
                || queueHandle is null)
            {
                return 1;
            }

            WebGPU api = GetApi();
            using WebGPUHandle.HandleReference deviceReference = deviceHandle.AcquireReference();
            using WebGPUHandle.HandleReference queueReference = queueHandle.AcquireReference();
            WGPUDeviceImpl* device = (WGPUDeviceImpl*)deviceReference.Handle;
            WGPUQueueImpl* queue = (WGPUQueueImpl*)queueReference.Handle;

            ReadOnlySpan<byte> probeShader = "@compute @workgroup_size(1) fn main() {}\0"u8;

            fixed (byte* shaderCodePtr = probeShader)
            {
                WGPUShaderSourceWGSL wgslDescriptor = new()
                {
                    chain = new WGPUChainedStruct { sType = WGPUSType.ShaderSourceWGSL },
                    code = shaderCodePtr
                };

                WGPUShaderModuleDescriptor shaderDescriptor = new()
                {
                    nextInChain = (WGPUChainedStruct*)&wgslDescriptor
                };

                WGPUShaderModuleImpl* shaderModule = api.DeviceCreateShaderModule(device, in shaderDescriptor);
                if (shaderModule is null)
                {
                    return 1;
                }

                try
                {
                    ReadOnlySpan<byte> entryPoint = "main\0"u8;
                    fixed (byte* entryPointPtr = entryPoint)
                    {
                        WGPUComputeState computeStage = new()
                        {
                            module = shaderModule,
                            entryPoint = entryPointPtr
                        };

                        WGPUPipelineLayoutDescriptor layoutDescriptor = new()
                        {
                            bindGroupLayoutCount = 0,
                            bindGroupLayouts = null
                        };

                        WGPUPipelineLayoutImpl* pipelineLayout = api.DeviceCreatePipelineLayout(device, in layoutDescriptor);
                        if (pipelineLayout is null)
                        {
                            return 1;
                        }

                        try
                        {
                            WGPUComputePipelineDescriptor pipelineDescriptor = new()
                            {
                                layout = pipelineLayout,
                                compute = computeStage
                            };

                            WGPUComputePipelineImpl* pipeline = api.DeviceCreateComputePipeline(device, in pipelineDescriptor);
                            if (pipeline is null)
                            {
                                return 1;
                            }

                            api.ComputePipelineRelease(pipeline);

                            // Pipeline creation and even trivial submissions succeed on adapters
                            // whose execution or render path later fails natively, so the probe
                            // proves each deeper capability in turn. The distinct exit codes name
                            // the first stage that failed.
                            if (!ProbeSubmissionReadback(api, device, queue))
                            {
                                return 2;
                            }

                            if (!ProbeComputeDispatch(api, device, queue))
                            {
                                return 3;
                            }

                            return ProbeSceneRenderReadback() ? 0 : 4;
                        }
                        finally
                        {
                            api.PipelineLayoutRelease(pipelineLayout);
                        }
                    }
                }
                finally
                {
                    api.ShaderModuleRelease(shaderModule);
                }
            }
        }
        catch
        {
            return 1;
        }
    }

    /// <summary>
    /// Exercises one indexed queue submission and buffer readback against the probe device.
    /// </summary>
    /// <param name="api">The WebGPU API loader.</param>
    /// <param name="device">The probe device.</param>
    /// <param name="queue">The probe device's queue.</param>
    /// <returns><see langword="true"/> when the submission completed and the readback buffer mapped.</returns>
    private static bool ProbeSubmissionReadback(WebGPU api, WGPUDeviceImpl* device, WGPUQueueImpl* queue)
    {
        // Buffer-to-buffer copies require four-byte multiples; one readback row-alignment unit
        // keeps the probe within every adapter's minimum resource limits.
        const ulong probeByteCount = 256;

        WGPUBufferImpl* sourceBuffer = null;
        WGPUBufferImpl* readbackBuffer = null;
        WGPUCommandEncoderImpl* commandEncoder = null;
        WGPUCommandBufferImpl* commandBuffer = null;
        try
        {
            WGPUBufferDescriptor sourceDescriptor = new()
            {
                usage = (ulong)BufferUsage.CopySrc,
                size = probeByteCount,
                mappedAtCreation = 0U,
            };

            WGPUBufferDescriptor readbackDescriptor = new()
            {
                usage = (ulong)(BufferUsage.CopyDst | BufferUsage.MapRead),
                size = probeByteCount,
                mappedAtCreation = 0U,
            };

            sourceBuffer = api.DeviceCreateBuffer(device, in sourceDescriptor);
            readbackBuffer = api.DeviceCreateBuffer(device, in readbackDescriptor);
            if (sourceBuffer is null || readbackBuffer is null)
            {
                return false;
            }

            WGPUCommandEncoderDescriptor encoderDescriptor = default;
            commandEncoder = api.DeviceCreateCommandEncoder(device, in encoderDescriptor);
            if (commandEncoder is null)
            {
                return false;
            }

            // The copy is the smallest command whose completion is observable from the host
            // through a mappable buffer, making the submission's progress provable.
            api.CommandEncoderCopyBufferToBuffer(commandEncoder, sourceBuffer, 0, readbackBuffer, 0, probeByteCount);

            WGPUCommandBufferDescriptor commandBufferDescriptor = default;
            commandBuffer = api.CommandEncoderFinish(commandEncoder, in commandBufferDescriptor);
            if (commandBuffer is null)
            {
                return false;
            }

            ulong submissionIndex = api.QueueSubmitForIndex(queue, 1, ref commandBuffer);
            return TryMapProbeBuffer(api, device, readbackBuffer, (nuint)probeByteCount, submissionIndex);
        }
        finally
        {
            if (commandBuffer is not null)
            {
                api.CommandBufferRelease(commandBuffer);
            }

            if (commandEncoder is not null)
            {
                api.CommandEncoderRelease(commandEncoder);
            }

            if (readbackBuffer is not null)
            {
                api.BufferRelease(readbackBuffer);
            }

            if (sourceBuffer is not null)
            {
                api.BufferRelease(sourceBuffer);
            }
        }
    }

    /// <summary>
    /// Maps a probe readback buffer after its submission and verifies readable bytes.
    /// </summary>
    /// <param name="api">The WebGPU API loader.</param>
    /// <param name="device">The probe device.</param>
    /// <param name="readbackBuffer">The mappable buffer written by the probe submission.</param>
    /// <param name="byteCount">The buffer length in bytes.</param>
    /// <param name="submissionIndex">The submission the map depends on.</param>
    /// <returns><see langword="true"/> when the buffer mapped with readable data.</returns>
    private static bool TryMapProbeBuffer(
        WebGPU api,
        WGPUDeviceImpl* device,
        WGPUBufferImpl* readbackBuffer,
        nuint byteCount,
        ulong submissionIndex)
    {
        WGPUMapAsyncStatus mapStatus = default;
        using ManualResetEventSlim mapReady = new(false);
        void Callback(WGPUMapAsyncStatus status, void* userData)
        {
            _ = userData;
            mapStatus = status;
            mapReady.Set();
        }

        using WebGPUBufferMapCallback callback = WebGPUBufferMapCallback.From(Callback);
        api.BufferMapAsync(readbackBuffer, MapMode.Read, 0, byteCount, callback, null);

        // Non-blocking polls scoped to the submission index mirror the renderer's readback
        // wait, and the managed timeout turns an adapter that never completes the submission
        // into a probe failure instead of a hang.
        Stopwatch stopwatch = Stopwatch.StartNew();
        while (!mapReady.IsSet && stopwatch.ElapsedMilliseconds < CallbackTimeoutMilliseconds)
        {
            _ = api.DevicePoll(device, false, &submissionIndex);
            if (!mapReady.IsSet)
            {
                Thread.Yield();
            }
        }

        // The status keeps its default value when the callback never fired, so timeouts and
        // explicit non-success map results fail through the same check.
        if (!mapReady.IsSet || mapStatus != WGPUMapAsyncStatus.Success)
        {
            return false;
        }

        void* mapped = api.BufferGetConstMappedRange(readbackBuffer, 0, byteCount);
        if (mapped is null)
        {
            return false;
        }

        api.BufferUnmap(readbackBuffer);
        return true;
    }

    /// <summary>
    /// Executes the chunk-reset production shader once and proves its submission completes.
    /// </summary>
    /// <param name="api">The WebGPU API loader.</param>
    /// <param name="device">The probe device.</param>
    /// <param name="queue">The probe device's queue.</param>
    /// <returns><see langword="true"/> when the dispatch completed and the readback mapped.</returns>
    /// <remarks>
    /// The chunk-reset stage is the smallest production shader: one workgroup, one thread, one
    /// storage binding. Executing it proves the adapter can run the full shader compilation
    /// chain, which pipeline creation alone does not.
    /// </remarks>
    private static bool ProbeComputeDispatch(WebGPU api, WGPUDeviceImpl* device, WGPUQueueImpl* queue)
    {
        // The chunk-reset shader's single binding is one bump-allocators block, so the storage
        // buffer must be exactly that size for the bind-group layout's minimum binding size.
        nuint statusByteCount = (nuint)sizeof(GpuSceneBumpAllocators);

        WGPUBufferImpl* storageBuffer = null;
        WGPUBufferImpl* readbackBuffer = null;
        WGPUShaderModuleImpl* shaderModule = null;
        WGPUBindGroupLayoutImpl* bindGroupLayout = null;
        WGPUPipelineLayoutImpl* pipelineLayout = null;
        WGPUComputePipelineImpl* pipeline = null;
        WGPUBindGroupImpl* bindGroup = null;
        WGPUCommandEncoderImpl* commandEncoder = null;
        WGPUCommandBufferImpl* commandBuffer = null;
        try
        {
            WGPUBufferDescriptor storageDescriptor = new()
            {
                usage = (ulong)(BufferUsage.Storage | BufferUsage.CopySrc),
                size = statusByteCount,
                mappedAtCreation = 0U,
            };

            WGPUBufferDescriptor readbackDescriptor = new()
            {
                usage = (ulong)(BufferUsage.CopyDst | BufferUsage.MapRead),
                size = statusByteCount,
                mappedAtCreation = 0U,
            };

            // The shader writes the storage buffer, the copy drains it into the mappable
            // readback buffer, and the map proves the whole chain executed on the adapter.
            storageBuffer = api.DeviceCreateBuffer(device, in storageDescriptor);
            readbackBuffer = api.DeviceCreateBuffer(device, in readbackDescriptor);
            if (storageBuffer is null || readbackBuffer is null)
            {
                return false;
            }

            fixed (byte* shaderCodePtr = ChunkResetComputeShader.ShaderCode)
            {
                fixed (byte* entryPointPtr = ChunkResetComputeShader.EntryPoint)
                {
                    WGPUShaderSourceWGSL wgslDescriptor = new()
                    {
                        chain = new WGPUChainedStruct { sType = WGPUSType.ShaderSourceWGSL },
                        code = shaderCodePtr
                    };

                    WGPUShaderModuleDescriptor shaderDescriptor = new()
                    {
                        nextInChain = (WGPUChainedStruct*)&wgslDescriptor
                    };

                    shaderModule = api.DeviceCreateShaderModule(device, in shaderDescriptor);
                    if (shaderModule is null
                        || !ChunkResetComputeShader.TryCreateBindGroupLayout(api, device, out bindGroupLayout, out _))
                    {
                        return false;
                    }

                    WGPUBindGroupLayoutImpl** layouts = stackalloc WGPUBindGroupLayoutImpl*[1] { bindGroupLayout };
                    WGPUPipelineLayoutDescriptor layoutDescriptor = new()
                    {
                        bindGroupLayoutCount = 1,
                        bindGroupLayouts = layouts
                    };

                    pipelineLayout = api.DeviceCreatePipelineLayout(device, in layoutDescriptor);
                    if (pipelineLayout is null)
                    {
                        return false;
                    }

                    WGPUComputePipelineDescriptor pipelineDescriptor = new()
                    {
                        layout = pipelineLayout,
                        compute = new WGPUComputeState
                        {
                            module = shaderModule,
                            entryPoint = entryPointPtr
                        }
                    };

                    pipeline = api.DeviceCreateComputePipeline(device, in pipelineDescriptor);
                    if (pipeline is null)
                    {
                        return false;
                    }
                }
            }

            // Bind-group creation copies the entries during the call, so stack storage is safe.
            WGPUBindGroupEntry* bindEntries = stackalloc WGPUBindGroupEntry[1];
            bindEntries[0] = new WGPUBindGroupEntry
            {
                binding = 0,
                buffer = storageBuffer,
                offset = 0,
                size = statusByteCount
            };

            WGPUBindGroupDescriptor bindGroupDescriptor = new()
            {
                layout = bindGroupLayout,
                entryCount = 1,
                entries = bindEntries
            };

            bindGroup = api.DeviceCreateBindGroup(device, in bindGroupDescriptor);
            if (bindGroup is null)
            {
                return false;
            }

            WGPUCommandEncoderDescriptor encoderDescriptor = default;
            commandEncoder = api.DeviceCreateCommandEncoder(device, in encoderDescriptor);
            if (commandEncoder is null)
            {
                return false;
            }

            WGPUComputePassDescriptor passDescriptor = default;
            WGPUComputePassEncoderImpl* pass = api.CommandEncoderBeginComputePass(commandEncoder, in passDescriptor);
            if (pass is null)
            {
                return false;
            }

            // The pass must be ended and released before the encoder can record the copy;
            // an open pass makes every later encoder command invalid.
            api.ComputePassEncoderSetPipeline(pass, pipeline);
            api.ComputePassEncoderSetBindGroup(pass, 0, bindGroup, 0, null);
            api.ComputePassEncoderDispatchWorkgroups(pass, ChunkResetComputeShader.GetDispatchX(), 1, 1);
            api.ComputePassEncoderEnd(pass);
            api.ComputePassEncoderRelease(pass);

            // Queue ordering guarantees the dispatch's writes are visible to the copy inside
            // the same submission, so no host-side synchronization sits between them.
            api.CommandEncoderCopyBufferToBuffer(commandEncoder, storageBuffer, 0, readbackBuffer, 0, statusByteCount);

            WGPUCommandBufferDescriptor commandBufferDescriptor = default;
            commandBuffer = api.CommandEncoderFinish(commandEncoder, in commandBufferDescriptor);
            if (commandBuffer is null)
            {
                return false;
            }

            ulong submissionIndex = api.QueueSubmitForIndex(queue, 1, ref commandBuffer);
            return TryMapProbeBuffer(api, device, readbackBuffer, statusByteCount, submissionIndex);
        }
        finally
        {
            // Releases run in reverse creation order so no object outlives one it references.
            if (commandBuffer is not null)
            {
                api.CommandBufferRelease(commandBuffer);
            }

            if (commandEncoder is not null)
            {
                api.CommandEncoderRelease(commandEncoder);
            }

            if (bindGroup is not null)
            {
                api.BindGroupRelease(bindGroup);
            }

            if (pipeline is not null)
            {
                api.ComputePipelineRelease(pipeline);
            }

            if (pipelineLayout is not null)
            {
                api.PipelineLayoutRelease(pipelineLayout);
            }

            if (bindGroupLayout is not null)
            {
                api.BindGroupLayoutRelease(bindGroupLayout);
            }

            if (shaderModule is not null)
            {
                api.ShaderModuleRelease(shaderModule);
            }

            if (readbackBuffer is not null)
            {
                api.BufferRelease(readbackBuffer);
            }

            if (storageBuffer is not null)
            {
                api.BufferRelease(storageBuffer);
            }
        }
    }

    /// <summary>
    /// Renders one small retained scene containing a layer boundary and reads the result back.
    /// </summary>
    /// <returns><see langword="true"/> when the render completed and the target read back.</returns>
    /// <remarks>
    /// Layered retained scenes drive intermediate targets through the full submission and
    /// readback pipeline, which is the workload software adapters fail on while passing
    /// simpler probes.
    /// </remarks>
    private static bool ProbeSceneRenderReadback()
    {
        try
        {
            DrawingBackendScene? scene = null;
            try
            {
                // A fill plus a blended layer is the smallest scene that records a layer
                // boundary into the retained command stream, so its replay drives the full
                // scheduling, fine, and composition pipeline the renderer uses.
                using (WebGPURenderTarget sceneTarget = new(16, 16))
                using (DrawingCanvas sceneCanvas = sceneTarget.CreateCanvas())
                {
                    sceneCanvas.Fill(Brushes.Solid(Color.Red), new RectanglePolygon(2, 2, 12, 12));
                    sceneCanvas.SaveLayer(new GraphicsOptions { BlendPercentage = 0.65F }, new Rectangle(4, 4, 8, 8));
                    sceneCanvas.Fill(Brushes.Solid(Color.Blue), new RectanglePolygon(5, 5, 8, 8));
                    sceneCanvas.Restore();
                    scene = sceneCanvas.CreateScene();
                }

                using WebGPURenderTarget renderTarget = new(16, 16);
                using (DrawingCanvas canvas = renderTarget.CreateCanvas())
                {
                    canvas.RenderScene(scene);
                }

                using Image<PixelFormats.Rgba32> image = renderTarget.ReadbackImage<PixelFormats.Rgba32>();
                return image.Width == 16;
            }
            finally
            {
                scene?.Dispose();
            }
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Releases all runtime-owned GPU state and cached probe results. The process-wide loader
    /// wrappers remain rooted until operating-system process teardown.
    /// Callers must hold <see cref="Sync"/>.
    /// </summary>
    private static void DisposeRuntimeCore()
    {
        // Device shared state holds references on the device handles, so it must be released
        // first; otherwise disposing the auto handles below could not drain their refcounts
        // and the native device would stay open.
        ClearDeviceStateCache();

        if (api is not null)
        {
            autoQueueHandle?.Dispose();
            autoDeviceHandle?.Dispose();

            // Released last among GPU objects: every surface that borrowed it is disposed by now,
            // and non-owning surface instance handles never release it.
            if (sharedInstance is not null)
            {
                api.InstanceRelease(sharedInstance);
            }
        }

        autoDeviceHandle = null;
        autoQueueHandle = null;
        sharedInstance = null;

        lock (ProbeSync)
        {
            availabilityProbeResult = null;
            computePipelineProbeResult = null;
        }

        // DllImport keeps the native module loaded until normal process teardown. It must not be
        // explicitly unloaded while a platform window can still dispatch native callbacks.
    }

    /// <summary>
    /// Loads the shared API facade and installs the process-exit teardown hook.
    /// Callers must hold <see cref="Sync"/>; the exit hook re-acquires the lock when it fires.
    /// </summary>
    private static void EnsureInitialized()
    {
        if (!processExitHooked)
        {
            AppDomain.CurrentDomain.ProcessExit += (_, _) =>
            {
                // Acquire in the global lock order (ProbeSync, then Sync). The probes hold
                // ProbeSync and then enter Sync via TryGetOrCreateDevice, so taking Sync
                // first here would deadlock against a probe racing process exit. The inner
                // lock (ProbeSync) in DisposeRuntimeCore is then a reentrant acquisition.
                lock (ProbeSync)
                {
                    lock (Sync)
                    {
                        DisposeRuntimeCore();
                    }
                }
            };

            processExitHooked = true;
        }

        api ??= WebGPU.GetApi();
        if (api is null)
        {
            throw new InvalidOperationException("WebGPU.GetApi returned null.");
        }
    }

    /// <summary>
    /// Creates an instance configured to use the packaged DirectX Shader Compiler on Windows.
    /// </summary>
    /// <param name="webGpu">The WebGPU API used to create the instance.</param>
    /// <returns>The created instance, or <see langword="null"/> when creation fails.</returns>
    private static WGPUInstanceImpl* CreateConfiguredInstance(WebGPU webGpu)
    {
        WGPUInstanceDescriptor descriptor = default;
        if (!OperatingSystem.IsWindows())
        {
            return webGpu.CreateInstance(&descriptor);
        }

        _ = TryGetDxcPaths(out string dxcPath, out _);
        byte[] dxcPathBytes = Encoding.UTF8.GetBytes(dxcPath + '\0');

        fixed (byte* dxcPathPointer = dxcPathBytes)
        {
            // DXC applies only to DX12, so select that backend explicitly. This also prevents
            // unused Windows backends from reporting failed HWND capability probes before the
            // runtime selects the valid DX12 adapter.
            WGPUInstanceExtras extras = new()
            {
                chain = new WGPUChainedStruct
                {
                    sType = (WGPUSType)WGPUNativeSType.WGPUSType_InstanceExtras
                },
                backends = WebGPUNative.WGPUInstanceBackend_DX12,
                dx12ShaderCompiler = WGPUDx12Compiler.Dxc,
                dxcPath = dxcPathPointer,

                // A direct HWND swapchain is always opaque. The visual path makes wgpu create
                // a DirectComposition visual for that HWND and supports alpha-aware presentation.
                dx12PresentationSystem = WGPUDx12SwapchainKind.DxgiFromVisual
            };

            descriptor.nextInChain = (WGPUChainedStruct*)&extras;
            return webGpu.CreateInstance(&descriptor);
        }
    }

    /// <summary>
    /// Locates the packaged DirectX Shader Compiler files required by the Windows backend.
    /// </summary>
    /// <param name="dxcPath">Receives the path to <c>dxcompiler.dll</c>.</param>
    /// <param name="dxilPath">Receives the path to <c>dxil.dll</c>.</param>
    /// <returns><see langword="true"/> when both files are available, or on non-Windows platforms.</returns>
    private static bool TryGetDxcPaths(out string dxcPath, out string dxilPath)
    {
        dxcPath = string.Empty;
        dxilPath = string.Empty;
        if (!OperatingSystem.IsWindows())
        {
            return true;
        }

        // wgpu receives the compiler as a file location rather than binding it as an import,
        // so resolve it through the host's native-library search. That search already honors
        // every deployment layout: flat outputs, the NuGet runtimes layout, and
        // self-contained publishes.
        if (!NativeLibrary.TryLoad("dxcompiler.dll", typeof(WebGPURuntime).Assembly, null, out IntPtr module))
        {
            // A Native AOT shared library hosted by a foreign process probes relative to that
            // host, so fall back to the directory of this library's own image.
            if (NativeModuleLocator.TryGetModuleDirectory(out string moduleDirectory))
            {
                dxcPath = FilePath.Combine(moduleDirectory, "dxcompiler.dll");
                dxilPath = FilePath.Combine(moduleDirectory, "dxil.dll");
                if (File.Exists(dxcPath) && File.Exists(dxilPath))
                {
                    return true;
                }
            }

            dxcPath = string.Empty;
            dxilPath = string.Empty;
            return false;
        }

        try
        {
            dxcPath = NativeModuleLocator.GetModuleFilePath(module);
        }
        finally
        {
            NativeLibrary.Free(module);
        }

        if (dxcPath.Length == 0)
        {
            return false;
        }

        // The compiler loads dxil.dll from its own directory to sign shaders, so both files
        // ship side by side in every layout.
        dxilPath = FilePath.Combine(FilePath.GetDirectoryName(dxcPath)!, "dxil.dll");
        return File.Exists(dxilPath);
    }

    /// <summary>
    /// Creates one explicit exception message for a WebGPU environment failure code.
    /// </summary>
    /// <param name="errorCode">The environment failure code.</param>
    /// <returns>The exception message describing that failure.</returns>
    public static string CreateEnvironmentExceptionMessage(WebGPUEnvironmentError errorCode)
        => errorCode switch
        {
            WebGPUEnvironmentError.Success => "The WebGPU operation did not report an error.",
            WebGPUEnvironmentError.ApiInitializationFailed => "Failed to initialize the WebGPU runtime.",
            WebGPUEnvironmentError.DxcUnavailable => "The packaged DirectX Shader Compiler runtime is unavailable.",
            WebGPUEnvironmentError.InstanceCreationFailed => "The WebGPU runtime could not create an instance.",
            WebGPUEnvironmentError.AdapterRequestTimedOut => "Timed out while waiting for the WebGPU adapter request callback.",
            WebGPUEnvironmentError.AdapterRequestFailed => "The WebGPU runtime failed to acquire a WebGPU adapter.",
            WebGPUEnvironmentError.DeviceRequestTimedOut => "Timed out while waiting for the WebGPU device request callback.",
            WebGPUEnvironmentError.DeviceRequestFailed => "The WebGPU runtime failed to acquire a WebGPU device.",
            WebGPUEnvironmentError.QueueAcquisitionFailed => "The WebGPU runtime acquired a device but could not retrieve its default queue.",
            WebGPUEnvironmentError.DeviceAcquisitionFailed => "The WebGPU runtime failed to provision a WebGPU device and queue.",
            WebGPUEnvironmentError.ComputePipelineCreationFailed => "The isolated WebGPU compute-pipeline probe reported failure.",
            WebGPUEnvironmentError.ComputePipelineProbeProcessFailed => "The isolated WebGPU compute-pipeline probe process terminated before it could report a result.",
            WebGPUEnvironmentError.WgpuExtensionUnavailable => "The required WGPU extension is unavailable.",
            _ => "The WebGPU runtime failed for an unknown reason."
        };

    /// <summary>
    /// Requests a high-performance adapter from the current WebGPU instance.
    /// </summary>
    /// <param name="api">The WebGPU API wrapper.</param>
    /// <param name="instance">The instance that issues the request.</param>
    /// <param name="compatibleSurface">The presentation surface the adapter must support, or <see langword="null"/> for an offscreen adapter.</param>
    /// <param name="adapter">Receives the returned adapter on success.</param>
    /// <param name="errorCode">Receives the stable failure code when the request fails.</param>
    /// <returns><see langword="true"/> when an adapter was acquired; otherwise, <see langword="false"/>.</returns>
    public static bool TryRequestAdapter(
        WebGPU api,
        WGPUInstanceImpl* instance,
        WGPUSurfaceImpl* compatibleSurface,
        out WGPUAdapterImpl* adapter,
        out WebGPUEnvironmentError errorCode)
    {
        WGPURequestAdapterStatus callbackStatus = default;
        WGPUAdapterImpl* callbackAdapter = null;
        using ManualResetEventSlim callbackReady = new(false);

        // The native callback completes on the runtime's thread model, so the managed side stores
        // the result into locals and then resumes once the signal is set or the request times out.
        void Callback(WGPURequestAdapterStatus status, WGPUAdapterImpl* adapterPtr, WGPUStringView message, void* userData)
        {
            callbackStatus = status;
            callbackAdapter = adapterPtr;
            callbackReady.Set();
        }

        void ReleaseAbandonedResult(WGPURequestAdapterStatus status, WGPUAdapterImpl* adapterPtr, WGPUStringView message, void* userData)
        {
            _ = status;
            _ = message;
            _ = userData;

            if (adapterPtr is not null)
            {
                // AllowSpontaneous callbacks can run from a native WebGPU call stack, where the
                // checked-in header forbids re-entrant WebGPU calls. Release a late owned result
                // on the thread pool after the callback has returned to native code.
                _ = ThreadPool.UnsafeQueueUserWorkItem(
                    static state => state.Api.AdapterRelease((WGPUAdapterImpl*)state.Handle),
                    (Api: api, Handle: (nint)adapterPtr),
                    preferLocal: false);
            }
        }

        using WebGPURequestAdapterCallback callbackPtr = WebGPURequestAdapterCallback.From(Callback, ReleaseAbandonedResult);
        WebGPUEnvironmentOptions environmentOptions = WebGPUEnvironment.Options;

        WGPURequestAdapterOptions options = new()
        {
            compatibleSurface = compatibleSurface,
            forceFallbackAdapter = environmentOptions.ForceFallbackAdapter ? 1u : 0u,
            powerPreference = environmentOptions.PowerPreference switch
            {
                WebGPUPowerPreference.Default => WGPUPowerPreference.Undefined,
                WebGPUPowerPreference.LowPower => WGPUPowerPreference.LowPower,
                WebGPUPowerPreference.HighPerformance => WGPUPowerPreference.HighPerformance,
                _ => throw new InvalidOperationException("The WebGPU power preference mapping is incomplete.")
            }
        };

        api.InstanceRequestAdapter(instance, in options, callbackPtr, null);
        if (!callbackReady.Wait(CallbackTimeoutMilliseconds))
        {
            // Retire the owner before disposing its signal. Dispose waits for a callback already
            // in progress; rechecking the signal then distinguishes that race from a genuinely
            // outstanding native request whose eventual result must use the abandonment path.
            callbackPtr.Dispose();
            if (callbackReady.IsSet)
            {
                adapter = callbackAdapter;
                errorCode = callbackStatus == WGPURequestAdapterStatus.Success && callbackAdapter is not null
                    ? WebGPUEnvironmentError.Success
                    : WebGPUEnvironmentError.AdapterRequestFailed;
                return errorCode == WebGPUEnvironmentError.Success;
            }

            adapter = null;
            errorCode = WebGPUEnvironmentError.AdapterRequestTimedOut;
            return false;
        }

        adapter = callbackAdapter;
        if (callbackStatus != WGPURequestAdapterStatus.Success || callbackAdapter is null)
        {
            errorCode = WebGPUEnvironmentError.AdapterRequestFailed;
            return false;
        }

        errorCode = WebGPUEnvironmentError.Success;
        return true;
    }

    /// <summary>
    /// Requests a device from the chosen adapter, enabling optional features that the backend can use.
    /// </summary>
    /// <param name="api">The WebGPU API wrapper.</param>
    /// <param name="adapter">The adapter to request the device from.</param>
    /// <param name="device">Receives the returned device on success.</param>
    /// <param name="errorCode">Receives the stable failure code when the request fails.</param>
    /// <returns><see langword="true"/> when a device was acquired; otherwise, <see langword="false"/>.</returns>
    public static bool TryRequestDevice(
        WebGPU api,
        WGPUAdapterImpl* adapter,
        out WGPUDeviceImpl* device,
        out WebGPUEnvironmentError errorCode)
    {
        WGPURequestDeviceStatus callbackStatus = default;
        WGPUDeviceImpl* callbackDevice = null;
        using ManualResetEventSlim callbackReady = new(false);

        // Device creation is also callback-driven, so the request writes into locals and then
        // the caller continues once the callback signals completion.
        void Callback(WGPURequestDeviceStatus status, WGPUDeviceImpl* devicePtr, WGPUStringView message, void* userData)
        {
            callbackStatus = status;
            callbackDevice = devicePtr;
            callbackReady.Set();
        }

        void ReleaseAbandonedResult(WGPURequestDeviceStatus status, WGPUDeviceImpl* devicePtr, WGPUStringView message, void* userData)
        {
            _ = status;
            _ = message;
            _ = userData;

            if (devicePtr is not null)
            {
                // The native result carries ownership even though the managed request has timed
                // out. Defer its release until after this spontaneous callback returns because
                // webgpu.h explicitly forbids re-entrant API calls from such a callback.
                _ = ThreadPool.UnsafeQueueUserWorkItem(
                    static state => state.Api.DeviceRelease((WGPUDeviceImpl*)state.Handle),
                    (Api: api, Handle: (nint)devicePtr),
                    preferLocal: false);
            }
        }

        using WebGPURequestDeviceCallback callbackPtr = WebGPURequestDeviceCallback.From(Callback, ReleaseAbandonedResult);

        // Auto-provision a device when no native surface provides one.
        // Request optional storage features that are available on this adapter.
        // The compute compositor needs storage binding on the transient output texture,
        // and some formats (e.g. Bgra8Unorm) require explicit device features.
        Span<WGPUFeatureName> requestedFeatures = stackalloc WGPUFeatureName[2];
        int requestedCount = 0;
        if (api.AdapterHasFeature(adapter, WGPUFeatureName.BGRA8UnormStorage))
        {
            requestedFeatures[requestedCount++] = WGPUFeatureName.BGRA8UnormStorage;
        }

        if (api.AdapterHasFeature(adapter, WGPUFeatureName.TextureFormatsTier1))
        {
            requestedFeatures[requestedCount++] = WGPUFeatureName.TextureFormatsTier1;
        }

        // Raise only the storage-buffer binding and total buffer-size ceilings to the adapter maximum so
        // a large scene fits in a single storage binding instead of falling back to chunked (multi-pass)
        // rendering. Every other limit stays at its WebGPU default. Without this the device inherits the
        // default 128 MiB maxStorageBufferBindingSize, which a dense stroke batch's segment buffer exceeds.
        WGPULimits requiredLimits = BuildStorageBindingLimits(api, adapter);

        fixed (WGPUFeatureName* featuresPtr = requestedFeatures)
        {
            WGPUDeviceDescriptor descriptor = new()
            {
                requiredLimits = &requiredLimits,
                requiredFeatureCount = (nuint)requestedCount,
                requiredFeatures = requestedCount > 0 ? featuresPtr : null,
                deviceLostCallbackInfo = new()
                {
                    mode = WGPUCallbackMode.AllowSpontaneous,
                    callback = &HandleDeviceLost
                },
                uncapturedErrorCallbackInfo = new()
                {
                    callback = &HandleUncapturedError
                }
            };

            api.AdapterRequestDevice(adapter, in descriptor, callbackPtr, null);
        }

        if (!callbackReady.Wait(CallbackTimeoutMilliseconds))
        {
            // Synchronize with a callback that crossed the timeout boundary before deciding
            // whether the result was observed or must be abandoned later.
            callbackPtr.Dispose();
            if (callbackReady.IsSet)
            {
                device = callbackDevice;
                errorCode = callbackStatus == WGPURequestDeviceStatus.Success && callbackDevice is not null
                    ? WebGPUEnvironmentError.Success
                    : WebGPUEnvironmentError.DeviceRequestFailed;
                return errorCode == WebGPUEnvironmentError.Success;
            }

            device = null;
            errorCode = WebGPUEnvironmentError.DeviceRequestTimedOut;
            return false;
        }

        device = callbackDevice;
        if (callbackStatus != WGPURequestDeviceStatus.Success || callbackDevice is null)
        {
            errorCode = WebGPUEnvironmentError.DeviceRequestFailed;
            return false;
        }

        errorCode = WebGPUEnvironmentError.Success;
        return true;
    }

    /// <summary>
    /// Builds a device limit request that raises only the storage-buffer binding size and the total
    /// buffer size to the adapter's maximum, leaving every other limit at its WebGPU default.
    /// </summary>
    /// <remarks>
    /// Requesting the adapter's full limit set perturbs alignment and per-stage limits and corrupts
    /// resource bindings, so all fields except the two storage ceilings are left at the undefined
    /// sentinel, which instructs the implementation to keep the default value for that limit.
    /// </remarks>
    /// <param name="api">The WebGPU API used to query the adapter.</param>
    /// <param name="adapter">The adapter whose maximum storage limits are requested.</param>
    /// <returns>The populated <see cref="WGPULimits"/> to attach to the device descriptor.</returns>
    public static WGPULimits BuildStorageBindingLimits(WebGPU api, WGPUAdapterImpl* adapter)
    {
        WGPULimits adapterLimits = default;
        _ = api.AdapterGetLimits(adapter, &adapterLimits);

        // WebGPU treats these sentinel values as "leave this limit at its default" when a required-limits
        // block is supplied, so only the two storage ceilings below deviate from the device defaults.
        const uint keepU32 = uint.MaxValue;
        const ulong keepU64 = ulong.MaxValue;

        WGPULimits limits = new()
        {
            maxTextureDimension1D = keepU32,
            maxTextureDimension2D = keepU32,
            maxTextureDimension3D = keepU32,
            maxTextureArrayLayers = keepU32,
            maxBindGroups = keepU32,
            maxBindGroupsPlusVertexBuffers = keepU32,
            maxBindingsPerBindGroup = keepU32,
            maxDynamicUniformBuffersPerPipelineLayout = keepU32,
            maxDynamicStorageBuffersPerPipelineLayout = keepU32,
            maxSampledTexturesPerShaderStage = keepU32,
            maxSamplersPerShaderStage = keepU32,
            maxStorageBuffersPerShaderStage = keepU32,
            maxStorageTexturesPerShaderStage = keepU32,
            maxUniformBuffersPerShaderStage = keepU32,
            maxUniformBufferBindingSize = keepU64,
            maxStorageBufferBindingSize = adapterLimits.maxStorageBufferBindingSize,
            minUniformBufferOffsetAlignment = keepU32,
            minStorageBufferOffsetAlignment = keepU32,
            maxVertexBuffers = keepU32,
            maxBufferSize = adapterLimits.maxBufferSize,
            maxVertexAttributes = keepU32,
            maxVertexBufferArrayStride = keepU32,
            maxInterStageShaderVariables = keepU32,
            maxColorAttachments = keepU32,
            maxColorAttachmentBytesPerSample = keepU32,
            maxComputeWorkgroupStorageSize = keepU32,
            maxComputeInvocationsPerWorkgroup = keepU32,
            maxComputeWorkgroupSizeX = keepU32,
            maxComputeWorkgroupSizeY = keepU32,
            maxComputeWorkgroupSizeZ = keepU32,
            maxComputeWorkgroupsPerDimension = keepU32,
            maxImmediateSize = keepU32,
        };

        return limits;
    }

    /// <summary>
    /// Reports a native device-lost callback through the managed WebGPU error callback.
    /// </summary>
    /// <param name="device">The address of the device reported by WebGPU.</param>
    /// <param name="reason">The native device-lost reason.</param>
    /// <param name="message">The diagnostic message supplied by the runtime.</param>
    /// <param name="userData1">The first unused native user-data pointer.</param>
    /// <param name="userData2">The second unused native user-data pointer.</param>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void HandleDeviceLost(
        WGPUDeviceImpl** device,
        WGPUDeviceLostReason reason,
        WGPUStringView message,
        void* userData1,
        void* userData2)
    {
        if (device is not null && *device is not null)
        {
            MarkDeviceLost((nint)(*device));
        }

        _ = userData1;
        _ = userData2;
        WebGPUEnvironment.ReportUncapturedError(WebGPUErrorType.DeviceLost, $"Device lost ({reason}): {message.ToManagedString()}");
    }

    /// <summary>
    /// Reports a native uncaptured-error callback through the managed WebGPU error callback.
    /// </summary>
    /// <param name="device">The address of the device reporting the error.</param>
    /// <param name="type">The native error type.</param>
    /// <param name="message">The diagnostic message supplied by the runtime.</param>
    /// <param name="userData1">The first unused native user-data pointer.</param>
    /// <param name="userData2">The second unused native user-data pointer.</param>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void HandleUncapturedError(
        WGPUDeviceImpl** device,
        WGPUErrorType type,
        WGPUStringView message,
        void* userData1,
        void* userData2)
    {
        _ = device;
        _ = userData1;
        _ = userData2;
        WebGPUEnvironment.ReportUncapturedError(WebGPUErrorTypeMapper.ToPublic(type), message.ToManagedString());
    }
}
