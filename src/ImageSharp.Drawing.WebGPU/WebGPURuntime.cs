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
/// Runtime unload is explicit:
/// </para>
/// <list type="bullet">
/// <item><description><see cref="Shutdown"/> for explicit teardown.</description></item>
/// <item><description>Best-effort cleanup on process exit.</description></item>
/// </list>
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
    private static nint autoDeviceHandle;

    /// <summary>
    /// Lazily provisioned queue handle for CPU-backed frames.
    /// </summary>
    private static nint autoQueueHandle;

    /// <summary>
    /// Tracks whether the process-exit hook has been installed.
    /// </summary>
    private static bool processExitHooked;

    private static bool? availabilityProbeResult;
    private static string? availabilityProbeError;
    private static bool? computePipelineProbeResult;
    private static string? computePipelineProbeError;

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
    /// <param name="error">Receives an error message on failure.</param>
    /// <returns><see langword="true"/> when handles are available; otherwise <see langword="false"/>.</returns>
    internal static bool TryGetOrCreateDevice(out Device* device, out Queue* queue, out string? error)
    {
        lock (Sync)
        {
            // Fast path: return cached handles.
            if (autoDeviceHandle != 0 && autoQueueHandle != 0)
            {
                device = (Device*)autoDeviceHandle;
                queue = (Queue*)autoQueueHandle;
                error = null;
                return true;
            }

            EnsureInitialized();
            if (api is null)
            {
                device = null;
                queue = null;
                error = "WebGPU API is not initialized.";
                return false;
            }

            // Provision: instance → adapter → device → queue.
            // The instance and adapter are transient; only the device and queue are cached.
            Instance* instance = api.CreateInstance((InstanceDescriptor*)null);
            if (instance is null)
            {
                device = null;
                queue = null;
                error = "WebGPU.CreateInstance returned null.";
                return false;
            }

            Adapter* adapter = null;
            Device* requestedDevice = null;
            Queue* requestedQueue = null;
            bool initialized = false;
            try
            {
                if (!TryRequestAdapter(api, instance, out adapter, out error))
                {
                    device = null;
                    queue = null;
                    return false;
                }

                if (!TryRequestDevice(api, adapter, out requestedDevice, out error))
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
                    error = "WebGPU.DeviceGetQueue returned null.";
                    return false;
                }

                // Cache for subsequent calls.
                autoDeviceHandle = (nint)requestedDevice;
                autoQueueHandle = (nint)requestedQueue;
                device = requestedDevice;
                queue = requestedQueue;
                error = null;
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

    internal static bool TryProbeAvailability(out string? error)
    {
        lock (ProbeSync)
        {
            if (availabilityProbeResult.HasValue)
            {
                error = availabilityProbeError;
                return availabilityProbeResult.Value;
            }

            try
            {
                availabilityProbeResult = TryGetOrCreateDevice(out _, out _, out string? deviceError);
                availabilityProbeError = availabilityProbeResult.Value ? null : deviceError ?? "WebGPU device acquisition failed.";
            }
            catch (Exception ex)
            {
                availabilityProbeResult = false;
                availabilityProbeError = ex.Message;
            }

            error = availabilityProbeError;
            return availabilityProbeResult.Value;
        }
    }

    internal static bool TryProbeComputePipelineSupport(out string? error)
    {
        lock (ProbeSync)
        {
            if (computePipelineProbeResult.HasValue)
            {
                error = computePipelineProbeError;
                return computePipelineProbeResult.Value;
            }

            if (!TryProbeAvailability(out string? availabilityError))
            {
                computePipelineProbeResult = false;
                computePipelineProbeError = availabilityError;
                error = computePipelineProbeError;
                return false;
            }

            if (!RemoteExecutor.IsSupported)
            {
                computePipelineProbeResult = true;
                computePipelineProbeError = null;
                error = null;
                return true;
            }

            int exitCode = RemoteExecutor.Invoke(ProbeComputePipelineSupport);
            computePipelineProbeResult = exitCode == 0;
            computePipelineProbeError = computePipelineProbeResult.Value
                ? null
                : $"WebGPU compute pipeline probe failed with exit code {exitCode}.";
            error = computePipelineProbeError;
            return computePipelineProbeResult.Value;
        }
    }

    internal static int ProbeComputePipelineSupport()
    {
        try
        {
            if (!TryGetOrCreateDevice(out Device* device, out _, out _))
            {
                return 1;
            }

            WebGPU api = GetApi();

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
    /// Shuts down the process-level WebGPU runtime.
    /// </summary>
    /// <remarks>
    /// This call is intended for coordinated application shutdown. Runtime state can be
    /// reinitialized later by calling <see cref="GetApi"/> again.
    /// </remarks>
    public static void Shutdown()
    {
        lock (Sync)
        {
            DisposeRuntimeCore();
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
        Shutdown();
    }

    private static void DisposeRuntimeCore()
    {
        ClearDeviceStateCache();

        if (api is not null)
        {
            if (autoQueueHandle != 0)
            {
                api.QueueRelease((Queue*)autoQueueHandle);
            }

            if (autoDeviceHandle != 0)
            {
                api.DeviceRelease((Device*)autoDeviceHandle);
            }
        }

        autoDeviceHandle = 0;
        autoQueueHandle = 0;

        lock (ProbeSync)
        {
            availabilityProbeResult = null;
            availabilityProbeError = null;
            computePipelineProbeResult = null;
            computePipelineProbeError = null;
        }

        wgpuExtension?.Dispose();
        wgpuExtension = null;
        api?.Dispose();
        api = null;
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
    /// Requests a high-performance adapter from the current WebGPU instance.
    /// </summary>
    /// <param name="api">The WebGPU API wrapper.</param>
    /// <param name="instance">The instance that issues the request.</param>
    /// <param name="adapter">Receives the returned adapter on success.</param>
    /// <param name="error">Receives the failure reason when the request fails.</param>
    /// <returns><see langword="true"/> when an adapter was acquired; otherwise, <see langword="false"/>.</returns>
    private static bool TryRequestAdapter(WebGPU api, Instance* instance, out Adapter* adapter, out string? error)
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
            error = "Timed out while waiting for WebGPU adapter request callback.";
            return false;
        }

        adapter = callbackAdapter;
        if (callbackStatus != RequestAdapterStatus.Success || callbackAdapter is null)
        {
            error = $"WebGPU adapter request failed with status '{callbackStatus}'.";
            return false;
        }

        error = null;
        return true;
    }

    /// <summary>
    /// Requests a device from the chosen adapter, enabling optional features that the backend can use.
    /// </summary>
    /// <param name="api">The WebGPU API wrapper.</param>
    /// <param name="adapter">The adapter to request the device from.</param>
    /// <param name="device">Receives the returned device on success.</param>
    /// <param name="error">Receives the failure reason when the request fails.</param>
    /// <returns><see langword="true"/> when a device was acquired; otherwise, <see langword="false"/>.</returns>
    private static bool TryRequestDevice(WebGPU api, Adapter* adapter, out Device* device, out string? error)
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
            error = "Timed out while waiting for WebGPU device request callback.";
            return false;
        }

        device = callbackDevice;
        if (callbackStatus != RequestDeviceStatus.Success || callbackDevice is null)
        {
            error = $"WebGPU device request failed with status '{callbackStatus}'.";
            return false;
        }

        error = null;
        return true;
    }
}
