// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Silk.NET.WebGPU;
using Silk.NET.WebGPU.Extensions.WGPU;
using SixLabors.ImageSharp.Memory;
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
        Buffer2DRegion<TPixel> destination)
        where TPixel : unmanaged, IPixel<TPixel>
        => this.TryReadRegion(configuration, target, sourceRectangle, destination, out _);

    /// <summary>
    /// Attempts to read source pixels from the target into a caller-provided buffer.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    /// <param name="configuration">The active processing configuration.</param>
    /// <param name="target">The target frame.</param>
    /// <param name="sourceRectangle">Source rectangle in target-local coordinates.</param>
    /// <param name="destination">
    /// The caller-allocated region to receive the pixel data.
    /// Must be at least as large as <paramref name="sourceRectangle"/> (clamped to target bounds).
    /// </param>
    /// <param name="error">Receives the failure reason when readback cannot complete.</param>
    /// <returns><see langword="true"/> when readback succeeds; otherwise <see langword="false"/>.</returns>
    public bool TryReadRegion<TPixel>(
        Configuration configuration,
        ICanvasFrame<TPixel> target,
        Rectangle sourceRectangle,
        Buffer2DRegion<TPixel> destination,
        [NotNullWhen(false)] out string? error)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        this.ThrowIfDisposed();
        Guard.NotNull(configuration, nameof(configuration));
        Guard.NotNull(target, nameof(target));
        Guard.NotNull(destination.Buffer, nameof(destination));

        // Readback is only available for native WebGPU targets with valid interop handles.
        if (!target.TryGetNativeSurface(out NativeSurface? nativeSurface) ||
            !nativeSurface.TryGetCapability(out WebGPUSurfaceCapability? capability) ||
            capability.Device == 0 ||
            capability.Queue == 0 ||
            capability.TargetTexture == 0)
        {
            error = "The target does not expose a native WebGPU surface with valid device, queue, and texture handles for readback.";
            return false;
        }

        if (!TryGetCompositeTextureFormat<TPixel>(out WebGPUTextureFormatId expectedFormat, out FeatureName requiredFeature) ||
            expectedFormat != capability.TargetFormat)
        {
            error = $"Pixel type '{typeof(TPixel).Name}' cannot be read back from target format '{capability.TargetFormat}'.";
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
            error = "The requested readback rectangle does not intersect the target bounds.";
            return false;
        }

        WebGPU api = WebGPURuntime.GetApi();
        Wgpu wgpuExtension = WebGPURuntime.GetWgpuExtension();
        Device* device = (Device*)capability.Device;

        if (requiredFeature != FeatureName.Undefined
            && !WebGPURuntime.GetOrCreateDeviceState(api, device).HasFeature(requiredFeature))
        {
            error = $"The target device does not support WebGPU feature '{requiredFeature}' required to read back pixel type '{typeof(TPixel).Name}'.";
            return false;
        }

        Queue* queue = (Queue*)capability.Queue;

        int pixelSizeInBytes = Unsafe.SizeOf<TPixel>();
        int packedRowBytes = checked(source.Width * pixelSizeInBytes);

        // WebGPU copy-to-buffer requires bytes-per-row alignment to 256 bytes.
        int readbackRowBytes = Align(packedRowBytes, 256);
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
                error = "The WebGPU device could not create a readback buffer.";
                return false;
            }

            CommandEncoderDescriptor encoderDescriptor = default;
            commandEncoder = api.DeviceCreateCommandEncoder(device, in encoderDescriptor);
            if (commandEncoder is null)
            {
                error = "The WebGPU device could not create a command encoder for readback.";
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
                error = "The WebGPU device could not finalize the readback command buffer.";
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
            if (!WaitForMapSignal(wgpuExtension, device, mapReady) || mapStatus != BufferMapAsyncStatus.Success)
            {
                error = $"The WebGPU device could not map the readback buffer. Status: '{mapStatus}'.";
                return false;
            }

            void* mapped = api.BufferGetConstMappedRange(readbackBuffer, 0, (nuint)readbackByteCount);
            if (mapped is null)
            {
                api.BufferUnmap(readbackBuffer);
                error = "The WebGPU device mapped the readback buffer but returned no readable data.";
                return false;
            }

            try
            {
                ReadOnlySpan<byte> readback = new(mapped, checked((int)readbackByteCount));

                // Copy directly from the mapped GPU buffer into the caller's buffer,
                // stripping WebGPU row padding in the process. Single copy, no intermediate array.
                int copyHeight = Math.Min(source.Height, destination.Height);
                for (int y = 0; y < copyHeight; y++)
                {
                    readback
                        .Slice(y * readbackRowBytes, packedRowBytes)
                        .CopyTo(MemoryMarshal.AsBytes(destination.DangerousGetRowSpan(y)));
                }

                error = null;
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
    /// Aligns <paramref name="value"/> up to the next multiple of <paramref name="alignment"/>.
    /// </summary>
    /// <param name="value">The value to align.</param>
    /// <param name="alignment">The required alignment.</param>
    /// <returns>The aligned value.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int Align(int value, int alignment)
        => ((value + alignment - 1) / alignment) * alignment;

    /// <summary>
    /// Waits for the asynchronous readback map callback, pumping the native device when available.
    /// </summary>
    /// <param name="extension">The optional native WGPU extension used for device polling.</param>
    /// <param name="device">The device that owns the mapped buffer.</param>
    /// <param name="signal">The event signaled by the map callback.</param>
    /// <returns><see langword="true"/> when the callback completed before the timeout; otherwise, <see langword="false"/>.</returns>
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
