// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using Silk.NET.WebGPU;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// WebGPU native drawing surface exposed through the backend-agnostic <see cref="NativeSurface"/> base type.
/// </summary>
internal sealed class WebGPUNativeSurface : NativeSurface
{
    /// <summary>
    /// Initializes a new instance of the <see cref="WebGPUNativeSurface"/> class.
    /// </summary>
    /// <param name="deviceHandle">The wrapped device handle that owns the target texture.</param>
    /// <param name="queueHandle">The wrapped queue handle used to submit work against the target texture.</param>
    /// <param name="targetTextureHandle">The wrapped target texture handle.</param>
    /// <param name="targetTextureViewHandle">The wrapped target texture-view handle.</param>
    /// <param name="targetFormat">The target texture format.</param>
    /// <param name="width">The surface width in pixels.</param>
    /// <param name="height">The surface height in pixels.</param>
    public WebGPUNativeSurface(
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

        this.DeviceHandle = deviceHandle;
        this.QueueHandle = queueHandle;
        this.TargetTextureHandle = targetTextureHandle;
        this.TargetTextureViewHandle = targetTextureViewHandle;
        this.TargetFormat = targetFormat;
        this.Width = width;
        this.Height = height;
    }

    /// <summary>
    /// Gets the wrapped device handle that owns the target texture.
    /// </summary>
    public WebGPUDeviceHandle DeviceHandle { get; }

    /// <summary>
    /// Gets the wrapped queue handle used to submit work against the target texture.
    /// </summary>
    public WebGPUQueueHandle QueueHandle { get; }

    /// <summary>
    /// Gets the wrapped target texture handle.
    /// </summary>
    public WebGPUTextureHandle TargetTextureHandle { get; }

    /// <summary>
    /// Gets the wrapped target texture-view handle.
    /// </summary>
    public WebGPUTextureViewHandle TargetTextureViewHandle { get; }

    /// <summary>
    /// Gets the native render target texture format identifier.
    /// </summary>
    public WebGPUTextureFormat TargetFormat { get; }

    /// <summary>
    /// Gets the surface width in pixels.
    /// </summary>
    public int Width { get; }

    /// <summary>
    /// Gets the surface height in pixels.
    /// </summary>
    public int Height { get; }

    /// <summary>
    /// Allocates a WebGPU render target and creates a native surface over the owned texture handles.
    /// </summary>
    /// <param name="api">The WebGPU API instance used to allocate native resources.</param>
    /// <param name="deviceHandle">The wrapped <c>WGPUDevice*</c> handle.</param>
    /// <param name="queueHandle">The wrapped <c>WGPUQueue*</c> handle.</param>
    /// <param name="format">The target texture format.</param>
    /// <param name="width">The texture width in pixels.</param>
    /// <param name="height">The texture height in pixels.</param>
    /// <param name="textureHandle">Receives the allocated wrapped <c>WGPUTexture*</c> handle.</param>
    /// <param name="textureViewHandle">Receives the allocated wrapped <c>WGPUTextureView*</c> handle.</param>
    /// <returns>The native surface wrapping the allocated texture.</returns>
    internal static unsafe WebGPUNativeSurface Create(
        WebGPU api,
        WebGPUDeviceHandle deviceHandle,
        WebGPUQueueHandle queueHandle,
        WebGPUTextureFormat format,
        int width,
        int height,
        out WebGPUTextureHandle textureHandle,
        out WebGPUTextureViewHandle textureViewHandle)
    {
        if (deviceHandle.IsInvalid)
        {
            throw new InvalidOperationException("The WebGPU device handle is invalid.");
        }

        if (queueHandle.IsInvalid)
        {
            throw new InvalidOperationException("The WebGPU queue handle is invalid.");
        }

        Guard.MustBeGreaterThan(width, 0, nameof(width));
        Guard.MustBeGreaterThan(height, 0, nameof(height));

        WebGPUDrawingBackend.GetCompositeTextureFormatInfo(format, out TextureFormat textureFormat, out FeatureName requiredFeature);

        using WebGPUHandle.HandleReference deviceReference = deviceHandle.AcquireReference();

        Device* device = (Device*)deviceReference.Handle;
        if (requiredFeature != FeatureName.Undefined &&
            !WebGPURuntime.GetOrCreateDeviceState(api, deviceHandle).HasFeature(requiredFeature))
        {
            throw new NotSupportedException($"The WebGPU device does not support required feature '{requiredFeature}' for texture format '{format}'.");
        }

        TextureDescriptor textureDescriptor = new()
        {
            Usage = TextureUsage.RenderAttachment | TextureUsage.CopySrc | TextureUsage.CopyDst | TextureUsage.TextureBinding | TextureUsage.StorageBinding,
            Dimension = TextureDimension.Dimension2D,
            Size = new Extent3D((uint)width, (uint)height, 1),
            Format = textureFormat,
            MipLevelCount = 1,
            SampleCount = 1,
        };

        Texture* texture = api.DeviceCreateTexture(device, in textureDescriptor);
        if (texture is null)
        {
            throw new InvalidOperationException("The WebGPU device could not create a render-target texture.");
        }

        TextureViewDescriptor textureViewDescriptor = new()
        {
            Format = textureFormat,
            Dimension = TextureViewDimension.Dimension2D,
            BaseMipLevel = 0,
            MipLevelCount = 1,
            BaseArrayLayer = 0,
            ArrayLayerCount = 1,
            Aspect = TextureAspect.All,
        };

        TextureView* textureView = api.TextureCreateView(texture, in textureViewDescriptor);
        if (textureView is null)
        {
            api.TextureRelease(texture);
            throw new InvalidOperationException("The WebGPU device could not create a render-target texture view.");
        }

        WebGPUTextureHandle? createdTextureHandle = null;
        WebGPUTextureViewHandle? createdTextureViewHandle = null;
        try
        {
            createdTextureHandle = new WebGPUTextureHandle(api, (nint)texture, ownsHandle: true);
            createdTextureViewHandle = new WebGPUTextureViewHandle(api, (nint)textureView, ownsHandle: true);
            WebGPUNativeSurface surface = Create(
                deviceHandle,
                queueHandle,
                createdTextureHandle,
                createdTextureViewHandle,
                format,
                width,
                height);

            textureHandle = createdTextureHandle;
            textureViewHandle = createdTextureViewHandle;
            return surface;
        }
        catch
        {
            createdTextureViewHandle?.Dispose();
            createdTextureHandle?.Dispose();

            if (createdTextureViewHandle is null)
            {
                api.TextureViewRelease(textureView);
            }

            if (createdTextureHandle is null)
            {
                api.TextureRelease(texture);
            }

            throw;
        }
    }

    /// <summary>
    /// Creates a native surface over wrapped WebGPU texture handles.
    /// </summary>
    internal static WebGPUNativeSurface Create(
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

        return new WebGPUNativeSurface(
            deviceHandle,
            queueHandle,
            targetTextureHandle,
            targetTextureViewHandle,
            targetFormat,
            width,
            height);
    }
}
