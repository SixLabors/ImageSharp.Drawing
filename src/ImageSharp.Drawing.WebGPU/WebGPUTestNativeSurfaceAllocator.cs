// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using Silk.NET.WebGPU;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Internal helper for benchmark/test-only native WebGPU target allocation.
/// </summary>
internal static unsafe class WebGPUTestNativeSurfaceAllocator
{
    internal static bool TryCreate<TPixel>(
        WebGPUDrawingBackend backend,
        int width,
        int height,
        bool isSrgb,
        bool isPremultipliedAlpha,
        out NativeSurface surface,
        out nint textureHandle,
        out nint textureViewHandle,
        out string error)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        if (!backend.TryGetInteropHandles(out nint deviceHandle, out nint queueHandle))
        {
            surface = new NativeSurface(TPixel.GetPixelTypeInfo());
            textureHandle = 0;
            textureViewHandle = 0;
            error = "WebGPU backend is not initialized.";
            return false;
        }

        if (!WebGPUDrawingBackend.TryGetCompositeTextureFormat<TPixel>(out WebGPUTextureFormatId formatId))
        {
            surface = new NativeSurface(TPixel.GetPixelTypeInfo());
            textureHandle = 0;
            textureViewHandle = 0;
            error = $"Pixel type '{typeof(TPixel).Name}' is not supported by the WebGPU backend.";
            return false;
        }

        TextureFormat textureFormat = WebGPUTextureFormatMapper.ToSilk(formatId);

        // Lease.Dispose only decrements the runtime ref-count; it does not dispose the shared WebGPU API.
        using WebGPURuntime.Lease lease = WebGPURuntime.Acquire();
        WebGPU api = lease.Api;
        Device* device = (Device*)deviceHandle;

        TextureDescriptor targetTextureDescriptor = new()
        {
            Usage = TextureUsage.RenderAttachment | TextureUsage.CopySrc | TextureUsage.CopyDst,
            Dimension = TextureDimension.Dimension2D,
            Size = new Extent3D((uint)width, (uint)height, 1),
            Format = textureFormat,
            MipLevelCount = 1,
            SampleCount = 1
        };

        Texture* targetTexture = api.DeviceCreateTexture(device, in targetTextureDescriptor);
        if (targetTexture is null)
        {
            surface = new NativeSurface(TPixel.GetPixelTypeInfo());
            textureHandle = 0;
            textureViewHandle = 0;
            error = "WebGPU.DeviceCreateTexture returned null.";
            return false;
        }

        TextureViewDescriptor targetViewDescriptor = new()
        {
            Format = textureFormat,
            Dimension = TextureViewDimension.Dimension2D,
            BaseMipLevel = 0,
            MipLevelCount = 1,
            BaseArrayLayer = 0,
            ArrayLayerCount = 1,
            Aspect = TextureAspect.All
        };

        TextureView* targetView = api.TextureCreateView(targetTexture, in targetViewDescriptor);
        if (targetView is null)
        {
            api.TextureRelease(targetTexture);
            surface = new NativeSurface(TPixel.GetPixelTypeInfo());
            textureHandle = 0;
            textureViewHandle = 0;
            error = "WebGPU.TextureCreateView returned null.";
            return false;
        }

        textureHandle = (nint)targetTexture;
        textureViewHandle = (nint)targetView;
        surface = WebGPUNativeSurfaceFactory.Create<TPixel>(
            deviceHandle,
            queueHandle,
            textureHandle,
            textureViewHandle,
            formatId,
            width,
            height,
            isSrgb,
            isPremultipliedAlpha);
        error = string.Empty;
        return true;
    }

    internal static void Release(nint textureHandle, nint textureViewHandle)
    {
        if (textureHandle == 0 && textureViewHandle == 0)
        {
            return;
        }

        // Keep the runtime alive while releasing native handles.
        using WebGPURuntime.Lease lease = WebGPURuntime.Acquire();
        WebGPU api = lease.Api;
        if (textureViewHandle != 0)
        {
            api.TextureViewRelease((TextureView*)textureViewHandle);
        }

        if (textureHandle != 0)
        {
            api.TextureRelease((Texture*)textureHandle);
        }
    }
}
