// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Diagnostics;
using System.Runtime.CompilerServices;
using SixLabors.ImageSharp.Drawing.Processing.Backends.Native;

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
/// buffer with a map still pending is undefined behavior. The map callback wrapper roots itself
/// from native registration until WebGPU delivers the invocation; after a resolution timeout it
/// suppresses access to this disposed owner while still preserving the unmanaged function target.
/// </remarks>
internal sealed unsafe class WebGPUPendingSchedulingStatus : IDisposable
{
    /// <summary>
    /// The API facade used to read and release the readback buffer.
    /// </summary>
    private readonly WebGPU api;

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
    private WGPUBufferImpl* readbackBuffer;

    /// <summary>
    /// The mapped byte length requested when the asynchronous map was started.
    /// </summary>
    private readonly nuint readbackByteLength;

    /// <summary>
    /// The physical byte capacity of the rented readback buffer. The buffer is returned to the
    /// pool under this size rather than the mapped length, which for a chunked flush is smaller
    /// (one record per chunk versus one per tile row), so the oversized buffer is filed under its
    /// true capacity and stays reusable for later equally large rents.
    /// </summary>
    private readonly nuint bufferByteCapacity;

    /// <summary>
    /// The self-rooting map callback wrapper that tracks the invocation accepted by native WebGPU.
    /// </summary>
    private readonly WebGPUBufferMapCallback callback;

    /// <summary>
    /// Set by the map callback once the buffer contents are readable.
    /// </summary>
    private readonly ManualResetEventSlim mapReady = new(false);

    /// <summary>
    /// The map status reported by the callback.
    /// </summary>
    private WGPUMapAsyncStatus mapStatus;

    /// <summary>
    /// The submission index tied to this readback submission.
    /// </summary>
    private readonly ulong submissionIndex;

    /// <summary>
    /// Whether resolution has already run.
    /// </summary>
    private bool resolved;

