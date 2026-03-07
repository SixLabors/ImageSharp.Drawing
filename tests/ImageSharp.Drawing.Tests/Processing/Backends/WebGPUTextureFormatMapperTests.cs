// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using Silk.NET.WebGPU;
using SixLabors.ImageSharp.Drawing.Processing.Backends;

namespace SixLabors.ImageSharp.Drawing.Tests.Processing.Backends;

public class WebGPUTextureFormatMapperTests
{
    [Fact]
    public void Mapper_UsesExactSilkEnumValues_ForAllSupportedFormats()
    {
        (WebGPUTextureFormatId Drawing, TextureFormat Silk)[] mappings =
        [
            (WebGPUTextureFormatId.Rgba8Unorm, TextureFormat.Rgba8Unorm),
            (WebGPUTextureFormatId.Rgba8Snorm, TextureFormat.Rgba8Snorm),
            (WebGPUTextureFormatId.Rgba8Uint, TextureFormat.Rgba8Uint),
            (WebGPUTextureFormatId.Bgra8Unorm, TextureFormat.Bgra8Unorm),
            (WebGPUTextureFormatId.Rgba16Uint, TextureFormat.Rgba16Uint),
            (WebGPUTextureFormatId.Rgba16Sint, TextureFormat.Rgba16Sint),
            (WebGPUTextureFormatId.Rgba16Float, TextureFormat.Rgba16float),
            (WebGPUTextureFormatId.Rgba32Float, TextureFormat.Rgba32float)
        ];

        Assert.Equal(Enum.GetValues<WebGPUTextureFormatId>().Length, mappings.Length);

        foreach ((WebGPUTextureFormatId drawing, TextureFormat silk) in mappings)
        {
            Assert.Equal((int)silk, (int)drawing);
            Assert.Equal(silk, WebGPUTextureFormatMapper.ToSilk(drawing));
            Assert.Equal(drawing, WebGPUTextureFormatMapper.FromSilk(silk));
        }
    }
}
