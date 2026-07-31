// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Processing.Backends.Native;

namespace SixLabors.ImageSharp.Drawing.Tests.Processing.Backends;

public class NativeTypeNameAttributeTests
{
    [Fact]
    public void Constructor_StoresNativeName()
        => Assert.Equal("WGPUBool", new NativeTypeNameAttribute("WGPUBool").Name);
}