    /// <summary>
    /// Whether owned callback and synchronization resources have already been released.
    /// </summary>
    private bool disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebGPUPendingSchedulingStatus"/> class and
    /// starts the asynchronous map of the already-submitted readback copy.
    /// </summary>
    /// <param name="api">The API facade used to read and release the readback buffer.</param>
    /// <param name="deviceHandle">The wrapped device handle that owns the readback buffer.</param>
    /// <param name="deviceState">The device-scoped shared state whose pool recycles the buffer.</param>
    /// <param name="readbackBuffer">The dedicated map-readable buffer; ownership transfers to this instance.</param>
    /// <param name="submittedBumpSizes">The scratch capacities the deferred flush rendered with.</param>
    /// <param name="submissionIndex">The queue submission index for this deferred status copy.</param>
    public WebGPUPendingSchedulingStatus(
        WebGPU api,
        WebGPUDeviceHandle deviceHandle,
        WebGPURuntime.DeviceSharedState deviceState,
        WGPUBufferImpl* readbackBuffer,
        WebGPUSceneBumpSizes submittedBumpSizes,
        ulong submissionIndex)
        : this(
            api,
            deviceHandle,
            deviceState,
            readbackBuffer,
            (nuint)sizeof(GpuSceneBumpAllocators),
            (nuint)sizeof(GpuSceneBumpAllocators),
            submittedBumpSizes,
            null,
            null,
            0U,
            submissionIndex)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="WebGPUPendingSchedulingStatus"/> class for
    /// one allocator record stored in a larger pooled readback buffer.
    /// </summary>
    /// <param name="api">The API facade used to read and release the readback buffer.</param>
    /// <param name="deviceHandle">The wrapped device handle that owns the readback buffer.</param>
    /// <param name="deviceState">The device-scoped shared state whose pool recycles the buffer.</param>
    /// <param name="readbackBuffer">The dedicated map-readable buffer; ownership transfers to this instance.</param>
    /// <param name="bufferByteCapacity">The physical byte capacity of <paramref name="readbackBuffer"/>.</param>
    /// <param name="submittedBumpSizes">The scratch capacities the deferred flush rendered with.</param>
    /// <param name="submissionIndex">The queue submission index for this deferred status copy.</param>
    public WebGPUPendingSchedulingStatus(
        WebGPU api,
        WebGPUDeviceHandle deviceHandle,
        WebGPURuntime.DeviceSharedState deviceState,
        WGPUBufferImpl* readbackBuffer,
        nuint bufferByteCapacity,
        WebGPUSceneBumpSizes submittedBumpSizes,
        ulong submissionIndex)
        : this(
            api,
            deviceHandle,
            deviceState,
            readbackBuffer,
            (nuint)sizeof(GpuSceneBumpAllocators),
            bufferByteCapacity,
            submittedBumpSizes,
            null,
            null,
            0U,
            submissionIndex)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="WebGPUPendingSchedulingStatus"/> class for
    /// a chunked staged-scene flush and starts the asynchronous map of all chunk status records.
    /// </summary>
    /// <param name="api">The API facade used to read and release the readback buffer.</param>
    /// <param name="deviceHandle">The wrapped device handle that owns the readback buffer.</param>
    /// <param name="deviceState">The device-scoped shared state whose pool recycles the buffer.</param>
    /// <param name="readbackBuffer">The dedicated map-readable buffer; ownership transfers to this instance.</param>
    /// <param name="bufferByteCapacity">The physical byte capacity of <paramref name="readbackBuffer"/>, used when it is returned to the pool.</param>
    /// <param name="submittedBumpSizes">The full-scene scratch capacities the deferred flush rendered from.</param>
    /// <param name="chunkBumpSizes">The scratch capacities each chunk rendered with.</param>
    /// <param name="chunkTileHeights">The tile-row height of each chunk.</param>
    /// <param name="fullTileHeight">The full tile-row height of the chunked target range.</param>
    /// <param name="submissionIndex">The queue submission index for this deferred status copy.</param>
    public WebGPUPendingSchedulingStatus(
        WebGPU api,
        WebGPUDeviceHandle deviceHandle,
        WebGPURuntime.DeviceSharedState deviceState,
        WGPUBufferImpl* readbackBuffer,
        nuint bufferByteCapacity,
        WebGPUSceneBumpSizes submittedBumpSizes,
        ReadOnlySpan<WebGPUSceneBumpSizes> chunkBumpSizes,
        ReadOnlySpan<uint> chunkTileHeights,
        uint fullTileHeight,
        ulong submissionIndex)
        : this(
            api,
            deviceHandle,
            deviceState,
            readbackBuffer,
            checked((nuint)chunkBumpSizes.Length * (nuint)sizeof(GpuSceneBumpAllocators)),
            bufferByteCapacity,
            submittedBumpSizes,
            chunkBumpSizes.ToArray(),
            chunkTileHeights.ToArray(),
            fullTileHeight,
            submissionIndex)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="WebGPUPendingSchedulingStatus"/> class and
    /// starts the asynchronous map of the already-submitted readback copy.
    /// </summary>
    /// <param name="api">The API facade used to read and release the readback buffer.</param>
    /// <param name="deviceHandle">The wrapped device handle that owns the readback buffer.</param>
    /// <param name="deviceState">The device-scoped shared state whose pool recycles the buffer.</param>
    /// <param name="readbackBuffer">The dedicated map-readable buffer; ownership transfers to this instance.</param>
    /// <param name="readbackByteLength">The byte length to map from <paramref name="readbackBuffer"/>.</param>
    /// <param name="bufferByteCapacity">The physical byte capacity of <paramref name="readbackBuffer"/>, used when it is returned to the pool.</param>
    /// <param name="submittedBumpSizes">The scratch capacities the deferred flush rendered with.</param>
    /// <param name="chunkBumpSizes">The scratch capacities each chunk rendered with.</param>
    /// <param name="chunkTileHeights">The tile-row height of each chunk.</param>
    /// <param name="fullTileHeight">The full tile-row height of the chunked target range.</param>
    /// <param name="submissionIndex">The queue submission index for this deferred status copy.</param>
    private WebGPUPendingSchedulingStatus(
        WebGPU api,
        WebGPUDeviceHandle deviceHandle,
        WebGPURuntime.DeviceSharedState deviceState,
        WGPUBufferImpl* readbackBuffer,
        nuint readbackByteLength,
        nuint bufferByteCapacity,
        WebGPUSceneBumpSizes submittedBumpSizes,
        WebGPUSceneBumpSizes[]? chunkBumpSizes,
        uint[]? chunkTileHeights,
        uint fullTileHeight,
        ulong submissionIndex)
    {
        this.api = api;
        this.deviceHandle = deviceHandle;
        this.deviceState = deviceState;
        this.readbackBuffer = readbackBuffer;
        this.readbackByteLength = readbackByteLength;
        this.bufferByteCapacity = bufferByteCapacity;
        this.submissionIndex = submissionIndex;
        this.SubmittedBumpSizes = submittedBumpSizes;
        this.ChunkBumpSizes = chunkBumpSizes;
        this.ChunkTileHeights = chunkTileHeights;
        this.FullTileHeight = fullTileHeight;

        this.callback = WebGPUBufferMapCallback.From(this.OnMapped);
        this.api.BufferMapAsync(readbackBuffer, MapMode.Read, 0, readbackByteLength, this.callback, null);
    }

    /// <summary>
    /// Gets the scratch capacities the deferred flush rendered with. Comparing the resolved
    /// counters against these sizes tells the caller whether the frame overflowed.
    /// </summary>
    public WebGPUSceneBumpSizes SubmittedBumpSizes { get; }

    /// <summary>
    /// Gets the scratch capacities each chunk rendered with, or <see langword="null"/> for a
    /// non-chunked deferred flush.
    /// </summary>
    public WebGPUSceneBumpSizes[]? ChunkBumpSizes { get; }

    /// <summary>
    /// Gets the tile-row height of each chunk, or <see langword="null"/> for a non-chunked
    /// deferred flush.
    /// </summary>
    public uint[]? ChunkTileHeights { get; }

    /// <summary>
    /// Gets the full tile-row height of the chunked target range.
    /// </summary>
    public uint FullTileHeight { get; }

