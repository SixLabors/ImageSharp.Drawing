// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.PixelFormats;
using IOPath = System.IO.Path;

namespace SixLabors.ImageSharp.Drawing.Tests;

[GroupOutput("Foo")]
public class GroupOutputTests
{
    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void OutputSubfolderName_ValueIsTakeFromGroupOutputAttribute<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => Assert.Equal("Foo", provider.Utility.OutputSubfolderName);

    [Theory]
    [WithBlankImage(1, 1, PixelTypes.Rgba32)]
    public void GetTestOutputDir_ShouldDefineSubfolder<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        string expected = $"{IOPath.DirectorySeparatorChar}Foo{IOPath.DirectorySeparatorChar}";
        Assert.Contains(expected, provider.Utility.GetTestOutputDir());
    }
}
