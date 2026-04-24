// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Diagnostics.CodeAnalysis;
using Silk.NET.WebGPU;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Shared WebGPU render-target allocation helpers used by the public target API.
/// </summary>
internal static unsafe class WebGPURenderTargetAllocation
{
    /// <summary>
    /// Tries to allocate a WebGPU render target for the specified pixel type.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    /// <param name="api">The WebGPU API instance used to allocate native resources.</param>
    /// <param name="deviceHandle">The wrapped <c>WGPUDevice*</c> handle.</param>
    /// <param name="queueHandle">The wrapped <c>WGPUQueue*</c> handle.</param>
    /// <param name="width">The texture width in pixels.</param>
    /// <param name="height">The texture height in pixels.</param>
    /// <param name="surface">Receives the native surface wrapping the allocated texture.</param>
    /// <param name="textureHandle">Receives the allocated wrapped <c>WGPUTexture*</c> handle.</param>
    /// <param name="textureViewHandle">Receives the allocated wrapped <c>WGPUTextureView*</c> handle.</param>
    /// <param name="formatId">Receives the allocated texture format identifier.</param>
    /// <param name="error">Receives the failure reason when allocation fails.</param>
    /// <returns><see langword="true"/> when allocation succeeds; otherwise <see langword="false"/>.</returns>
    internal static bool TryCreateRenderTarget<TPixel>(
        WebGPU api,
        WebGPUDeviceHandle deviceHandle,
        WebGPUQueueHandle queueHandle,
        int width,
        int height,
        out NativeSurface surface,
        [NotNullWhen(true)] out WebGPUTextureHandle? textureHandle,
        [NotNullWhen(true)] out WebGPUTextureViewHandle? textureViewHandle,
        out WebGPUTextureFormatId formatId,
        out string error)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        surface = new NativeSurface(TPixel.GetPixelTypeInfo());
        textureHandle = null;
        textureViewHandle = null;
        formatId = default;

        if (deviceHandle.IsInvalid)
        {
            error = "Device handle must be non-zero.";
            return false;
        }

        if (queueHandle.IsInvalid)
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

        using WebGPUHandle.HandleReference deviceReference = deviceHandle.AcquireReference();

        Device* device = (Device*)deviceReference.Handle;
        if (requiredFeature != FeatureName.Undefined &&
            !WebGPURuntime.GetOrCreateDeviceState(api, deviceHandle).HasFeature(requiredFeature))
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

        WebGPUTextureHandle? createdTextureHandle = null;
        WebGPUTextureViewHandle? createdTextureViewHandle = null;
        try
        {
            createdTextureHandle = new WebGPUTextureHandle(api, (nint)texture, ownsHandle: true);
            createdTextureViewHandle = new WebGPUTextureViewHandle(api, (nint)textureView, ownsHandle: true);
            surface = WebGPUNativeSurfaceFactory.Create<TPixel>(
                deviceHandle,
                queueHandle,
                createdTextureHandle,
                createdTextureViewHandle,
                formatId,
                width,
                height);
            textureHandle = createdTextureHandle;
            textureViewHandle = createdTextureViewHandle;
            error = string.Empty;
            return true;
        }
        catch (Exception ex)
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

            error = ex.Message;
            return false;
        }
    }
}