    /// <summary>
    /// Gets a value indicating whether this status describes a chunked staged-scene flush.
    /// </summary>
    public bool IsChunked => this.ChunkBumpSizes is not null;

    /// <summary>
    /// Gets a value indicating whether the map callback has fired, making <see cref="TryResolve"/>
    /// wait-free. Callbacks only advance while the device is pumped, so callers should
    /// <see cref="PollDevice"/> once before checking a batch of pending statuses.
    /// </summary>
    public bool IsReady => this.mapReady.IsSet;

    /// <summary>
    /// Pumps the device once without waiting so any completed map callbacks are delivered.
    /// </summary>
    public void PollDevice()
    {
        if (this.resolved)
        {
            return;
        }

        using WebGPUHandle.HandleReference deviceReference = this.deviceHandle.AcquireReference();
        ulong submissionIndex = this.submissionIndex;
        _ = this.api.DevicePoll((WGPUDeviceImpl*)deviceReference.Handle, false, &submissionIndex);
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
            signaled = WaitForMapSignal(
                this.api,
                (WGPUDeviceImpl*)deviceReference.Handle,
                this.mapReady,
                this.submissionIndex);
        }

        if (!signaled || this.mapStatus != WGPUMapAsyncStatus.Success)
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
    /// Resolves the deferred chunked readback: waits (bounded) for the map callback, reads all
    /// chunk-local bump-allocator counters, and releases the readback buffer.
    /// </summary>
    /// <param name="bumpAllocators">Receives the counters reported by the GPU for each chunk when successful.</param>
    /// <returns>
    /// <see langword="true"/> when the counters were read; <see langword="false"/> when the map
    /// failed or timed out, in which case the caller keeps its current capacities.
    /// </returns>
    public bool TryResolveChunked(out GpuSceneBumpAllocators[]? bumpAllocators)
    {
        bumpAllocators = null;
        if (this.resolved || this.readbackBuffer is null || this.ChunkBumpSizes is null)
        {
            return false;
        }

        this.resolved = true;

        bool signaled;
        using (WebGPUHandle.HandleReference deviceReference = this.deviceHandle.AcquireReference())
        {
            signaled = WaitForMapSignal(
                this.api,
                (WGPUDeviceImpl*)deviceReference.Handle,
                this.mapReady,
                this.submissionIndex);
        }

        if (!signaled || this.mapStatus != WGPUMapAsyncStatus.Success)
        {
            this.ReleaseBuffer(unmap: false);
            return false;
        }

        void* mapped = this.api.BufferGetConstMappedRange(this.readbackBuffer, 0, this.readbackByteLength);
        if (mapped is null)
        {
            this.ReleaseBuffer(unmap: true);
            return false;
        }

        bumpAllocators = new GpuSceneBumpAllocators[this.ChunkBumpSizes.Length];
        ReadOnlySpan<GpuSceneBumpAllocators> statuses = new(mapped, bumpAllocators.Length);
        statuses.CopyTo(bumpAllocators);
        this.ReleaseBuffer(unmap: true);
        return true;
    }

    /// <summary>
    /// Disposes the pending status. When still unresolved this waits (bounded) for the map
    /// callback, then retires the managed owner and releases the readback buffer.
    /// </summary>
    public void Dispose()
    {
        if (this.disposed)
        {
            return;
        }

        // Mark disposal before retiring the callback owner so repeated calls cannot release the
        // readback resources twice. The wrapper retains its own root if native still owes a call.
        this.disposed = true;

        if (!this.resolved)
        {
            // Give the accepted map a bounded opportunity to finish before releasing its buffer.
            // A timeout is still safe because retiring the wrapper below suppresses a later call
            // into this owner while its self-root preserves the native function target.
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
    private void OnMapped(WGPUMapAsyncStatus status, void* userData)
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
            this.deviceState.ReturnStatusReadbackBuffer(this.readbackBuffer, this.bufferByteCapacity);
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
    /// <param name="api">The WebGPU API used to advance callback delivery.</param>
    /// <param name="device">The device that owns the mapped readback buffer.</param>
    /// <param name="signal">The event that the map callback sets when the copy is ready to read.</param>
    /// <param name="submissionIndex">The queue submission index used for this readback.</param>
    /// <returns><see langword="true"/> when the callback completed before the timeout; otherwise, <see langword="false"/>.</returns>
    private static bool WaitForMapSignal(
        WebGPU api,
        WGPUDeviceImpl* device,
        ManualResetEventSlim signal,
        ulong submissionIndex)
    {
        // Keep polling scoped to this readback's submission, but never ask one native poll to wait.
        // A blocking DevicePoll can overrun the managed five-second bound before control returns
        // to the stopwatch check.
        Stopwatch stopwatch = Stopwatch.StartNew();

        while (!signal.IsSet && stopwatch.ElapsedMilliseconds < 5000)
        {
            _ = api.DevicePoll(device, false, &submissionIndex);

            if (!signal.IsSet)
            {
                _ = Thread.Yield();
            }
        }

        return signal.IsSet;
    }
}
