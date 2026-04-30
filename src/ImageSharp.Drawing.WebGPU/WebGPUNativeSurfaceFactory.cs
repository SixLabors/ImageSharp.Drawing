// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Low-level escape hatch for constructing a <see cref="NativeSurface"/> directly from raw WebGPU handles.
/// </summary>
/// <remarks>
/// Use this factory only when you need to bind ImageSharp.Drawing to a caller-owned WebGPU texture.
/// </remarks>
public static class WebGPUNativeSurfaceFactory
{
    /// <summary>
    /// Creates a WebGPU-backed <see cref="NativeSurface"/> from external native handles.
    /// </summary>
    /// <param name="deviceHandle">The external WebGPU device handle.</param>
    /// <param name="queueHandle">The external WebGPU queue handle.</param>
    /// <param name="targetTextureHandle">The external WebGPU texture handle for writable uploads.</param>
    /// <param name="targetTextureViewHandle">The external WebGPU texture-view handle for render-target binding.</param>
    /// <param name="targetFormat">Texture format.</param>
    /// <param name="width">Surface width in pixels.</param>
    /// <param name="height">Surface height in pixels.</param>
    /// <returns>A configured <see cref="NativeSurface"/> instance.</returns>
    /// <remarks>
    /// These handles must originate from the same process WebGPU runtime used by ImageSharp.Drawing.WebGPU.
    /// The target texture must support render attachment, copy source, copy destination, and texture binding usage.
    /// </remarks>
    public static NativeSurface Create(
        nint deviceHandle,
        nint queueHandle,
        nint targetTextureHandle,
        nint targetTextureViewHandle,
        WebGPUTextureFormat targetFormat,
        int width,
        int height)
        => Create(
            new WebGPUDeviceHandle(deviceHandle, ownsHandle: false),
            new WebGPUQueueHandle(queueHandle, ownsHandle: false),
            new WebGPUTextureHandle(targetTextureHandle, ownsHandle: false),
            new WebGPUTextureViewHandle(targetTextureViewHandle, ownsHandle: false),
            targetFormat,
            width,
            height);

    internal static NativeSurface Create(
        WebGPUDeviceHandle deviceHandle,
        WebGPUQueueHandle queueHandle,
        WebGPUTextureHandle targetTextureHandle,
        WebGPUTextureViewHandle targetTextureViewHandle,
        WebGPUTextureFormat targetFormat,
        int width,
        int height)
    {
        Guard.NotNull(deviceHandle, nameof(deviceHandle));
        Guard.NotNull(queueHandle, nameof(queueHandle));
        Guard.NotNull(targetTextureHandle, nameof(targetTextureHandle));
        Guard.NotNull(targetTextureViewHandle, nameof(targetTextureViewHandle));

        Guard.MustBeGreaterThan(width, 0, nameof(width));
        Guard.MustBeGreaterThan(height, 0, nameof(height));

        return new WebGPUNativeSurface(new WebGPUNativeTarget(
            deviceHandle,
            queueHandle,
            targetTextureHandle,
            targetTextureViewHandle,
            targetFormat,
            width,
            height));
    }
}
