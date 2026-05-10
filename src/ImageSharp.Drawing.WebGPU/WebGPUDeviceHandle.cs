// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using Silk.NET.WebGPU;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Safe-handle wrapper for a WebGPU device handle.
/// </summary>
internal sealed unsafe class WebGPUDeviceHandle : WebGPUHandle
{
    private readonly WebGPU? api;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebGPUDeviceHandle"/> class.
    /// </summary>
    /// <param name="deviceHandle">The WebGPU device handle value.</param>
    /// <param name="ownsHandle">
    /// <see langword="true"/> when this wrapper owns the device and must release it;
    /// <see langword="false"/> when the caller retains ownership.
    /// </param>
    internal WebGPUDeviceHandle(nint deviceHandle, bool ownsHandle)
        : this(ownsHandle ? WebGPURuntime.GetApi() : null, deviceHandle, ownsHandle)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="WebGPUDeviceHandle"/> class.
    /// </summary>
    /// <param name="api">
    /// The WebGPU API facade used to release the handle when this wrapper owns it,
    /// or <see langword="null"/> when the wrapper is non-owning.
    /// </param>
    /// <param name="deviceHandle">The WebGPU device handle value.</param>
    /// <param name="ownsHandle">
    /// <see langword="true"/> when this wrapper owns the device and must release it;
    /// <see langword="false"/> when the caller retains ownership.
    /// </param>
    internal WebGPUDeviceHandle(WebGPU? api, nint deviceHandle, bool ownsHandle)
        : base(deviceHandle, ownsHandle)
        => this.api = api;

    /// <inheritdoc />
    protected override bool ReleaseHandle()
    {
        try
        {
            this.api?.DeviceRelease((Device*)this.handle);
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
