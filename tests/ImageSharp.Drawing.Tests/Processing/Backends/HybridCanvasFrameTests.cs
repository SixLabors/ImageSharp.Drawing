// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Processing.Backends;
using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Drawing.Tests.Processing.Backends;

public class HybridCanvasFrameTests
{
    [Fact]
    public void Constructor_ExposesCpuAndNativeCapabilities()
    {
        using Image<Rgba32> image = new(4, 3);
        Buffer2DRegion<Rgba32> cpuRegion = new(image.Frames.RootFrame.PixelBuffer, image.Bounds);
        NativeSurface surface = new(Rgba32.GetPixelTypeInfo());

        HybridCanvasFrame<Rgba32> frame = new(new Rectangle(0, 0, 4, 3), cpuRegion, surface);

        Assert.True(frame.TryGetCpuRegion(out Buffer2DRegion<Rgba32> returnedRegion));
        Assert.True(frame.TryGetNativeSurface(out NativeSurface? returnedSurface));
        Assert.Equal(cpuRegion.Width, returnedRegion.Width);
        Assert.Equal(cpuRegion.Height, returnedRegion.Height);
        Assert.Same(surface, returnedSurface);
    }

    [Fact]
    public void Constructor_RejectsMismatchedCpuRegionDimensions()
    {
        using Image<Rgba32> image = new(4, 3);
        Buffer2DRegion<Rgba32> cpuRegion = new(image.Frames.RootFrame.PixelBuffer, image.Bounds);
        NativeSurface surface = new(Rgba32.GetPixelTypeInfo());

        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => new HybridCanvasFrame<Rgba32>(new Rectangle(0, 0, 3, 3), cpuRegion, surface));

        Assert.Contains("CPU region dimensions", exception.Message, StringComparison.Ordinal);
    }
}
