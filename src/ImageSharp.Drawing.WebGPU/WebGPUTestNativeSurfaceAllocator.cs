// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Diagnostics;
using System.Runtime.CompilerServices;
using Silk.NET.WebGPU;
using Silk.NET.WebGPU.Extensions.WGPU;
using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Internal helper for benchmark/test-only native WebGPU target allocation.
/// </summary>
internal static unsafe class WebGPUTestNativeSurfaceAllocator
{
    private const int CallbackTimeoutMilliseconds = 5000;

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
        if (!WebGPUDrawingBackend.TryGetCompositeTextureFormat<TPixel>(out WebGPUTextureFormatId formatId, out FeatureName requiredFeature))
        {
            surface = new NativeSurface(TPixel.GetPixelTypeInfo());
            textureHandle = 0;
            textureViewHandle = 0;
            error = $"Pixel type '{typeof(TPixel).Name}' is not supported by the WebGPU backend.";
            return false;
        }

        using WebGPURuntime.Lease lease = WebGPURuntime.Acquire();
        WebGPU api = lease.Api;

        if (!WebGPURuntime.TryGetOrCreateDevice(out Device* device, out Queue* queue, out string? deviceError))
        {
            surface = new NativeSurface(TPixel.GetPixelTypeInfo());
            textureHandle = 0;
            textureViewHandle = 0;
            error = deviceError ?? "WebGPU device auto-provisioning failed.";
            return false;
        }

        if (requiredFeature != FeatureName.Undefined
            && !WebGPURuntime.GetOrCreateDeviceState(api, device).HasFeature(requiredFeature))
        {
            surface = new NativeSurface(TPixel.GetPixelTypeInfo());
            textureHandle = 0;
            textureViewHandle = 0;
            error = $"Device does not support required feature '{requiredFeature}' for pixel type '{typeof(TPixel).Name}'.";
            return false;
        }

