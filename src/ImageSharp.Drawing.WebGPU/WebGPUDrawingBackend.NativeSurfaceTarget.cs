// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Diagnostics.CodeAnalysis;
using Silk.NET.WebGPU;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

internal sealed unsafe partial class WebGPUDrawingBackend
{
    internal bool TryCreateNativeSurfaceTarget<TPixel>(
        int width,
        int height,
        bool isSrgb,
        bool isPremultipliedAlpha,
        [NotNullWhen(true)] out NativeSurface? surface,
        out nint textureHandle,
        out nint textureViewHandle)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        if (!CompositePixelHandlers.TryGetValue(typeof(TPixel), out CompositePixelRegistration pixelHandler))
        {
            surface = null;
            textureHandle = 0;
            textureViewHandle = 0;
            return false;
        }

        return this.TryCreateNativeSurfaceTarget(
            TPixel.GetPixelTypeInfo(),
            width,
            height,
            pixelHandler.TextureFormat,
            isSrgb,
            isPremultipliedAlpha,
            out surface,
            out textureHandle,
            out textureViewHandle);
    }

    internal bool TryCreateNativeSurfaceTarget(
        PixelTypeInfo pixelType,
        int width,
        int height,
        TextureFormat textureFormat,
        bool isSrgb,
        bool isPremultipliedAlpha,
        [NotNullWhen(true)] out NativeSurface? surface,
        out nint textureHandle,
        out nint textureViewHandle)
    {
        this.ThrowIfDisposed();

        surface = null;
        textureHandle = 0;
        textureViewHandle = 0;

        if (!this.IsGPUReady || width <= 0 || height <= 0)
        {
            return false;
        }

        lock (this.gpuSync)
        {
            if (!this.TryGetGPUState(out GPUState gpuState))
            {
                return false;
            }

            TextureDescriptor targetTextureDescriptor = new()
            {
                Usage = TextureUsage.RenderAttachment | TextureUsage.CopySrc | TextureUsage.CopyDst,
                Dimension = TextureDimension.Dimension2D,
                Size = new Extent3D((uint)width, (uint)height, 1),
                Format = textureFormat,
                MipLevelCount = 1,
                SampleCount = 1
            };

            Texture* targetTexture = gpuState.Api.DeviceCreateTexture(gpuState.Device, in targetTextureDescriptor);
            if (targetTexture is null)
            {
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

            TextureView* targetView = gpuState.Api.TextureCreateView(targetTexture, in targetViewDescriptor);
            if (targetView is null)
            {
                this.ReleaseTextureLocked(targetTexture);
                return false;
            }

            textureHandle = (nint)targetTexture;
            textureViewHandle = (nint)targetView;

            NativeSurface nativeSurface = new(pixelType);
            nativeSurface.SetCapability(new WebGPUSurfaceCapability(
                (nint)gpuState.Device,
                (nint)gpuState.Queue,
                textureHandle,
                textureViewHandle,
                textureFormat,
                width,
                height,
                isSrgb,
                isPremultipliedAlpha));

            surface = nativeSurface;
            return true;
        }
    }

    internal void ReleaseNativeSurfaceTarget(nint textureHandle, nint textureViewHandle)
    {
        if ((textureHandle == 0 && textureViewHandle == 0) || this.isDisposed)
        {
            return;
        }

        lock (this.gpuSync)
        {
            if (textureViewHandle != 0)
            {
                this.ReleaseTextureViewLocked((TextureView*)textureViewHandle);
            }

            if (textureHandle != 0)
            {
                this.ReleaseTextureLocked((Texture*)textureHandle);
            }
        }
    }
}
