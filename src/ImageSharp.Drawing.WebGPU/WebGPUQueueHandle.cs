// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using Silk.NET.WebGPU;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Safe-handle wrapper for a WebGPU queue handle.
/// </summary>
internal sealed unsafe class WebGPUQueueHandle : WebGPUHandle
{
    private readonly WebGPU? api;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebGPUQueueHandle"/> class.
    /// </summary>
    /// <param name="queueHandle">The WebGPU queue handle value.</param>
    /// <param name="ownsHandle">
    /// <see langword="true"/> when this wrapper owns the queue and must release it;
    /// <see langword="false"/> when the caller retains ownership.
    /// </param>
    internal WebGPUQueueHandle(nint queueHandle, bool ownsHandle)
        : this(ownsHandle ? WebGPURuntime.GetApi() : null, queueHandle, ownsHandle)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="WebGPUQueueHandle"/> class.
    /// </summary>
    /// <param name="api">
    /// The WebGPU API facade used to release the handle when this wrapper owns it,
    /// or <see langword="null"/> when the wrapper is non-owning.
    /// </param>
    /// <param name="queueHandle">The WebGPU queue handle value.</param>
    /// <param name="ownsHandle">
    /// <see langword="true"/> when this wrapper owns the queue and must release it;
    /// <see langword="false"/> when the caller retains ownership.
    /// </param>
    internal WebGPUQueueHandle(WebGPU? api, nint queueHandle, bool ownsHandle)
        : base(queueHandle, ownsHandle)
        => this.api = api;

    /// <inheritdoc />
    protected override bool ReleaseHandle()
    {
        try
        {
            this.api?.QueueRelease((Queue*)this.handle);
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            this.handle = IntPtr.Zero;
        }
    }
}
