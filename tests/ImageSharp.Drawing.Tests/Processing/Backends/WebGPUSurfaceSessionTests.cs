// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Processing.Backends;
using SixLabors.ImageSharp.Drawing.Tests.TestUtilities.Attributes;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Drawing.Tests.Processing.Backends;

public unsafe class WebGPUSurfaceSessionTests
{
    [WebGPUFact]
    public void EnsureInitialized_CanRetryAfterDeviceContextConstructionFails()
    {
        int contextCreationCount = 0;
        using WebGPUSurfaceSession session = new(
            Configuration.Default,
            (configuration, device, queue) =>
            {
                contextCreationCount++;

                if (contextCreationCount == 1)
                {
                    throw new InvalidOperationException("Injected device-context construction failure.");
                }

                return new WebGPUDeviceContext(configuration, device, queue);
            });

        Assert.Throws<InvalidOperationException>(() => session.EnsureInitialized(null));

        // The failed transaction must not publish the adapter, device, queue, or context wrappers
        // that its exception cleanup just disposed. A second call must perform a complete retry.
        session.EnsureInitialized(null);

        Assert.Equal(2, contextCreationCount);

        using WebGPURenderTarget target = session.CreateRenderTarget(WebGPUTextureFormat.Rgba8Unorm, PixelAlphaRepresentation.Unassociated, 4, 4);
        Assert.Equal(4, target.Width);
        Assert.Equal(4, target.Height);
    }
}
