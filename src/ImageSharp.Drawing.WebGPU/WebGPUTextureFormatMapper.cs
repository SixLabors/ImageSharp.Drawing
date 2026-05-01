// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using Silk.NET.WebGPU;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Maps public WebGPU texture format identifiers to native texture formats and back.
/// </summary>
internal static class WebGPUTextureFormatMapper
{
    /// <summary>
    /// Converts a public WebGPU texture format identifier to the corresponding native texture format.
    /// </summary>
    /// <param name="formatId">The public texture format identifier.</param>
    /// <returns>The matching <see cref="TextureFormat"/> value.</returns>
    public static TextureFormat ToNative(WebGPUTextureFormat formatId)
        => formatId switch
        {
            WebGPUTextureFormat.Rgba8Unorm => TextureFormat.Rgba8Unorm,
            WebGPUTextureFormat.Rgba8Snorm => TextureFormat.Rgba8Snorm,
            WebGPUTextureFormat.Bgra8Unorm => TextureFormat.Bgra8Unorm,
            WebGPUTextureFormat.Rgba16Float => TextureFormat.Rgba16float,
            _ => throw new InvalidOperationException("The WebGPU texture format mapping is incomplete.")
        };

    /// <summary>
    /// Converts a native texture format to the corresponding public WebGPU texture format identifier.
    /// </summary>
    /// <param name="textureFormat">The native texture format.</param>
    /// <returns>The matching <see cref="WebGPUTextureFormat"/> value.</returns>
    public static WebGPUTextureFormat FromNative(TextureFormat textureFormat)
        => textureFormat switch
        {
            TextureFormat.Rgba8Unorm => WebGPUTextureFormat.Rgba8Unorm,
            TextureFormat.Rgba8Snorm => WebGPUTextureFormat.Rgba8Snorm,
            TextureFormat.Bgra8Unorm => WebGPUTextureFormat.Bgra8Unorm,
            TextureFormat.Rgba16float => WebGPUTextureFormat.Rgba16Float,
            _ => throw new InvalidOperationException("The native texture format mapping is incomplete.")
        };
}
