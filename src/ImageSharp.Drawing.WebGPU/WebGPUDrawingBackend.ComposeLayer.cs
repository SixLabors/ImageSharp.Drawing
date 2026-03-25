// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Silk.NET.WebGPU;
using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.PixelFormats;
using WgpuBuffer = Silk.NET.WebGPU.Buffer;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <content>
/// GPU layer compositing via <see cref="ComposeLayerComputeShader"/>.
/// </content>
public sealed unsafe partial class WebGPUDrawingBackend
{
    private const string ComposeLayerPipelineKey = "compose-layer";
    private const string ComposeLayerConfigBufferKey = "compose-layer/config";

    /// <summary>
    /// Attempts to composite a source layer onto a destination using a GPU compute shader.
    /// Returns <see langword="false"/> when GPU compositing is not available — the caller
    /// should fall back to <see cref="DefaultDrawingBackend.ComposeLayer{TPixel}"/> for
    /// CPU-backed destinations where the transfer overhead outweighs the GPU benefit.
    /// </summary>
    private bool TryComposeLayerGpu<TPixel>(
        Configuration configuration,
        ICanvasFrame<TPixel> source,
        ICanvasFrame<TPixel> destination,
        Point destinationOffset,
        GraphicsOptions options)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        if (!TryGetCompositeTextureFormat<TPixel>(out WebGPUTextureFormatId formatId, out FeatureName requiredFeature))
        {
            return false;
        }

        TextureFormat textureFormat = WebGPUTextureFormatMapper.ToSilk(formatId);

        // Only use GPU compositing when the destination is a native surface.
        // CPU-backed destinations fall back to DefaultDrawingBackend where a
        // simple pixel blend avoids the upload/readback transfer overhead.
        if (!destination.TryGetNativeSurface(out NativeSurface? nativeSurface))
        {
            return false;
        }

        int srcWidth = source.Bounds.Width;
        int srcHeight = source.Bounds.Height;
        if (srcWidth <= 0 || srcHeight <= 0)
        {
            return true; // Nothing to composite.
        }

        if (!ComposeLayerComputeShader.TryGetCode(textureFormat, out byte[] shaderCode, out _))
        {
            return false;
        }

        // TryGetCode already validates format support via TryGetInputSampleType internally.
        _ = WebGPUTextureSampleTypeHelper.TryGetInputSampleType(textureFormat, out TextureSampleType inputSampleType);

        // Create a flush context against the destination surface.
        WebGPUFlushContext? flushContext = WebGPUFlushContext.Create(
            destination,
            textureFormat,
            requiredFeature,
            configuration.MemoryAllocator);

        if (flushContext is null)
        {
            return false;
        }

