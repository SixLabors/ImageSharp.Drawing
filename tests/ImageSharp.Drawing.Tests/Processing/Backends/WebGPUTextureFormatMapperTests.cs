// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Processing.Backends;
using SixLabors.ImageSharp.Drawing.Processing.Backends.Native;

namespace SixLabors.ImageSharp.Drawing.Tests.Processing.Backends;

public class WebGPUTextureFormatMapperTests
{
    [Fact]
    public void Mapper_UsesExplicitMappings_ForAllSupportedFormats()
    {
        (WebGPUTextureFormat Drawing, WGPUTextureFormat Native)[] mappings =
        [
            (WebGPUTextureFormat.Rgba8Unorm, WGPUTextureFormat.RGBA8Unorm),
            (WebGPUTextureFormat.Rgba8Snorm, WGPUTextureFormat.RGBA8Snorm),
            (WebGPUTextureFormat.Bgra8Unorm, WGPUTextureFormat.BGRA8Unorm),
            (WebGPUTextureFormat.Rgba16Float, WGPUTextureFormat.RGBA16Float)
        ];

        Assert.Equal(Enum.GetValues<WebGPUTextureFormat>().Length, mappings.Length);

        foreach ((WebGPUTextureFormat drawing, WGPUTextureFormat native) in mappings)
        {
            Assert.Equal(native, WebGPUTextureFormatMapper.ToNative(drawing));
            Assert.Equal(drawing, WebGPUTextureFormatMapper.FromNative(native));
        }
    }
}
