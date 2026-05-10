// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using Silk.NET.WebGPU;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Safe-handle wrapper for a WebGPU instance handle.
/// </summary>
internal sealed unsafe class WebGPUInstanceHandle : WebGPUHandle
{
    private readonly WebGPU? api;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebGPUInstanceHandle"/> class.
    /// </summary>
    /// <param name="instanceHandle">The WebGPU instance handle value.</param>
    /// <param name="ownsHandle">
    /// <see langword="true"/> when this wrapper owns the instance and must release it;
    /// <see langword="false"/> when the caller retains ownership.
    /// </param>
    internal WebGPUInstanceHandle(nint instanceHandle, bool ownsHandle)
        : this(ownsHandle ? WebGPURuntime.GetApi() : null, instanceHandle, ownsHandle)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="WebGPUInstanceHandle"/> class.
    /// </summary>
    /// <param name="api">
    /// The WebGPU API facade used to release the handle when this wrapper owns it,
    /// or <see langword="null"/> when the wrapper is non-owning.
    /// </param>
    /// <param name="instanceHandle">The WebGPU instance handle value.</param>
    /// <param name="ownsHandle">
    /// <see langword="true"/> when this wrapper owns the instance and must release it;
    /// <see langword="false"/> when the caller retains ownership.
    /// </param>
    internal WebGPUInstanceHandle(WebGPU? api, nint instanceHandle, bool ownsHandle)
        : base(instanceHandle, ownsHandle)
        => this.api = api;

    /// <inheritdoc />
    protected override bool ReleaseHandle()
    {
        try
        {
            this.api?.InstanceRelease((Instance*)this.handle);
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
