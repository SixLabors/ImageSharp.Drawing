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
/// This type owns the process-level Silk <see cref="WebGPU"/> API loader and its
/// optional <see cref="Wgpu"/> extension.
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
internal static unsafe class WebGpuRuntime
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
    /// Number of currently active runtime leases.
    /// </summary>
    private static int leaseCount;

    /// <summary>
    /// Tracks whether the process-exit hook has been installed.
    /// </summary>
    private static bool processExitHooked;

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
                _ = api.TryGetDeviceExtension<Wgpu>(null, out wgpuExtension);
            }

            leaseCount++;
            return new Lease(api, wgpuExtension);
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

    /// <summary>
    /// Ref-counted access token for <see cref="WebGpuRuntime"/>.
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
