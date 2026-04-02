// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using Silk.NET.WebGPU;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Shared WebGPU render-target allocation helpers used by the public target API and internal tests.
/// </summary>
internal static unsafe class WebGPURenderTargetAllocation
{
    /// <summary>
    /// Tries to allocate a WebGPU render target for the specified pixel type.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    /// <param name="api">The WebGPU API instance used to allocate native resources.</param>
    /// <param name="deviceHandle">The opaque <c>WGPUDevice*</c> handle.</param>
    /// <param name="queueHandle">The opaque <c>WGPUQueue*</c> handle.</param>
    /// <param name="width">The texture width in pixels.</param>
    /// <param name="height">The texture height in pixels.</param>
    /// <param name="surface">Receives the native surface wrapping the allocated texture.</param>
    /// <param name="textureHandle">Receives the allocated opaque <c>WGPUTexture*</c> handle.</param>
    /// <param name="textureViewHandle">Receives the allocated opaque <c>WGPUTextureView*</c> handle.</param>
    /// <param name="formatId">Receives the allocated texture format identifier.</param>
    /// <param name="error">Receives the failure reason when allocation fails.</param>
    /// <returns><see langword="true"/> when allocation succeeds; otherwise <see langword="false"/>.</returns>
    internal static bool TryCreateRenderTarget<TPixel>(
        WebGPU api,
        nint deviceHandle,
        nint queueHandle,
        int width,
        int height,
        out NativeSurface surface,
        out nint textureHandle,
        out nint textureViewHandle,
        out WebGPUTextureFormatId formatId,
        out string error)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        surface = new NativeSurface(TPixel.GetPixelTypeInfo());
        textureHandle = 0;
        textureViewHandle = 0;
        formatId = default;

        if (deviceHandle == 0)
        {
            error = "Device handle must be non-zero.";
            return false;
        }

        if (queueHandle == 0)
        {
            error = "Queue handle must be non-zero.";
            return false;
        }

        if (width <= 0)
        {
            error = "Width must be greater than zero.";
            return false;
        }

        if (height <= 0)
        {
            error = "Height must be greater than zero.";
            return false;
        }

        if (!WebGPUDrawingBackend.TryGetCompositeTextureFormat<TPixel>(out formatId, out FeatureName requiredFeature))
        {
            error = $"Pixel type '{typeof(TPixel).Name}' is not supported by the WebGPU backend.";
            return false;
        }

        Device* device = (Device*)deviceHandle;
        if (requiredFeature != FeatureName.Undefined &&
            !WebGPURuntime.GetOrCreateDeviceState(api, device).HasFeature(requiredFeature))
        {
            error = $"Device does not support required feature '{requiredFeature}' for pixel type '{typeof(TPixel).Name}'.";
            return false;
        }

        TextureFormat textureFormat = WebGPUTextureFormatMapper.ToSilk(formatId);
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
            error = "WebGPU.DeviceCreateTexture returned null.";
            return false;
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
            error = "WebGPU.TextureCreateView returned null.";
            return false;
        }

        textureHandle = (nint)texture;
        textureViewHandle = (nint)textureView;
        surface = WebGPUNativeSurfaceFactory.Create<TPixel>(
            deviceHandle,
            queueHandle,
            textureHandle,
            textureViewHandle,
            formatId,
            width,
            height);
        error = string.Empty;
        return true;
    }
}
