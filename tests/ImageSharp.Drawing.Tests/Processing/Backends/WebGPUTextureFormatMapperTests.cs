// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using Silk.NET.WebGPU;
using SixLabors.ImageSharp.Drawing.Processing.Backends;

namespace SixLabors.ImageSharp.Drawing.Tests.Processing.Backends;

public class WebGPUTextureFormatMapperTests
{
    [Fact]
    public void Mapper_UsesExplicitMappings_ForAllSupportedFormats()
    {
        (WebGPUTextureFormat Drawing, TextureFormat Silk)[] mappings =
        [
            (WebGPUTextureFormat.Rgba8Unorm, TextureFormat.Rgba8Unorm),
            (WebGPUTextureFormat.Rgba8Snorm, TextureFormat.Rgba8Snorm),
            (WebGPUTextureFormat.Bgra8Unorm, TextureFormat.Bgra8Unorm),
            (WebGPUTextureFormat.Rgba16Float, TextureFormat.Rgba16float)
        ];

        Assert.Equal(Enum.GetValues<WebGPUTextureFormat>().Length, mappings.Length);

        foreach ((WebGPUTextureFormat drawing, TextureFormat silk) in mappings)
        {
            Assert.Equal(silk, WebGPUTextureFormatMapper.ToNative(drawing));
            Assert.Equal(drawing, WebGPUTextureFormatMapper.FromNative(silk));
        }
    }
}
