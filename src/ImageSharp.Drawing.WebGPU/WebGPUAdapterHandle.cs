// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using Silk.NET.WebGPU;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Safe-handle wrapper for a WebGPU adapter handle.
/// </summary>
internal sealed unsafe class WebGPUAdapterHandle : WebGPUHandle
{
    private readonly WebGPU? api;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebGPUAdapterHandle"/> class.
    /// </summary>
    /// <param name="adapterHandle">The WebGPU adapter handle value.</param>
    /// <param name="ownsHandle">
    /// <see langword="true"/> when this wrapper owns the adapter and must release it;
    /// <see langword="false"/> when the caller retains ownership.
    /// </param>
    internal WebGPUAdapterHandle(nint adapterHandle, bool ownsHandle)
        : this(ownsHandle ? WebGPURuntime.GetApi() : null, adapterHandle, ownsHandle)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="WebGPUAdapterHandle"/> class.
    /// </summary>
    /// <param name="api">
    /// The WebGPU API facade used to release the handle when this wrapper owns it,
    /// or <see langword="null"/> when the wrapper is non-owning.
    /// </param>
    /// <param name="adapterHandle">The WebGPU adapter handle value.</param>
    /// <param name="ownsHandle">
    /// <see langword="true"/> when this wrapper owns the adapter and must release it;
    /// <see langword="false"/> when the caller retains ownership.
    /// </param>
    internal WebGPUAdapterHandle(WebGPU? api, nint adapterHandle, bool ownsHandle)
        : base(adapterHandle, ownsHandle)
        => this.api = api;

    /// <inheritdoc />
    protected override bool ReleaseHandle()
    {
        try
        {
            this.api?.AdapterRelease((Adapter*)this.handle);
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
