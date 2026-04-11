// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using Silk.NET.WebGPU;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Test-only helper for allocating native WebGPU render targets.
/// </summary>
internal static unsafe class WebGPUTestNativeSurfaceAllocator
{
    /// <summary>
    /// Tries to allocate a native WebGPU texture + view pair and wrap them in a <see cref="NativeSurface"/>.
    /// </summary>
    internal static bool TryCreate<TPixel>(
        int width,
        int height,
        out NativeSurface surface,
        out nint textureHandle,
        out nint textureViewHandle,
        out string error)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        WebGPU api = WebGPURuntime.GetApi();
        if (!WebGPURuntime.TryGetOrCreateDevice(out Device* device, out Queue* queue, out string? deviceError))
        {
            surface = new NativeSurface(TPixel.GetPixelTypeInfo());
            textureHandle = 0;
            textureViewHandle = 0;
            error = deviceError ?? "WebGPU device auto-provisioning failed.";
            return false;
        }

        return WebGPURenderTargetAllocation.TryCreateRenderTarget<TPixel>(
            api,
            (nint)device,
            (nint)queue,
            width,
            height,
            out surface,
            out textureHandle,
            out textureViewHandle,
            out _,
            out error);
    }

    /// <summary>
    /// Releases native texture and texture-view handles allocated for tests.
    /// </summary>
    /// <param name="textureHandle">The native texture handle.</param>
    /// <param name="textureViewHandle">The native texture-view handle.</param>
    internal static void Release(nint textureHandle, nint textureViewHandle)
        => WebGPUTextureTransfer.Release(textureHandle, textureViewHandle);
}
