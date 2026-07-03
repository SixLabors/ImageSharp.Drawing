// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Diagnostics;
using System.Runtime.CompilerServices;
using Silk.NET.WebGPU;
using Silk.NET.WebGPU.Extensions.WGPU;
using WgpuBuffer = Silk.NET.WebGPU.Buffer;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// A deferred scheduling-status readback for one presented flush. Presentation flushes submit the
/// full frame (scheduling, fine shading, status copy, and target copy) in one submission and start
/// the asynchronous readback map without waiting for the GPU, removing the per-frame CPU-GPU sync
/// from the present path. The owning retained scene resolves the pending status at the start of
/// its next flush (or on disposal): by then the GPU has long finished, so the map wait is
/// effectively free, and any observed scratch overflow grows the scene's cached capacities so the
/// following frames render fully provisioned.
/// </summary>
/// <remarks>
/// The instance owns a dedicated map-readable buffer rather than borrowing the pooled scheduling
/// arena's readback buffer: the arena returns to its pool when the flush ends, and reusing a
/// buffer with a map still pending is undefined behavior. The map callback thunk is kept alive by
/// this instance until the callback has fired or resolution timed out.
/// </remarks>
internal sealed unsafe class WebGPUPendingSchedulingStatus : IDisposable
{
    /// <summary>
    /// The API facade used to read and release the readback buffer.
    /// </summary>
    private readonly WebGPU api;

    /// <summary>
    /// The optional native WGPU extension used to pump map callbacks during resolution.
    /// </summary>
    private readonly Wgpu? wgpuExtension;

    /// <summary>
    /// The wrapped device handle that owns the readback buffer.
    /// </summary>
    private readonly WebGPUDeviceHandle deviceHandle;

    /// <summary>
    /// The device-scoped shared state whose pool recycles the readback buffer once it is
    /// safely unmapped.
    /// </summary>
    private readonly WebGPURuntime.DeviceSharedState deviceState;

    /// <summary>
    /// The dedicated map-readable buffer holding the copied bump-allocator counters.
    /// </summary>
    private WgpuBuffer* readbackBuffer;

    /// <summary>
    /// The pinned map callback thunk; must stay alive until the callback fires.
    /// </summary>
    private PfnBufferMapCallback callback;

    /// <summary>
    /// Set by the map callback once the buffer contents are readable.
    /// </summary>
    private readonly ManualResetEventSlim mapReady = new(false);

    /// <summary>
    /// The map status reported by the callback.
    /// </summary>
    private BufferMapAsyncStatus mapStatus = BufferMapAsyncStatus.Unknown;

    /// <summary>
    /// Whether resolution has already run; resolution and disposal are one-shot.
    /// </summary>
    private bool resolved;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebGPUPendingSchedulingStatus"/> class and
    /// starts the asynchronous map of the already-submitted readback copy.
    /// </summary>
    /// <param name="api">The API facade used to read and release the readback buffer.</param>
    /// <param name="wgpuExtension">The optional native WGPU extension used to pump map callbacks.</param>
    /// <param name="deviceHandle">The wrapped device handle that owns the readback buffer.</param>
    /// <param name="deviceState">The device-scoped shared state whose pool recycles the buffer.</param>
    /// <param name="readbackBuffer">The dedicated map-readable buffer; ownership transfers to this instance.</param>
    /// <param name="submittedBumpSizes">The scratch capacities the deferred flush rendered with.</param>
    public WebGPUPendingSchedulingStatus(
        WebGPU api,
        Wgpu? wgpuExtension,
        WebGPUDeviceHandle deviceHandle,
        WebGPURuntime.DeviceSharedState deviceState,
        WgpuBuffer* readbackBuffer,
        WebGPUSceneBumpSizes submittedBumpSizes)
    {
        this.api = api;
        this.wgpuExtension = wgpuExtension;
        this.deviceHandle = deviceHandle;
        this.deviceState = deviceState;
        this.readbackBuffer = readbackBuffer;
        this.SubmittedBumpSizes = submittedBumpSizes;

        this.callback = PfnBufferMapCallback.From(this.OnMapped);
        this.api.BufferMapAsync(readbackBuffer, MapMode.Read, 0, (nuint)sizeof(GpuSceneBumpAllocators), this.callback, null);
    }

    /// <summary>
    /// Gets the scratch capacities the deferred flush rendered with. Comparing the resolved
    /// counters against these sizes tells the caller whether the frame overflowed.
    /// </summary>
    public WebGPUSceneBumpSizes SubmittedBumpSizes { get; }

    /// <summary>
    /// Gets a value indicating whether the map callback has fired, making <see cref="TryResolve"/>
    /// wait-free. Callbacks only advance while the device is pumped, so callers should
    /// <see cref="PollDevice"/> once before checking a batch of pending statuses.
    /// </summary>
    public bool IsReady => this.mapReady.IsSet;

