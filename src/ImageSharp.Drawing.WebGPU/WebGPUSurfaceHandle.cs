// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using Silk.NET.WebGPU;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Safe-handle wrapper for a WebGPU surface handle.
/// </summary>
internal sealed unsafe class WebGPUSurfaceHandle : WebGPUHandle
{
    private readonly WebGPU? api;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebGPUSurfaceHandle"/> class.
    /// </summary>
    /// <param name="surfaceHandle">The WebGPU surface handle value.</param>
    /// <param name="ownsHandle">
    /// <see langword="true"/> when this wrapper owns the surface and must release it;
    /// <see langword="false"/> when the caller retains ownership.
    /// </param>
    internal WebGPUSurfaceHandle(nint surfaceHandle, bool ownsHandle)
        : this(ownsHandle ? WebGPURuntime.GetApi() : null, surfaceHandle, ownsHandle)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="WebGPUSurfaceHandle"/> class.
    /// </summary>
    /// <param name="api">
    /// The WebGPU API facade used to release the handle when this wrapper owns it,
    /// or <see langword="null"/> when the wrapper is non-owning.
    /// </param>
    /// <param name="surfaceHandle">The WebGPU surface handle value.</param>
    /// <param name="ownsHandle">
    /// <see langword="true"/> when this wrapper owns the surface and must release it;
    /// <see langword="false"/> when the caller retains ownership.
    /// </param>
    internal WebGPUSurfaceHandle(WebGPU? api, nint surfaceHandle, bool ownsHandle)
        : base(surfaceHandle, ownsHandle)
        => this.api = api;

    /// <inheritdoc />
    protected override bool ReleaseHandle()
    {
        try
        {
            this.api?.SurfaceRelease((Surface*)this.handle);
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
