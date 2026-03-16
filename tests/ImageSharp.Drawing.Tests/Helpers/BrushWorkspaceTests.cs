// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Drawing.Tests.Helpers;

public class BrushWorkspaceTests
{
    private readonly TestMemoryAllocator memoryAllocator = new();

    [Fact]
    public void ReturnsRowSizedSlicesFromSharedBuffers()
    {
        using BrushWorkspace<Rgb24> workspace = new(this.memoryAllocator, 100);

        Span<float> amounts = workspace.GetAmounts(8);
        Span<Rgb24> overlays = workspace.GetOverlays(8);

        Assert.Equal(8, amounts.Length);
        Assert.Equal(8, overlays.Length);

        amounts[0] = 10;
        overlays[0] = new Rgb24(10, 20, 30);

        Assert.Equal(10, workspace.GetAmounts(8)[0]);
        Assert.Equal((byte)10, workspace.GetOverlays(8)[0].R);
        Assert.Equal((byte)20, workspace.GetOverlays(8)[0].G);
        Assert.Equal((byte)30, workspace.GetOverlays(8)[0].B);
    }

    [Fact]
    public void Dispose_ReturnsSharedBuffers()
    {
        BrushWorkspace<Rgb24> workspace = new(this.memoryAllocator, 100);

        workspace.GetAmounts(16)[0] = 42;
        workspace.GetOverlays(16)[0] = new Rgb24(1, 2, 3);

        workspace.Dispose();

        Assert.Equal(2, this.memoryAllocator.ReturnLog.Count);
    }
}
