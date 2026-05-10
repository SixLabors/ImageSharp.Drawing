// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using Silk.NET.WebGPU;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Safe-handle wrapper for a WebGPU texture handle.
/// </summary>
internal sealed unsafe class WebGPUTextureHandle : WebGPUHandle
{
    private readonly WebGPU? api;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebGPUTextureHandle"/> class.
    /// </summary>
    /// <param name="textureHandle">The WebGPU texture handle value.</param>
    /// <param name="ownsHandle">
    /// <see langword="true"/> when this wrapper owns the texture and must release it;
    /// <see langword="false"/> when the caller retains ownership.
    /// </param>
    internal WebGPUTextureHandle(nint textureHandle, bool ownsHandle)
        : this(ownsHandle ? WebGPURuntime.GetApi() : null, textureHandle, ownsHandle)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="WebGPUTextureHandle"/> class.
    /// </summary>
    /// <param name="api">
    /// The WebGPU API facade used to release the handle when this wrapper owns it,
    /// or <see langword="null"/> when the wrapper is non-owning.
    /// </param>
    /// <param name="textureHandle">The WebGPU texture handle value.</param>
    /// <param name="ownsHandle">
    /// <see langword="true"/> when this wrapper owns the texture and must release it;
    /// <see langword="false"/> when the caller retains ownership.
    /// </param>
    internal WebGPUTextureHandle(WebGPU? api, nint textureHandle, bool ownsHandle)
        : base(textureHandle, ownsHandle)
        => this.api = api;

    /// <inheritdoc />
    protected override bool ReleaseHandle()
    {
        try
        {
            this.api?.TextureRelease((Texture*)this.handle);
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
