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
            (WebGPUTextureFormatId.R8Unorm, TextureFormat.R8Unorm),
            (WebGPUTextureFormatId.RG8Unorm, TextureFormat.RG8Unorm),
            (WebGPUTextureFormatId.RG8Snorm, TextureFormat.RG8Snorm),
            (WebGPUTextureFormatId.Rgba8Snorm, TextureFormat.Rgba8Snorm),
            (WebGPUTextureFormatId.R16Float, TextureFormat.R16float),
            (WebGPUTextureFormatId.RG16Float, TextureFormat.RG16float),
            (WebGPUTextureFormatId.Rgba16Float, TextureFormat.Rgba16float),
            (WebGPUTextureFormatId.RG16Sint, TextureFormat.RG16Sint),
            (WebGPUTextureFormatId.Rgba16Sint, TextureFormat.Rgba16Sint),
            (WebGPUTextureFormatId.Rgb10A2Unorm, TextureFormat.Rgb10A2Unorm),
            (WebGPUTextureFormatId.Rgba8Unorm, TextureFormat.Rgba8Unorm),
            (WebGPUTextureFormatId.Bgra8Unorm, TextureFormat.Bgra8Unorm),
            (WebGPUTextureFormatId.Rgba32Float, TextureFormat.Rgba32float),
            (WebGPUTextureFormatId.R16Uint, TextureFormat.R16Uint),
            (WebGPUTextureFormatId.RG16Uint, TextureFormat.RG16Uint),
            (WebGPUTextureFormatId.Rgba16Uint, TextureFormat.Rgba16Uint),
            (WebGPUTextureFormatId.Rgba8Uint, TextureFormat.Rgba8Uint)
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
