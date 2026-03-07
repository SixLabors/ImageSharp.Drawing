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
/// optional <see cref="Wgpu"/> extension, and a lazily provisioned default
/// device/queue pair used by the GPU backend when no native surface is available.
/// </para>
/// <para>
/// Backends acquire access by taking a <see cref="Lease"/> via <see cref="Acquire"/>.
/// The lease count is thread-safe and prevents accidental shutdown while active
/// backends are still running.
/// </para>
/// <para>
/// Runtime unload is explicit:
/// </para>
/// <list type="bullet">
/// <item><description><see cref="Shutdown"/> when there are no active leases.</description></item>
/// <item><description>Best-effort cleanup on process exit.</description></item>
/// </list>
/// <para>
/// The shutdown path is resilient to duplicate native unload attempts.
/// </para>
/// </remarks>
internal static unsafe class WebGPURuntime
{
    /// <summary>
    /// Synchronizes all runtime state transitions.
    /// </summary>
    private static readonly object Sync = new();

    /// <summary>
    /// Process-level WebGPU API loader.
    /// </summary>
    private static WebGPU? api;

    /// <summary>
    /// Optional wgpu-native extension facade.
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
    /// Number of currently active runtime leases.
    /// </summary>
    private static int leaseCount;

    /// <summary>
    /// Tracks whether the process-exit hook has been installed.
    /// </summary>
    private static bool processExitHooked;

    /// <summary>
    /// Timeout for asynchronous WebGPU callbacks.
    /// </summary>
    private const int CallbackTimeoutMilliseconds = 10_000;

    /// <summary>
    /// Acquires a runtime lease for WebGPU access.
    /// </summary>
    /// <returns>A lease that must be disposed when access is no longer required.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the WebGPU API cannot be initialized.</exception>
    public static Lease Acquire()
    {
        lock (Sync)
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

            if (wgpuExtension is null)
            {
                api.TryGetDeviceExtension<Wgpu>(null, out wgpuExtension);
            }

            leaseCount++;
            return new Lease(api, wgpuExtension);
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

            if (api is null)
            {
                device = null;
                queue = null;
                error = "WebGPU API is not initialized. Call Acquire() first.";
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

    /// <summary>
    /// Releases one active runtime lease.
    /// </summary>
    /// <remarks>
    /// Lease release does not automatically unload the runtime. Unload is performed by
    /// <see cref="Shutdown"/> or by the process-exit handler.
    /// </remarks>
    private static void Release()
    {
        lock (Sync)
        {
            if (leaseCount <= 0)
            {
                return;
            }

            leaseCount--;
        }
    }

    /// <summary>
    /// Shuts down the process-level WebGPU runtime when no leases are active.
    /// </summary>
    /// <remarks>
    /// This call is intended for coordinated application shutdown. Runtime state can be
    /// reinitialized later by calling <see cref="Acquire"/> again.
    /// </remarks>
    /// <exception cref="InvalidOperationException">Thrown when runtime leases are still active.</exception>
    public static void Shutdown()
    {
        lock (Sync)
        {
            if (leaseCount != 0)
            {
                throw new InvalidOperationException($"Cannot shut down WebGPU runtime while {leaseCount} lease(s) are active.");
            }

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
        lock (Sync)
        {
            leaseCount = 0;
            DisposeRuntimeCore();
        }
    }

    /// <summary>
    /// Disposes native runtime objects in a safe and idempotent way.
    /// </summary>
    /// <remarks>
    /// Duplicate-dispose exceptions are intentionally swallowed because process-exit
    /// teardown may race with other shutdown paths.
    /// </remarks>
    private static void DisposeRuntimeCore()
    {
        autoDeviceHandle = 0;
        autoQueueHandle = 0;

        try
        {
            wgpuExtension?.Dispose();
        }
        catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException)
        {
            // Safe to ignore at process shutdown or double-dispose races.
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
            // Safe to ignore at process shutdown or double-dispose races.
        }
        finally
        {
            api = null;
        }
    }

    private static bool TryRequestAdapter(WebGPU api, Instance* instance, out Adapter* adapter, out string? error)
    {
        RequestAdapterStatus callbackStatus = RequestAdapterStatus.Unknown;
        Adapter* callbackAdapter = null;
        using ManualResetEventSlim callbackReady = new(false);
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

    private static bool TryRequestDevice(WebGPU api, Adapter* adapter, out Device* device, out string? error)
    {
        RequestDeviceStatus callbackStatus = RequestDeviceStatus.Unknown;
        Device* callbackDevice = null;
        using ManualResetEventSlim callbackReady = new(false);
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

    /// <summary>
    /// Ref-counted access token for <see cref="WebGPURuntime"/>.
    /// </summary>
    /// <remarks>
    /// Disposing the lease decrements the runtime lease count exactly once.
    /// </remarks>
    internal sealed class Lease : IDisposable
    {
        private int disposed;

        /// <summary>
        /// Initializes a new instance of the <see cref="Lease"/> class.
        /// </summary>
        /// <param name="api">The shared WebGPU API loader.</param>
        /// <param name="wgpuExtension">The shared optional wgpu extension facade.</param>
        internal Lease(WebGPU api, Wgpu? wgpuExtension)
        {
            this.Api = api;
            this.WgpuExtension = wgpuExtension;
        }

        /// <summary>
        /// Gets the shared WebGPU API loader.
        /// </summary>
        public WebGPU Api { get; }

        /// <summary>
        /// Gets the shared optional wgpu extension facade.
        /// </summary>
        public Wgpu? WgpuExtension { get; }

        /// <summary>
        /// Releases this lease exactly once.
        /// </summary>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref this.disposed, 1) == 0)
            {
                Release();
            }
        }
    }
}
