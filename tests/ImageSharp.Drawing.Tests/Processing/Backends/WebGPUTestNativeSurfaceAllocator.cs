// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Collections.Concurrent;
using Silk.NET.WebGPU;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Test-only helper for allocating native WebGPU render targets.
/// </summary>
internal static class WebGPUTestNativeSurfaceAllocator
{
    private static readonly ConcurrentDictionary<WebGPUTextureHandle, OwnedTexturePair> OwnedHandles = new();

    /// <summary>
    /// Tries to allocate a native WebGPU texture + view pair and wrap them in a <see cref="NativeSurface"/>.
    /// </summary>
    internal static bool TryCreate<TPixel>(
        int width,
        int height,
        out NativeSurface surface,
        out WebGPUTextureHandle textureHandle,
        out WebGPUTextureViewHandle textureViewHandle,
        out string error)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        WebGPU api = WebGPURuntime.GetApi();
        if (!WebGPURuntime.TryGetOrCreateDevice(out WebGPUDeviceHandle deviceHandle, out WebGPUQueueHandle queueHandle, out WebGPUEnvironmentError errorCode)
            || deviceHandle is null
            || queueHandle is null)
        {
            surface = new NativeSurface(TPixel.GetPixelTypeInfo());
            textureHandle = null;
            textureViewHandle = null;
            error = $"WebGPU device auto-provisioning failed with '{errorCode}'.";
            return false;
        }

        if (!WebGPURenderTargetAllocation.TryCreateRenderTarget<TPixel>(
                api,
                deviceHandle,
                queueHandle,
                width,
                height,
                out surface,
                out textureHandle,
                out textureViewHandle,
                out _,
                out error))
        {
            return false;
        }

        if (textureHandle is null || textureViewHandle is null)
        {
            textureViewHandle?.Dispose();
            textureHandle?.Dispose();
            error = "WebGPU test allocation succeeded without returning both owned texture handles.";
            return false;
        }

        OwnedHandles[textureHandle] = new OwnedTexturePair(textureHandle, textureViewHandle);
        return true;
    }

    /// <summary>
    /// Releases native texture and texture-view handles allocated for tests.
    /// </summary>
    /// <param name="textureHandle">The wrapped texture handle.</param>
    /// <param name="textureViewHandle">The wrapped texture-view handle.</param>
    internal static void Release(WebGPUTextureHandle textureHandle, WebGPUTextureViewHandle textureViewHandle)
    {
        if (OwnedHandles.TryRemove(textureHandle, out OwnedTexturePair ownedHandles))
        {
            ownedHandles.Dispose();
            return;
        }

        textureViewHandle.Dispose();
        textureHandle.Dispose();
    }

    private sealed class OwnedTexturePair : IDisposable
    {
        public OwnedTexturePair(WebGPUTextureHandle textureHandle, WebGPUTextureViewHandle textureViewHandle)
        {
            this.TextureHandle = textureHandle;
            this.TextureViewHandle = textureViewHandle;
        }

        public WebGPUTextureHandle TextureHandle { get; }

        public WebGPUTextureViewHandle TextureViewHandle { get; }

        public void Dispose()
        {
            this.TextureViewHandle.Dispose();
            this.TextureHandle.Dispose();
        }
    }
}
