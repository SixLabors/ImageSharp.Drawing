// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using Silk.NET.WebGPU;
using Silk.NET.WebGPU.Extensions.WGPU;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Internal helper for benchmark/test-only native WebGPU target allocation.
/// </summary>
internal static unsafe class WebGPUTestNativeSurfaceAllocator
{
    private const int CallbackTimeoutMilliseconds = 5000;

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
            Usage = TextureUsage.RenderAttachment | TextureUsage.CopySrc | TextureUsage.CopyDst | TextureUsage.TextureBinding,
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
            isPremultipliedAlpha,
            supportsTextureSampling: true);
        error = string.Empty;
        return true;
    }

    internal static bool TryReadTexture<TPixel>(
        WebGPUDrawingBackend backend,
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

        if (!backend.TryGetInteropHandles(out nint deviceHandle, out nint queueHandle))
        {
            error = backend.TestingLastGPUInitializationFailure ?? "WebGPU backend is not initialized.";
            return false;
        }

        using WebGPURuntime.Lease lease = WebGPURuntime.Acquire();
        WebGPU api = lease.Api;
        Device* device = (Device*)deviceHandle;
        Queue* queue = (Queue*)queueHandle;

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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int Align(int value, int alignment)
        => ((value + alignment - 1) / alignment) * alignment;

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
