// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Processing.Backends;
using SixLabors.ImageSharp.Drawing.Processing.Backends.Native;

namespace SixLabors.ImageSharp.Drawing.Tests.Processing.Backends;

public class WebGPUErrorTypeMapperTests
{
    [Fact]
    public void ToPublic_MapsEveryNativeClassification()
    {
        Assert.Equal(WebGPUErrorType.NoError, WebGPUErrorTypeMapper.ToPublic(WGPUErrorType.NoError));
        Assert.Equal(WebGPUErrorType.Validation, WebGPUErrorTypeMapper.ToPublic(WGPUErrorType.Validation));
        Assert.Equal(WebGPUErrorType.OutOfMemory, WebGPUErrorTypeMapper.ToPublic(WGPUErrorType.OutOfMemory));
        Assert.Equal(WebGPUErrorType.Internal, WebGPUErrorTypeMapper.ToPublic(WGPUErrorType.Internal));
        Assert.Equal(WebGPUErrorType.Unknown, WebGPUErrorTypeMapper.ToPublic(WGPUErrorType.Unknown));
        Assert.Equal(WebGPUErrorType.Unknown, WebGPUErrorTypeMapper.ToPublic((WGPUErrorType)int.MaxValue));
    }
}
