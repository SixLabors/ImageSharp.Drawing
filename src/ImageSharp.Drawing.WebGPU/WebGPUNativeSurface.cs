// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// WebGPU native drawing target exposed through the backend-agnostic <see cref="NativeSurface"/> contract.
/// </summary>
internal sealed class WebGPUNativeSurface : NativeSurface
{
    private readonly WebGPUNativeTarget target;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebGPUNativeSurface"/> class.
    /// </summary>
    /// <param name="target">The WebGPU target exposed by this surface.</param>
    internal WebGPUNativeSurface(WebGPUNativeTarget target)
        => this.target = target;

    /// <summary>
    /// Creates a native surface over wrapped WebGPU texture handles.
    /// </summary>
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

    /// <inheritdoc />
    public override TNativeTarget GetNativeTarget<TNativeTarget>()
        where TNativeTarget : class
    {
        if (this.target is TNativeTarget typed)
        {
            return typed;
        }

        throw new NotSupportedException($"The native surface does not expose a native target of type '{typeof(TNativeTarget).Name}'.");
    }
}
