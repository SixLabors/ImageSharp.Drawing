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
/// Internal WebGPU texture upload, readback, and release helpers used by tests and benchmarks.
/// </summary>
internal static unsafe class WebGPUTextureTransfer
{
    private const int CallbackTimeoutMilliseconds = 5000;

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
        Guard.NotNull(image, nameof(image));

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

        WebGPU api = WebGPURuntime.GetApi();
        if (!WebGPURuntime.TryGetOrCreateDevice(out _, out Queue* queue, out string? deviceError))
        {
            error = deviceError ?? "WebGPU device auto-provisioning failed.";
            return false;
        }

        try
        {
            Buffer2DRegion<TPixel> sourceRegion = new(image.Frames.RootFrame.PixelBuffer, image.Bounds);
            WebGPUFlushContext.UploadTextureFromRegion(
                api,
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

        WebGPU api = WebGPURuntime.GetApi();
        Wgpu wgpuExtension = WebGPURuntime.GetWgpuExtension();
        if (!WebGPURuntime.TryGetOrCreateDevice(out Device* device, out Queue* queue, out string? deviceError))
        {
            error = deviceError ?? "WebGPU device auto-provisioning failed.";
            return false;
        }

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
                MappedAtCreation = false,
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
                Aspect = TextureAspect.All,
            };

            ImageCopyBuffer destination = new()
            {
                Buffer = readbackBuffer,
                Layout = new TextureDataLayout
                {
                    Offset = 0,
                    BytesPerRow = (uint)readbackRowBytes,
                    RowsPerImage = (uint)height,
                },
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
            if (!WaitForSignal(wgpuExtension, device, mapReady) || mapStatus != BufferMapAsyncStatus.Success)
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
    /// Releases native texture and texture-view handles.
    /// </summary>
    internal static void Release(nint textureHandle, nint textureViewHandle)
    {
        if (textureHandle == 0 && textureViewHandle == 0)
        {
            return;
        }

        WebGPU api = WebGPURuntime.GetApi();

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
