// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using Silk.NET.WebGPU;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Safe-handle wrapper for a WebGPU texture-view handle.
/// </summary>
internal sealed unsafe class WebGPUTextureViewHandle : WebGPUHandle
{
    private readonly WebGPU? api;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebGPUTextureViewHandle"/> class.
    /// </summary>
    /// <param name="textureViewHandle">The WebGPU texture-view handle value.</param>
    /// <param name="ownsHandle">
    /// <see langword="true"/> when this wrapper owns the texture view and must release it;
    /// <see langword="false"/> when the caller retains ownership.
    /// </param>
    internal WebGPUTextureViewHandle(nint textureViewHandle, bool ownsHandle)
        : this(ownsHandle ? WebGPURuntime.GetApi() : null, textureViewHandle, ownsHandle)
    {
    }

    internal WebGPUTextureViewHandle(WebGPU? api, nint textureViewHandle, bool ownsHandle)
        : base(textureViewHandle, ownsHandle)
    {
        this.api = api;
    }

    /// <inheritdoc />
    protected override bool ReleaseHandle()
    {
        try
        {
            this.api?.TextureViewRelease((TextureView*)this.handle);
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
