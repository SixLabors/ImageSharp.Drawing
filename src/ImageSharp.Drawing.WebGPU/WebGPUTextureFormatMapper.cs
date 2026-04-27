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
    public static TextureFormat ToSilk(WebGPUTextureFormatId formatId)
        => (TextureFormat)(int)formatId;

    /// <summary>
    /// Converts a native texture format to the corresponding public WebGPU texture format identifier.
    /// </summary>
    /// <param name="textureFormat">The native texture format.</param>
    /// <returns>The matching <see cref="WebGPUTextureFormatId"/> value.</returns>
    public static WebGPUTextureFormatId FromSilk(TextureFormat textureFormat)
        => (WebGPUTextureFormatId)(int)textureFormat;
}
