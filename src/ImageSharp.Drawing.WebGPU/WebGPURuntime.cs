// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using Silk.NET.WebGPU;
using Silk.NET.WebGPU.Extensions.WGPU;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Process-level WebGPU API runtime.
/// </summary>
/// <remarks>
/// <para>
/// This type owns the process-level Silk <see cref="WebGPU"/> API loader, its
/// shared <see cref="Wgpu"/> extension, and a lazily provisioned default
/// device/queue pair used by the GPU backend when no native surface is available.
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
    private static readonly object ProbeSync = new();

    /// <summary>
    /// Synchronizes all runtime state transitions.
    /// </summary>
    private static readonly object Sync = new();

    /// <summary>
    /// Process-level WebGPU API loader.
    /// </summary>
    private static WebGPU? api;

    /// <summary>
    /// Shared wgpu-native extension facade.
    /// </summary>
    private static Wgpu? wgpuExtension;

    /// <summary>
    /// Lazily provisioned device handle for CPU-backed frames.
    /// </summary>
    private static WebGPUDeviceHandle? autoDeviceHandle;

    /// <summary>
    /// Lazily provisioned queue handle for CPU-backed frames.
    /// </summary>
    private static WebGPUQueueHandle? autoQueueHandle;

    /// <summary>
    /// Tracks whether the process-exit hook has been installed.
    /// </summary>
    private static bool processExitHooked;

    private static WebGPUEnvironmentError? availabilityProbeResult;
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
    /// Gets the shared wgpu-native extension facade, initializing the runtime on first use.
    /// </summary>
    /// <returns>The shared wgpu-native extension facade.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the WebGPU API cannot be initialized.</exception>
    public static Wgpu GetWgpuExtension()
    {
        lock (Sync)
        {
            EnsureInitialized();
            if (wgpuExtension is null)
            {
                throw new InvalidOperationException("WebGPU.TryGetDeviceExtension for Wgpu failed.");
            }

            return wgpuExtension;
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
    internal static bool TryGetOrCreateDevice(
        out WebGPUDeviceHandle? device,
        out WebGPUQueueHandle? queue,
        out WebGPUEnvironmentError errorCode)
    {
        lock (Sync)
        {
            // Fast path: return cached handles.
            if (autoDeviceHandle is not null && autoQueueHandle is not null)
            {
                device = autoDeviceHandle;
                queue = autoQueueHandle;
                errorCode = WebGPUEnvironmentError.Success;
                return true;
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
            Instance* instance = api.CreateInstance((InstanceDescriptor*)null);
            if (instance is null)
            {
                device = null;
                queue = null;
                errorCode = WebGPUEnvironmentError.InstanceCreationFailed;
                return false;
            }

            Adapter* adapter = null;
            Device* requestedDevice = null;
            Queue* requestedQueue = null;
            bool initialized = false;
            try
            {
                if (!TryRequestAdapter(api, instance, out adapter, out errorCode))
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
    /// Probes whether the current process can initialize WebGPU and provision a device/queue pair.
    /// </summary>
    /// <returns><see cref="WebGPUEnvironmentError.Success"/> when basic WebGPU device acquisition succeeds; otherwise the failure code.</returns>
    /// <remarks>
    /// This is the broad availability check. It answers only "can this process get far enough to open WebGPU at all?"
    /// and deliberately stops before shader-module or pipeline creation. Callers that only need to know whether native
    /// WebGPU interop exists should use this probe. Callers that need the staged compute backend must additionally use
    /// <see cref="WebGPUEnvironment.ProbeComputePipelineSupport()"/>, because successful device acquisition does not guarantee
    /// that compute-pipeline creation is actually usable on the active runtime/driver stack.
    /// </remarks>
    internal static WebGPUEnvironmentError ProbeAvailability()
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
    internal static WebGPUEnvironmentError ProbeComputePipelineSupport()
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

            if (!RemoteExecutor.IsSupported)
            {
                computePipelineProbeResult = WebGPUEnvironmentError.Success;
                return computePipelineProbeResult.Value;
            }

            int exitCode = RemoteExecutor.Invoke(RunComputePipelineSupportProbe);
            computePipelineProbeResult = exitCode switch
            {
                0 => WebGPUEnvironmentError.Success,
                1 => WebGPUEnvironmentError.ComputePipelineCreationFailed,
                _ => WebGPUEnvironmentError.ComputePipelineProbeProcessFailed,
            };
            return computePipelineProbeResult.Value;
        }
    }

    /// <summary>
    /// Executes one isolated compute-pipeline creation probe for <see cref="WebGPUEnvironment.ProbeComputePipelineSupport()"/>.
    /// </summary>
    /// <returns>
    /// <c>0</c> when compute-pipeline creation succeeded; <c>1</c> when the probe completed and reported failure.
    /// Any other value means the isolated probe process terminated before the probe could return normally.
    /// </returns>
    internal static int RunComputePipelineSupportProbe()
    {
        try
        {
            if (!TryGetOrCreateDevice(out WebGPUDeviceHandle? deviceHandle, out _, out _)
                || deviceHandle is null)
            {
                return 1;
            }

            WebGPU api = GetApi();
            using WebGPUHandle.HandleReference deviceReference = deviceHandle.AcquireReference();
            Device* device = (Device*)deviceReference.Handle;

            ReadOnlySpan<byte> probeShader = "@compute @workgroup_size(1) fn main() {}\0"u8;

            fixed (byte* shaderCodePtr = probeShader)
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

                ShaderModule* shaderModule = api.DeviceCreateShaderModule(device, in shaderDescriptor);
                if (shaderModule is null)
                {
                    return 1;
                }

                try
                {
                    ReadOnlySpan<byte> entryPoint = "main\0"u8;
                    fixed (byte* entryPointPtr = entryPoint)
                    {
                        ProgrammableStageDescriptor computeStage = new()
                        {
                            Module = shaderModule,
                            EntryPoint = entryPointPtr
                        };

                        PipelineLayoutDescriptor layoutDescriptor = new()
                        {
                            BindGroupLayoutCount = 0,
                            BindGroupLayouts = null
                        };

                        PipelineLayout* pipelineLayout = api.DeviceCreatePipelineLayout(device, in layoutDescriptor);
                        if (pipelineLayout is null)
                        {
                            return 1;
                        }

                        try
                        {
                            ComputePipelineDescriptor pipelineDescriptor = new()
                            {
                                Layout = pipelineLayout,
                                Compute = computeStage
                            };

                            ComputePipeline* pipeline = api.DeviceCreateComputePipeline(device, in pipelineDescriptor);
                            if (pipeline is null)
                            {
                                return 1;
                            }

                            api.ComputePipelineRelease(pipeline);
                            return 0;
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
    /// Process-exit cleanup callback.
    /// </summary>
    /// <param name="sender">Event sender.</param>
    /// <param name="e">Event arguments.</param>
    private static void OnProcessExit(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;

        lock (Sync)
        {
            DisposeRuntimeCore();
        }
    }

    private static void DisposeRuntimeCore()
    {
        ClearDeviceStateCache();

        if (api is not null)
        {
            autoQueueHandle?.Dispose();
            autoDeviceHandle?.Dispose();
        }

        autoDeviceHandle = null;
        autoQueueHandle = null;

        lock (ProbeSync)
        {
            availabilityProbeResult = null;
            computePipelineProbeResult = null;
        }

        // By the time process-exit teardown reaches the shared loader wrappers, the runtime may
        // already be unwinding native loader state underneath Silk. The cached device/queue and
        // all runtime-owned GPU state have already been released above, so these dispose failures
        // no longer represent leaked WebGPU objects; they only mean the loader is already torn
        // down or no longer in a state where Silk can unload it cleanly. We must still null the
        // references so any later re-entry in the same process cannot observe stale wrappers.
        try
        {
            wgpuExtension?.Dispose();
        }
        catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException)
        {
        }
        finally
        {
            wgpuExtension = null;
        }

        try
        {
            api?.Dispose();
        }
        catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException)
        {
        }
        finally
        {
            api = null;
        }
    }

    private static void EnsureInitialized()
    {
        if (!processExitHooked)
        {
            AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
            processExitHooked = true;
        }

        api ??= WebGPU.GetApi();
        if (api is null)
        {
            throw new InvalidOperationException("WebGPU.GetApi returned null.");
        }

        if (wgpuExtension is null && !api.TryGetDeviceExtension<Wgpu>(null, out wgpuExtension))
        {
            throw new InvalidOperationException("WebGPU.TryGetDeviceExtension for Wgpu failed.");
        }
    }

    /// <summary>
    /// Creates one explicit exception message for a WebGPU environment failure code.
    /// </summary>
    /// <param name="errorCode">The environment failure code.</param>
    /// <returns>The exception message describing that failure.</returns>
    internal static string CreateEnvironmentExceptionMessage(WebGPUEnvironmentError errorCode)
        => errorCode switch
        {
            WebGPUEnvironmentError.Success => "The WebGPU operation did not report an error.",
            WebGPUEnvironmentError.ApiInitializationFailed => "Failed to initialize the WebGPU runtime.",
            WebGPUEnvironmentError.InstanceCreationFailed => "The WebGPU runtime could not create an instance.",
            WebGPUEnvironmentError.AdapterRequestTimedOut => "Timed out while waiting for the WebGPU adapter request callback.",
            WebGPUEnvironmentError.AdapterRequestFailed => "The WebGPU runtime failed to acquire a WebGPU adapter.",
            WebGPUEnvironmentError.DeviceRequestTimedOut => "Timed out while waiting for the WebGPU device request callback.",
            WebGPUEnvironmentError.DeviceRequestFailed => "The WebGPU runtime failed to acquire a WebGPU device.",
            WebGPUEnvironmentError.QueueAcquisitionFailed => "The WebGPU runtime acquired a device but could not retrieve its default queue.",
            WebGPUEnvironmentError.DeviceAcquisitionFailed => "The WebGPU runtime failed to provision a WebGPU device and queue.",
            WebGPUEnvironmentError.ComputePipelineCreationFailed => "The isolated WebGPU compute-pipeline probe reported failure.",
            WebGPUEnvironmentError.ComputePipelineProbeProcessFailed => "The isolated WebGPU compute-pipeline probe process terminated before it could report a result.",
            _ => "The WebGPU runtime failed for an unknown reason."
        };

    /// <summary>
    /// Requests a high-performance adapter from the current WebGPU instance.
    /// </summary>
    /// <param name="api">The WebGPU API wrapper.</param>
    /// <param name="instance">The instance that issues the request.</param>
    /// <param name="adapter">Receives the returned adapter on success.</param>
    /// <param name="errorCode">Receives the stable failure code when the request fails.</param>
    /// <returns><see langword="true"/> when an adapter was acquired; otherwise, <see langword="false"/>.</returns>
    private static bool TryRequestAdapter(
        WebGPU api,
        Instance* instance,
        out Adapter* adapter,
        out WebGPUEnvironmentError errorCode)
    {
        RequestAdapterStatus callbackStatus = RequestAdapterStatus.Unknown;
        Adapter* callbackAdapter = null;
        using ManualResetEventSlim callbackReady = new(false);

        // The native callback completes on the runtime's thread model, so the managed side stores
        // the result into locals and then resumes once the signal is set or the request times out.
        void Callback(RequestAdapterStatus status, Adapter* adapterPtr, byte* message, void* userData)
        {
            callbackStatus = status;
            callbackAdapter = adapterPtr;
            callbackReady.Set();
        }

        using PfnRequestAdapterCallback callbackPtr = PfnRequestAdapterCallback.From(Callback);
        RequestAdapterOptions options = new()
        {
            PowerPreference = PowerPreference.HighPerformance
        };

        api.InstanceRequestAdapter(instance, in options, callbackPtr, null);
        if (!callbackReady.Wait(CallbackTimeoutMilliseconds))
        {
            adapter = null;
            errorCode = WebGPUEnvironmentError.AdapterRequestTimedOut;
            return false;
        }

        adapter = callbackAdapter;
        if (callbackStatus != RequestAdapterStatus.Success || callbackAdapter is null)
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
    private static bool TryRequestDevice(
        WebGPU api,
        Adapter* adapter,
        out Device* device,
        out WebGPUEnvironmentError errorCode)
    {
        RequestDeviceStatus callbackStatus = RequestDeviceStatus.Unknown;
        Device* callbackDevice = null;
        using ManualResetEventSlim callbackReady = new(false);

        // Device creation is also callback-driven, so the request writes into locals and then
        // the caller continues once the callback signals completion.
        void Callback(RequestDeviceStatus status, Device* devicePtr, byte* message, void* userData)
        {
            callbackStatus = status;
            callbackDevice = devicePtr;
            callbackReady.Set();
        }

        using PfnRequestDeviceCallback callbackPtr = PfnRequestDeviceCallback.From(Callback);

        // Auto-provision a device when no native surface provides one.
        // Request optional storage features that are available on this adapter.
        // The compute compositor needs storage binding on the transient output texture,
        // and some formats (e.g. Bgra8Unorm) require explicit device features.
        Span<FeatureName> requestedFeatures = stackalloc FeatureName[1];
        int requestedCount = 0;
        if (api.AdapterHasFeature(adapter, FeatureName.Bgra8UnormStorage))
        {
            requestedFeatures[requestedCount++] = FeatureName.Bgra8UnormStorage;
        }

        DeviceDescriptor descriptor;
        if (requestedCount > 0)
        {
            fixed (FeatureName* featuresPtr = requestedFeatures)
            {
                descriptor = new DeviceDescriptor
                {
                    RequiredFeatureCount = (uint)requestedCount,
                    RequiredFeatures = featuresPtr,
                };

                api.AdapterRequestDevice(adapter, in descriptor, callbackPtr, null);
            }
        }
        else
        {
            descriptor = default;
            api.AdapterRequestDevice(adapter, in descriptor, callbackPtr, null);
        }

        if (!callbackReady.Wait(CallbackTimeoutMilliseconds))
        {
            device = null;
            errorCode = WebGPUEnvironmentError.DeviceRequestTimedOut;
            return false;
        }

        device = callbackDevice;
        if (callbackStatus != RequestDeviceStatus.Success || callbackDevice is null)
        {
            errorCode = WebGPUEnvironmentError.DeviceRequestFailed;
            return false;
        }

        errorCode = WebGPUEnvironmentError.Success;
        return true;
    }
}
