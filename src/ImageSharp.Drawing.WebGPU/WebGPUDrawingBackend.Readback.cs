// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Silk.NET.WebGPU;
using Silk.NET.WebGPU.Extensions.WGPU;
using SixLabors.ImageSharp.PixelFormats;
using WgpuBuffer = Silk.NET.WebGPU.Buffer;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <content>
/// GPU readback helpers.
/// </content>
public sealed unsafe partial class WebGPUDrawingBackend
{
    private const int ReadbackCallbackTimeoutMilliseconds = 5000;

    /// <inheritdoc />
    public bool TryReadRegion<TPixel>(
        Configuration configuration,
        ICanvasFrame<TPixel> target,
        Rectangle sourceRectangle,
        [NotNullWhen(true)] out Image<TPixel>? image)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        this.ThrowIfDisposed();
        Guard.NotNull(configuration, nameof(configuration));
        Guard.NotNull(target, nameof(target));

        image = null;

        // When a CPU-backed frame is used with this backend (for example in parity tests),
        // delegate to the default CPU readback implementation.
        if (target.TryGetCpuRegion(out _))
        {
            return this.fallbackBackend.TryReadRegion(configuration, target, sourceRectangle, out image);
        }

        // Readback is only available for native WebGPU targets with valid interop handles.
        if (!target.TryGetNativeSurface(out NativeSurface? nativeSurface) ||
            !nativeSurface.TryGetCapability(out WebGPUSurfaceCapability? capability) ||
            capability.Device == 0 ||
            capability.Queue == 0 ||
            capability.TargetTexture == 0)
        {
            return false;
        }

        if (!TryGetCompositeTextureFormat<TPixel>(out WebGPUTextureFormatId expectedFormat, out FeatureName requiredFeature) ||
            expectedFormat != capability.TargetFormat)
        {
            return false;
        }

        // Convert canvas-local source coordinates to absolute native-surface coordinates.
        Rectangle absoluteSource = new(
            target.Bounds.X + sourceRectangle.X,
            target.Bounds.Y + sourceRectangle.Y,
            sourceRectangle.Width,
            sourceRectangle.Height);
        Rectangle surfaceBounds = new(0, 0, capability.Width, capability.Height);
        Rectangle source = Rectangle.Intersect(surfaceBounds, absoluteSource);
        if (source.Width <= 0 || source.Height <= 0)
        {
            return false;
        }

        using WebGPURuntime.Lease lease = WebGPURuntime.Acquire();
        WebGPU api = lease.Api;
        Device* device = (Device*)capability.Device;

        if (requiredFeature != FeatureName.Undefined
            && !WebGPUFlushContext.GetOrCreateDeviceState(api, device).HasFeature(requiredFeature))
        {
            return false;
        }

        Queue* queue = (Queue*)capability.Queue;

        int pixelSizeInBytes = Unsafe.SizeOf<TPixel>();
        int packedRowBytes = checked(source.Width * pixelSizeInBytes);

        // WebGPU copy-to-buffer requires bytes-per-row alignment to 256 bytes.
        int readbackRowBytes = Align(packedRowBytes, 256);
        int packedByteCount = checked(packedRowBytes * source.Height);
        ulong readbackByteCount = checked((ulong)readbackRowBytes * (ulong)source.Height);

        WgpuBuffer* readbackBuffer = null;
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
                return false;
            }

            CommandEncoderDescriptor encoderDescriptor = default;
            commandEncoder = api.DeviceCreateCommandEncoder(device, in encoderDescriptor);
            if (commandEncoder is null)
            {
                return false;
            }

            // Copy only the requested source rect from the target texture into the readback buffer.
            ImageCopyTexture sourceCopy = new()
            {
                Texture = (Texture*)capability.TargetTexture,
                MipLevel = 0,
                Origin = new Origin3D((uint)source.X, (uint)source.Y, 0),
                Aspect = TextureAspect.All
            };

            ImageCopyBuffer destinationCopy = new()
            {
                Buffer = readbackBuffer,
                Layout = new TextureDataLayout
                {
                    Offset = 0,
                    BytesPerRow = (uint)readbackRowBytes,
                    RowsPerImage = (uint)source.Height
                }
            };

            Extent3D copySize = new((uint)source.Width, (uint)source.Height, 1);
            api.CommandEncoderCopyTextureToBuffer(commandEncoder, in sourceCopy, in destinationCopy, in copySize);

            CommandBufferDescriptor commandBufferDescriptor = default;
            commandBuffer = api.CommandEncoderFinish(commandEncoder, in commandBufferDescriptor);
            if (commandBuffer is null)
            {
                return false;
            }

            api.QueueSubmit(queue, 1, ref commandBuffer);
            api.CommandBufferRelease(commandBuffer);
            commandBuffer = null;
            api.CommandEncoderRelease(commandEncoder);
            commandEncoder = null;

            // Map the GPU buffer and wait for completion before reading host-visible bytes.
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
            if (!WaitForMapSignal(lease.WgpuExtension, device, mapReady) || mapStatus != BufferMapAsyncStatus.Success)
            {
                return false;
            }

            void* mapped = api.BufferGetConstMappedRange(readbackBuffer, 0, (nuint)readbackByteCount);
            if (mapped is null)
            {
                api.BufferUnmap(readbackBuffer);
                return false;
            }

            try
            {
                ReadOnlySpan<byte> readback = new(mapped, checked((int)readbackByteCount));
                byte[] packed = new byte[packedByteCount];
                Span<byte> packedSpan = packed;

                // Strip WebGPU row padding so Image.LoadPixelData receives tightly packed rows.
                for (int y = 0; y < source.Height; y++)
                {
                    readback
                        .Slice(y * readbackRowBytes, packedRowBytes)
                        .CopyTo(packedSpan.Slice(y * packedRowBytes, packedRowBytes));
                }

                image = Image.LoadPixelData<TPixel>(configuration, packed, source.Width, source.Height);
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int Align(int value, int alignment)
        => ((value + alignment - 1) / alignment) * alignment;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool WaitForMapSignal(Wgpu? extension, Device* device, ManualResetEventSlim signal)
    {
        if (extension is null)
        {
            return signal.Wait(ReadbackCallbackTimeoutMilliseconds);
        }

        Stopwatch stopwatch = Stopwatch.StartNew();
        while (!signal.IsSet && stopwatch.ElapsedMilliseconds < ReadbackCallbackTimeoutMilliseconds)
        {
            _ = extension.DevicePoll(device, true, (WrappedSubmissionIndex*)null);
        }

        return signal.IsSet;
    }
}
