// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <content>
/// GPU frame-copy helpers. Copies run entirely on the GPU as texture-to-texture commands;
/// both frames must share the same device and queue and use the pixel type's native format.
/// </content>
public sealed unsafe partial class WebGPUDrawingBackend
{
    /// <inheritdoc />
    public void CopyPixels<TPixel>(
        Configuration configuration,
        ICanvasFrame<TPixel> source,
        ICanvasFrame<TPixel> target,
        Rectangle sourceRectangle,
        Point targetPoint)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        this.ThrowIfDisposed();
        Guard.NotNull(configuration, nameof(configuration));

        if (!source.TryGetNativeSurface(out NativeSurface? sourceSurface) || sourceSurface is not WebGPUNativeSurface nativeSource)
        {
            throw new NotSupportedException("The source frame does not expose a WebGPU native surface.");
        }

        if (!target.TryGetNativeSurface(out NativeSurface? targetSurface) || targetSurface is not WebGPUNativeSurface nativeTarget)
        {
            throw new NotSupportedException("The target frame does not expose a WebGPU native surface.");
        }

        if (nativeSource.DeviceHandle.IsInvalid ||
            nativeSource.QueueHandle.IsInvalid ||
            nativeSource.TargetTextureHandle.IsInvalid ||
            nativeTarget.DeviceHandle.IsInvalid ||
            nativeTarget.QueueHandle.IsInvalid ||
            nativeTarget.TargetTextureHandle.IsInvalid)
        {
            throw new NotSupportedException("The source and target frames must expose valid WebGPU device, queue, and texture handles.");
        }

        if (!TryGetCompositeTargetDescriptor<TPixel>(out WebGPUTargetDescriptor expectedTargetDescriptor, out _))
        {
            throw new NotSupportedException($"The WebGPU backend does not support pixel format '{typeof(TPixel).Name}'.");
        }

        if (nativeSource.TargetDescriptor != expectedTargetDescriptor || nativeTarget.TargetDescriptor != expectedTargetDescriptor)
        {
            throw new NotSupportedException($"Pixel type '{typeof(TPixel).Name}' does not match one of the WebGPU target formats.");
        }

        // Clip in two stages: first the request against the source bounds, then the shifted
        // result against the target bounds. Out-of-range copies clip silently rather than throw.
        Rectangle sourceBounds = new(0, 0, source.Bounds.Width, source.Bounds.Height);
        Rectangle clippedSource = Rectangle.Intersect(sourceBounds, sourceRectangle);
        if (clippedSource.Width <= 0 || clippedSource.Height <= 0)
        {
            return;
        }

        Rectangle targetBounds = new(0, 0, target.Bounds.Width, target.Bounds.Height);
        Rectangle targetRectangle = new(
            targetPoint.X + clippedSource.X - sourceRectangle.X,
            targetPoint.Y + clippedSource.Y - sourceRectangle.Y,
            clippedSource.Width,
            clippedSource.Height);

        Rectangle clippedTarget = Rectangle.Intersect(targetBounds, targetRectangle);

        if (clippedTarget.Width <= 0 || clippedTarget.Height <= 0)
        {
            return;
        }

        // Propagate any target-side clipping back to the source origin so both rectangles
        // describe the same clipped extent.
        int sourceX = clippedSource.X + clippedTarget.X - targetRectangle.X;
        int sourceY = clippedSource.Y + clippedTarget.Y - targetRectangle.Y;
        int targetX = clippedTarget.X;
        int targetY = clippedTarget.Y;
        int width = clippedTarget.Width;
        int height = clippedTarget.Height;

        // Frame bounds are logical canvas coordinates; native surfaces may be a view into
        // a larger texture, so translate once before recording the texture copy.
        int nativeSourceX = source.Bounds.X + nativeSource.TextureCoordinateOffset.X + sourceX;
        int nativeSourceY = source.Bounds.Y + nativeSource.TextureCoordinateOffset.Y + sourceY;
        int nativeTargetX = target.Bounds.X + nativeTarget.TextureCoordinateOffset.X + targetX;
        int nativeTargetY = target.Bounds.Y + nativeTarget.TextureCoordinateOffset.Y + targetY;

        WebGPU api = WebGPURuntime.GetApi();
        using WebGPUHandle.HandleReference sourceDeviceReference = nativeSource.DeviceHandle.AcquireReference();
        using WebGPUHandle.HandleReference targetDeviceReference = nativeTarget.DeviceHandle.AcquireReference();
        using WebGPUHandle.HandleReference sourceQueueReference = nativeSource.QueueHandle.AcquireReference();
        using WebGPUHandle.HandleReference targetQueueReference = nativeTarget.QueueHandle.AcquireReference();
        using WebGPUHandle.HandleReference sourceTextureReference = nativeSource.TargetTextureHandle.AcquireReference();
        using WebGPUHandle.HandleReference targetTextureReference = nativeTarget.TargetTextureHandle.AcquireReference();

        // CommandEncoderCopyTextureToTexture cannot cross devices or queues; a cross-device
        // copy would need a CPU round trip, which this GPU-only path does not provide.
        if (sourceDeviceReference.Handle != targetDeviceReference.Handle || sourceQueueReference.Handle != targetQueueReference.Handle)
        {
            throw new NotSupportedException("WebGPU frame pixel copies require source and target frames from the same device and queue.");
        }

        CommandEncoder* commandEncoder = null;
        CommandBuffer* commandBuffer = null;

        try
        {
            Device* device = (Device*)targetDeviceReference.Handle;
            CommandEncoderDescriptor encoderDescriptor = default;
            commandEncoder = api.DeviceCreateCommandEncoder(device, in encoderDescriptor);
            if (commandEncoder is null)
            {
                throw new InvalidOperationException("The WebGPU device could not create a command encoder for the pixel copy.");
            }

            ImageCopyTexture textureSource = new()
            {
                texture = (Texture*)sourceTextureReference.Handle,
                mipLevel = 0,
                origin = new Origin3D((uint)nativeSourceX, (uint)nativeSourceY, 0),
                aspect = TextureAspect.All
            };

            ImageCopyTexture textureTarget = new()
            {
                texture = (Texture*)targetTextureReference.Handle,
                mipLevel = 0,
                origin = new Origin3D((uint)nativeTargetX, (uint)nativeTargetY, 0),
                aspect = TextureAspect.All
            };

            Extent3D copySize = new((uint)width, (uint)height, 1);
            api.CommandEncoderCopyTextureToTexture(commandEncoder, in textureSource, in textureTarget, in copySize);

            CommandBufferDescriptor commandBufferDescriptor = default;
            commandBuffer = api.CommandEncoderFinish(commandEncoder, in commandBufferDescriptor);
            if (commandBuffer is null)
            {
                throw new InvalidOperationException("The WebGPU device could not finalize the pixel-copy command buffer.");
            }

            api.QueueSubmit((Queue*)targetQueueReference.Handle, 1, ref commandBuffer);
            api.CommandBufferRelease(commandBuffer);
            commandBuffer = null;
            api.CommandEncoderRelease(commandEncoder);
            commandEncoder = null;
        }
        finally
        {
            if (commandBuffer is not null)
            {
                api.CommandBufferRelease(commandBuffer);
            }

            if (commandEncoder is not null)
            {
                api.CommandEncoderRelease(commandEncoder);
            }
        }
    }
}