        TextureFormat textureFormat = WebGPUTextureFormatMapper.ToSilk(formatId);
        TextureDescriptor targetTextureDescriptor = new()
        {
            Usage = TextureUsage.RenderAttachment | TextureUsage.CopySrc | TextureUsage.CopyDst | TextureUsage.TextureBinding | TextureUsage.StorageBinding,
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

        nint deviceHandle = (nint)device;
        nint queueHandle = (nint)queue;
        textureHandle = (nint)targetTexture;
        textureViewHandle = (nint)targetView;
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

    /// <summary>
    /// Tries to upload CPU pixel data to an existing native WebGPU texture handle.
    /// </summary>
    internal static bool TryWriteTexture<TPixel>(
        nint textureHandle,
        int width,
        int height,
        Image<TPixel> image,
        out string error)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        if (textureHandle == 0)
        {
            error = "Texture handle is zero.";
            return false;
        }

        if (image.Width != width || image.Height != height)
        {
            error = "Source image dimensions must match the target texture dimensions.";
            return false;
        }

        using WebGPURuntime.Lease lease = WebGPURuntime.Acquire();
        if (!WebGPURuntime.TryGetOrCreateDevice(out _, out Queue* queue, out string? deviceError))
        {
            error = deviceError ?? "WebGPU device auto-provisioning failed.";
            return false;
        }

        try
        {
            Buffer2DRegion<TPixel> sourceRegion = new(image.Frames.RootFrame.PixelBuffer, image.Bounds);
            WebGPUFlushContext.UploadTextureFromRegion(
                lease.Api,
                queue,
                (Texture*)textureHandle,
                sourceRegion,
                Configuration.Default.MemoryAllocator);
            error = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>
    /// Tries to read pixels from a native WebGPU texture handle into an <see cref="Image{TPixel}"/>.
    /// </summary>
    internal static bool TryReadTexture<TPixel>(
        nint textureHandle,
        int width,
        int height,
        out Image<TPixel>? image,
        out string error)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        image = null;
        if (textureHandle == 0)
        {
            error = "Texture handle is zero.";
            return false;
        }

        if (width <= 0 || height <= 0)
        {
            error = "Texture dimensions must be greater than zero.";
            return false;
        }

        using WebGPURuntime.Lease lease = WebGPURuntime.Acquire();
        if (!WebGPURuntime.TryGetOrCreateDevice(out Device* device, out Queue* queue, out string? deviceError))
        {
            error = deviceError ?? "WebGPU device auto-provisioning failed.";
            return false;
        }

        WebGPU api = lease.Api;
        int pixelSizeInBytes = Unsafe.SizeOf<TPixel>();
        int packedRowBytes = checked(width * pixelSizeInBytes);
        int readbackRowBytes = Align(packedRowBytes, 256);
        int packedByteCount = checked(packedRowBytes * height);
        ulong readbackByteCount = checked((ulong)readbackRowBytes * (ulong)height);

        Silk.NET.WebGPU.Buffer* readbackBuffer = null;
        CommandEncoder* commandEncoder = null;
        CommandBuffer* commandBuffer = null;
        try
        {
            BufferDescriptor bufferDescriptor = new()
            {
                Usage = BufferUsage.CopyDst | BufferUsage.MapRead,
                Size = readbackByteCount,
                MappedAtCreation = false
            };

            readbackBuffer = api.DeviceCreateBuffer(device, in bufferDescriptor);
            if (readbackBuffer is null)
            {
                error = "WebGPU.DeviceCreateBuffer returned null for readback.";
                return false;
            }

            CommandEncoderDescriptor encoderDescriptor = default;
            commandEncoder = api.DeviceCreateCommandEncoder(device, in encoderDescriptor);
            if (commandEncoder is null)
            {
                error = "WebGPU.DeviceCreateCommandEncoder returned null.";
                return false;
            }

            ImageCopyTexture source = new()
            {
                Texture = (Texture*)textureHandle,
                MipLevel = 0,
                Origin = new Origin3D(0, 0, 0),
                Aspect = TextureAspect.All
            };

            ImageCopyBuffer destination = new()
            {
                Buffer = readbackBuffer,
                Layout = new TextureDataLayout
                {
                    Offset = 0,
                    BytesPerRow = (uint)readbackRowBytes,
                    RowsPerImage = (uint)height
                }
            };

            Extent3D copySize = new((uint)width, (uint)height, 1);
            api.CommandEncoderCopyTextureToBuffer(commandEncoder, in source, in destination, in copySize);

            CommandBufferDescriptor commandBufferDescriptor = default;
            commandBuffer = api.CommandEncoderFinish(commandEncoder, in commandBufferDescriptor);
            if (commandBuffer is null)
            {
                error = "WebGPU.CommandEncoderFinish returned null.";
                return false;
            }

            api.QueueSubmit(queue, 1, ref commandBuffer);
            api.CommandBufferRelease(commandBuffer);
            commandBuffer = null;
            api.CommandEncoderRelease(commandEncoder);
            commandEncoder = null;

            BufferMapAsyncStatus mapStatus = BufferMapAsyncStatus.Unknown;
            using ManualResetEventSlim mapReady = new(false);
            void Callback(BufferMapAsyncStatus status, void* userData)
            {
                _ = userData;
                mapStatus = status;
                mapReady.Set();
            }

            using PfnBufferMapCallback callback = PfnBufferMapCallback.From(Callback);
            api.BufferMapAsync(readbackBuffer, MapMode.Read, 0, (nuint)readbackByteCount, callback, null);
            if (!WaitForSignal(lease.WgpuExtension, device, mapReady) || mapStatus != BufferMapAsyncStatus.Success)
            {
                error = $"WebGPU readback map failed with status '{mapStatus}'.";
                return false;
            }

            void* mapped = api.BufferGetConstMappedRange(readbackBuffer, 0, (nuint)readbackByteCount);
            if (mapped is null)
            {
                api.BufferUnmap(readbackBuffer);
                error = "WebGPU.BufferGetConstMappedRange returned null.";
                return false;
            }

            try
            {
                ReadOnlySpan<byte> readback = new(mapped, checked((int)readbackByteCount));
                byte[] packed = new byte[packedByteCount];
                Span<byte> packedSpan = packed;
                for (int y = 0; y < height; y++)
                {
                    readback
                        .Slice(y * readbackRowBytes, packedRowBytes)
                        .CopyTo(packedSpan.Slice(y * packedRowBytes, packedRowBytes));
                }

                image = Image.LoadPixelData<TPixel>(packed, width, height);
                error = string.Empty;
                return true;
            }
            finally
            {
                api.BufferUnmap(readbackBuffer);
            }
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

            if (readbackBuffer is not null)
            {
                api.BufferRelease(readbackBuffer);
            }
        }
    }

    /// <summary>
    /// Releases native texture and texture-view handles allocated for tests.
    /// </summary>
    /// <param name="textureHandle">The native texture handle.</param>
    /// <param name="textureViewHandle">The native texture-view handle.</param>
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

    /// <summary>
    /// Rounds <paramref name="value"/> up to the next multiple of <paramref name="alignment"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int Align(int value, int alignment)
        => ((value + alignment - 1) / alignment) * alignment;

    /// <summary>
    /// Pumps the WebGPU device while waiting for one asynchronous map callback to signal completion.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool WaitForSignal(Wgpu? extension, Device* device, ManualResetEventSlim signal)
    {
        if (extension is null)
        {
            return signal.Wait(CallbackTimeoutMilliseconds);
        }

        Stopwatch stopwatch = Stopwatch.StartNew();
        while (!signal.IsSet && stopwatch.ElapsedMilliseconds < CallbackTimeoutMilliseconds)
        {
            _ = extension.DevicePoll(device, true, (WrappedSubmissionIndex*)null);
        }

        return signal.IsSet;
    }
}