    /// <summary>
    /// Pumps the device once without waiting so any completed map callbacks are delivered.
    /// No-op when the native WGPU extension is unavailable (callbacks self-deliver there).
    /// </summary>
    public void PollDevice()
    {
        if (this.wgpuExtension is null || this.resolved)
        {
            return;
        }

        using WebGPUHandle.HandleReference deviceReference = this.deviceHandle.AcquireReference();
        _ = this.wgpuExtension.DevicePoll((Device*)deviceReference.Handle, false, (WrappedSubmissionIndex*)null);
    }

    /// <summary>
    /// Resolves the deferred readback: waits (bounded) for the map callback, reads the
    /// bump-allocator counters, and releases the readback buffer.
    /// </summary>
    /// <param name="bumpAllocators">Receives the counters reported by the GPU when successful.</param>
    /// <returns>
    /// <see langword="true"/> when the counters were read; <see langword="false"/> when the map
    /// failed or timed out, in which case the caller keeps its current capacities.
    /// </returns>
    public bool TryResolve(out GpuSceneBumpAllocators bumpAllocators)
    {
        bumpAllocators = default;
        if (this.resolved || this.readbackBuffer is null)
        {
            return false;
        }

        this.resolved = true;

        bool signaled;
        using (WebGPUHandle.HandleReference deviceReference = this.deviceHandle.AcquireReference())
        {
            signaled = WaitForMapSignal(this.wgpuExtension, (Device*)deviceReference.Handle, this.mapReady);
        }

        if (!signaled || this.mapStatus != BufferMapAsyncStatus.Success)
        {
            this.ReleaseBuffer(unmap: false);
            return false;
        }

        void* mapped = this.api.BufferGetConstMappedRange(this.readbackBuffer, 0, (nuint)sizeof(GpuSceneBumpAllocators));
        if (mapped is null)
        {
            this.ReleaseBuffer(unmap: true);
            return false;
        }

        bumpAllocators = Unsafe.Read<GpuSceneBumpAllocators>(mapped);
        this.ReleaseBuffer(unmap: true);
        return true;
    }

    /// <summary>
    /// Disposes the pending status. When still unresolved this waits (bounded) for the map
    /// callback so the pinned thunk is never freed while the native side can still invoke it,
    /// then releases the readback buffer.
    /// </summary>
    public void Dispose()
    {
        if (!this.resolved)
        {
            // The result is discarded; the wait only exists to retire the native callback safely.
            _ = this.TryResolve(out _);
        }

        this.ReleaseBuffer(unmap: false);
        this.callback.Dispose();
        this.mapReady.Dispose();
    }

    /// <summary>
    /// The buffer map callback; records the status and signals resolution.
    /// </summary>
    /// <param name="status">The map status reported by the implementation.</param>
    /// <param name="userData">Unused user data pointer.</param>
    private void OnMapped(BufferMapAsyncStatus status, void* userData)
    {
        _ = userData;
        this.mapStatus = status;
        this.mapReady.Set();
    }

    /// <summary>
    /// Retires the owned readback buffer exactly once. A buffer that completed its map cycle
    /// is unmapped and recycled through the device pool; a buffer whose map may still be
    /// pending (failed or timed-out wait) is released outright, because recycling a buffer
    /// with an outstanding map is undefined behavior.
    /// </summary>
    /// <param name="unmap">Whether the buffer completed its map and must be unmapped first.</param>
    private void ReleaseBuffer(bool unmap)
    {
        if (this.readbackBuffer is null)
        {
            return;
        }

        if (unmap)
        {
            this.api.BufferUnmap(this.readbackBuffer);
            this.deviceState.ReturnStatusReadbackBuffer(this.readbackBuffer);
        }
        else
        {
            this.api.BufferRelease(this.readbackBuffer);
        }

        this.readbackBuffer = null;
    }

    /// <summary>
    /// Pumps the WebGPU device while waiting for the map callback to signal completion.
    /// Mirrors the synchronous flush path's wait so a lost device cannot hang the caller.
    /// </summary>
    /// <param name="extension">The optional native WGPU extension used to advance callback delivery.</param>
    /// <param name="device">The device that owns the mapped readback buffer.</param>
    /// <param name="signal">The event that the map callback sets when the copy is ready to read.</param>
    /// <returns><see langword="true"/> when the callback completed before the timeout; otherwise, <see langword="false"/>.</returns>
    private static bool WaitForMapSignal(Wgpu? extension, Device* device, ManualResetEventSlim signal)
    {
        // Without the native wgpu extension the implementation delivers map callbacks on its own,
        // so a plain bounded wait suffices.
        if (extension is null)
        {
            return signal.Wait(5000);
        }

        // wgpu-native only fires map callbacks from inside DevicePoll, so the device must be
        // pumped until the callback sets the signal. The five-second bound matches the
        // synchronous readback path.
        Stopwatch stopwatch = Stopwatch.StartNew();
        while (!signal.IsSet && stopwatch.ElapsedMilliseconds < 5000)
        {
            _ = extension.DevicePoll(device, true, (WrappedSubmissionIndex*)null);
        }

        return signal.IsSet;
    }
}
