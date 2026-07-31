// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Processing.Backends;

namespace SixLabors.ImageSharp.Drawing.Tests.Processing.Backends;

public class WebGPUWindowOptionsTests
{
    [Fact]
    public void Defaults_MatchDocumentedInitialConfiguration()
    {
        WebGPUWindowOptions options = new();

        Assert.Equal("ImageSharp.Drawing WebGPU", options.Title);
        Assert.Equal(new Size(1280, 720), options.Size);
        Assert.Equal(new Point(50, 50), options.Position);
        Assert.True(options.IsVisible);
        Assert.Equal(0, options.FramesPerSecond);
        Assert.Equal(0, options.UpdatesPerSecond);
        Assert.False(options.IsEventDriven);
        Assert.Equal(WebGPUWindowState.Normal, options.WindowState);
        Assert.Equal(WebGPUWindowBorder.Resizable, options.WindowBorder);
        Assert.False(options.IsTopMost);
        Assert.Equal(WebGPUPresentMode.Fifo, options.PresentMode);
        Assert.Equal(WebGPUTextureFormat.Rgba8Unorm, options.Format);
    }
}