        try
        {
            if (!flushContext.EnsureCommandEncoder())
            {
                return false;
            }

            // Acquire the source texture: bind the existing GPU texture if native,
            // otherwise upload from CPU pixels.
            if (!TryAcquireSourceTexture(
                    flushContext,
                    source,
                    configuration.MemoryAllocator,
                    out Texture* sourceTexture,
                    out TextureView* sourceTextureView))
            {
                return false;
            }

            // The destination texture/view are guaranteed valid by WebGPUFlushContext.Create.
            Texture* destTexture = flushContext.TargetTexture;
            TextureView* destTextureView = flushContext.TargetView;

            // Create output texture sized to the destination.
            int destWidth = destination.Bounds.Width;
            int destHeight = destination.Bounds.Height;

            // Clamp compositing region to both source and destination bounds.
            int startX = Math.Max(0, -destinationOffset.X);
            int startY = Math.Max(0, -destinationOffset.Y);
            int endX = Math.Min(srcWidth, destWidth - destinationOffset.X);
            int endY = Math.Min(srcHeight, destHeight - destinationOffset.Y);

            if (endX <= startX || endY <= startY)
            {
                return true; // No overlap.
            }

            int compositeWidth = endX - startX;
            int compositeHeight = endY - startY;

            if (!TryCreateCompositionTexture(flushContext, compositeWidth, compositeHeight, out Texture* outputTexture, out TextureView* outputTextureView, out _))
            {
                return false;
            }

            // Get or create the compute pipeline.
            string pipelineKey = $"{ComposeLayerPipelineKey}/{textureFormat}";
            bool LayoutFactory(WebGPU api, Device* device, out BindGroupLayout* layout, out string? layoutError)
                => TryCreateComposeLayerBindGroupLayout(
                    api,
                    device,
                    textureFormat,
                    inputSampleType,
                    out layout,
                    out layoutError);

            if (!flushContext.DeviceState.TryGetOrCreateCompositeComputePipeline(
                    pipelineKey,
                    shaderCode,
                    ComposeLayerComputeShader.EntryPoint,
                    LayoutFactory,
                    out BindGroupLayout* bindGroupLayout,
                    out ComputePipeline* pipeline,
                    out _))
            {
                return false;
            }

            // Create and upload the config uniform.
            nuint configSize = (nuint)Unsafe.SizeOf<LayerConfigGpu>();
            if (!flushContext.DeviceState.TryGetOrCreateSharedBuffer(
                    ComposeLayerConfigBufferKey,
                    BufferUsage.Uniform | BufferUsage.CopyDst,
                    configSize,
                    out WgpuBuffer* configBuffer,
                    out _,
                    out _))
            {
                return false;
            }

            LayerConfigGpu config = new()
            {
                SourceWidth = (uint)srcWidth,
                SourceHeight = (uint)srcHeight,
                DestOffsetX = destinationOffset.X + startX,
                DestOffsetY = destinationOffset.Y + startY,
                ColorBlendMode = (uint)options.ColorBlendingMode,
                AlphaCompositionMode = (uint)options.AlphaCompositionMode,
                BlendPercentage = FloatToUInt32Bits(options.BlendPercentage),
                Padding = 0
            };

            flushContext.Api.QueueWriteBuffer(flushContext.Queue, configBuffer, 0, &config, configSize);

            // Create bind group.
            BindGroupEntry* bindGroupEntries = stackalloc BindGroupEntry[4];
            bindGroupEntries[0] = new BindGroupEntry { Binding = 0, TextureView = sourceTextureView };
            bindGroupEntries[1] = new BindGroupEntry { Binding = 1, TextureView = destTextureView };
            bindGroupEntries[2] = new BindGroupEntry { Binding = 2, TextureView = outputTextureView };
            bindGroupEntries[3] = new BindGroupEntry
            {
                Binding = 3,
                Buffer = configBuffer,
                Offset = 0,
                Size = configSize
            };

            BindGroupDescriptor bindGroupDescriptor = new()
            {
                Layout = bindGroupLayout,
                EntryCount = 4,
                Entries = bindGroupEntries
            };

            BindGroup* bindGroup = flushContext.Api.DeviceCreateBindGroup(flushContext.Device, in bindGroupDescriptor);
            if (bindGroup is null)
            {
                return false;
            }

            flushContext.TrackBindGroup(bindGroup);

            // Dispatch compute.
            uint tileCountX = DivideRoundUp(compositeWidth, CompositeTileWidth);
            uint tileCountY = DivideRoundUp(compositeHeight, CompositeTileHeight);

            ComputePassDescriptor passDescriptor = default;
            ComputePassEncoder* passEncoder = flushContext.Api.CommandEncoderBeginComputePass(flushContext.CommandEncoder, in passDescriptor);
            if (passEncoder is null)
            {
                return false;
            }

            try
            {
                flushContext.Api.ComputePassEncoderSetPipeline(passEncoder, pipeline);
                flushContext.Api.ComputePassEncoderSetBindGroup(passEncoder, 0, bindGroup, 0, null);
                flushContext.Api.ComputePassEncoderDispatchWorkgroups(passEncoder, tileCountX, tileCountY, 1);
            }
            finally
            {
                flushContext.Api.ComputePassEncoderEnd(passEncoder);
                flushContext.Api.ComputePassEncoderRelease(passEncoder);
            }

            // Copy output back to destination texture at the compositing offset.
            CopyTextureRegion(
                flushContext,
                outputTexture,
                0,
                0,
                destTexture,
                destinationOffset.X + startX,
                destinationOffset.Y + startY,
                compositeWidth,
                compositeHeight);

            return TrySubmit(flushContext);
        }
        finally
        {
            flushContext.Dispose();
        }
    }

    /// <summary>
    /// Creates the bind-group layout for the layer compositing compute shader.
    /// </summary>
    private static bool TryCreateComposeLayerBindGroupLayout(
        WebGPU api,
        Device* device,
        TextureFormat outputTextureFormat,
        TextureSampleType inputTextureSampleType,
        out BindGroupLayout* layout,
        out string? error)
    {
        BindGroupLayoutEntry* entries = stackalloc BindGroupLayoutEntry[4];

        // Binding 0: source layer texture (read).
        entries[0] = new BindGroupLayoutEntry
        {
            Binding = 0,
            Visibility = ShaderStage.Compute,
            Texture = new TextureBindingLayout
            {
                SampleType = inputTextureSampleType,
                ViewDimension = TextureViewDimension.Dimension2D,
                Multisampled = false
            }
        };

        // Binding 1: destination/backdrop texture (read).
        entries[1] = new BindGroupLayoutEntry
        {
            Binding = 1,
            Visibility = ShaderStage.Compute,
            Texture = new TextureBindingLayout
            {
                SampleType = inputTextureSampleType,
                ViewDimension = TextureViewDimension.Dimension2D,
                Multisampled = false
            }
        };

        // Binding 2: output texture (write storage).
        entries[2] = new BindGroupLayoutEntry
        {
            Binding = 2,
            Visibility = ShaderStage.Compute,
            StorageTexture = new StorageTextureBindingLayout
            {
                Access = StorageTextureAccess.WriteOnly,
                Format = outputTextureFormat,
                ViewDimension = TextureViewDimension.Dimension2D
            }
        };

        // Binding 3: uniform config buffer.
        entries[3] = new BindGroupLayoutEntry
        {
            Binding = 3,
            Visibility = ShaderStage.Compute,
            Buffer = new BufferBindingLayout
            {
                Type = BufferBindingType.Uniform,
                HasDynamicOffset = false,
                MinBindingSize = (nuint)Unsafe.SizeOf<LayerConfigGpu>()
            }
        };

        BindGroupLayoutDescriptor descriptor = new()
        {
            EntryCount = 4,
            Entries = entries
        };

        layout = api.DeviceCreateBindGroupLayout(device, in descriptor);
        if (layout is null)
        {
            error = "Failed to create compose-layer bind group layout.";
            return false;
        }

        error = null;
        return true;
    }

    /// <summary>
    /// Acquires a GPU texture and view for the source frame. If the source is already
    /// a native GPU texture it is bound directly; otherwise CPU pixels are uploaded
    /// to a temporary texture.
    /// </summary>
    private static bool TryAcquireSourceTexture<TPixel>(
        WebGPUFlushContext flushContext,
        ICanvasFrame<TPixel> source,
        MemoryAllocator memoryAllocator,
        out Texture* sourceTexture,
        out TextureView* sourceTextureView)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        if (source.TryGetNativeSurface(out NativeSurface? sourceSurface)
            && sourceSurface.TryGetCapability(out WebGPUSurfaceCapability? srcCapability))
        {
            sourceTexture = (Texture*)srcCapability.TargetTexture;
            sourceTextureView = (TextureView*)srcCapability.TargetTextureView;
            return true;
        }

        if (source.TryGetCpuRegion(out Buffer2DRegion<TPixel> sourceRegion))
        {
            if (!TryCreateCompositionTexture(flushContext, sourceRegion.Width, sourceRegion.Height, out sourceTexture, out sourceTextureView, out _))
            {
                return false;
            }

            WebGPUFlushContext.UploadTextureFromRegion(
                flushContext.Api,
                flushContext.Queue,
                sourceTexture,
                sourceRegion,
                memoryAllocator);
            return true;
        }

        sourceTexture = null;
        sourceTextureView = null;
        return false;
    }

    /// <summary>
    /// GPU uniform config matching the WGSL <c>LayerConfig</c> struct layout.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct LayerConfigGpu
    {
        /// <summary>
        /// Gets or sets the source layer width in pixels.
        /// </summary>
        public uint SourceWidth;

        /// <summary>
        /// Gets or sets the source layer height in pixels.
        /// </summary>
        public uint SourceHeight;

        /// <summary>
        /// Gets or sets the destination-space X offset where the source layer is composited.
        /// </summary>
        public int DestOffsetX;

        /// <summary>
        /// Gets or sets the destination-space Y offset where the source layer is composited.
        /// </summary>
        public int DestOffsetY;

        /// <summary>
        /// Gets or sets the packed color blend mode consumed by the compute shader.
        /// </summary>
        public uint ColorBlendMode;

        /// <summary>
        /// Gets or sets the packed alpha composition mode consumed by the compute shader.
        /// </summary>
        public uint AlphaCompositionMode;

        /// <summary>
        /// Gets or sets the blend percentage bitcast to the shader's uniform layout.
        /// </summary>
        public uint BlendPercentage;

        /// <summary>
        /// Gets or sets the explicit padding word required by the WGSL uniform layout.
        /// </summary>
        public uint Padding;
    }
}
