// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using Silk.NET.WebGPU;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

internal static class WebGPUTextureFormatMapper
{
    public static TextureFormat ToSilk(WebGPUTextureFormatId formatId)
        => (TextureFormat)(int)formatId;

    public static WebGPUTextureFormatId FromSilk(TextureFormat textureFormat)
        => (WebGPUTextureFormatId)(int)textureFormat;
}
