// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Bmp;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using Xunit.Abstractions;
using IOPath = System.IO.Path;

// ReSharper disable InconsistentNaming
namespace SixLabors.ImageSharp.Drawing.Tests;

public class TestEnvironmentTests
{
    public TestEnvironmentTests(ITestOutputHelper output)
        => this.Output = output;

    private ITestOutputHelper Output { get; }

    private void CheckPath(string path)
    {
        this.Output.WriteLine(path);
        Assert.True(Directory.Exists(path));
    }

    [Fact]
    public void SolutionDirectoryFullPath()
        => this.CheckPath(TestEnvironment.SolutionDirectoryFullPath);

    [Fact]
    public void InputImagesDirectoryFullPath()
        => this.CheckPath(TestEnvironment.InputImagesDirectoryFullPath);

    [Fact]
    public void ExpectedOutputDirectoryFullPath()
        => this.CheckPath(TestEnvironment.ReferenceOutputDirectoryFullPath);

    [Fact]
    public void GetReferenceOutputFileName()
    {
        string actual = IOPath.Combine(TestEnvironment.ActualOutputDirectoryFullPath, @"foo\bar\lol.jpeg");
        string expected = TestEnvironment.GetReferenceOutputFileName(actual);

        this.Output.WriteLine(expected);
        Assert.Contains(TestEnvironment.ReferenceOutputDirectoryFullPath, expected);
    }

    [Theory]
    [InlineData("lol/foo.png", typeof(PngEncoder))]
    [InlineData("lol/Rofl.bmp", typeof(BmpEncoder))]
    [InlineData("lol/Baz.JPG", typeof(JpegEncoder))]
    [InlineData("lol/Baz.gif", typeof(GifEncoder))]
    public void GetReferenceEncoder_ReturnsCorrectEncoders(string fileName, Type expectedEncoderType)
    {
        IImageEncoder encoder = TestEnvironment.GetReferenceEncoder(fileName);
        Assert.IsType(expectedEncoderType, encoder);
    }

    [Theory]
    [InlineData("lol/foo.png", typeof(PngDecoder))]
    [InlineData("lol/Rofl.bmp", typeof(BmpDecoder))]
    [InlineData("lol/Baz.JPG", typeof(JpegDecoder))]
    [InlineData("lol/Baz.gif", typeof(GifDecoder))]
    public void GetReferenceDecoder_ReturnsCorrectDecoders(string fileName, Type expectedDecoderType)
    {
        IImageDecoder decoder = TestEnvironment.GetReferenceDecoder(fileName);
        Assert.IsType(expectedDecoderType, decoder);
    }
}
