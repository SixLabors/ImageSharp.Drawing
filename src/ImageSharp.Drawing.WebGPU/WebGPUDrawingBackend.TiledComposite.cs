// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Buffers;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Silk.NET.WebGPU;
using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.PixelFormats;
using WgpuBuffer = Silk.NET.WebGPU.Buffer;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

internal sealed unsafe partial class WebGPUDrawingBackend
{
    private const int TiledCompositeTileSize = CompositeComputeWorkgroupSize;
    private const string TiledCompositePipelineKey = "tiled-composite";

    private bool TryCompositeBatchTiled<TPixel>(
        WebGPUFlushContext flushContext,
        TextureView* coverageView,
        IReadOnlyList<PreparedCompositionCommand> commands,
        bool blitToTarget,
        out string? error)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        error = null;
        if (commands.Count == 0)
        {
            return true;
        }

        Rectangle targetLocalBounds = new(0, 0, flushContext.TargetBounds.Width, flushContext.TargetBounds.Height);
        if (targetLocalBounds.Width <= 0 || targetLocalBounds.Height <= 0)
        {
            return true;
        }

        if (!flushContext.EnsureCommandEncoder())
        {
            error = "Failed to create WebGPU command encoder.";
            return false;
        }

        if (flushContext.TargetTexture is null || flushContext.TargetView is null || coverageView is null)
        {
            error = "WebGPU flush context does not expose required target/coverage resources.";
            return false;
        }

        WgpuBuffer* destinationPixelsBuffer = flushContext.CompositeDestinationPixelsBuffer;
        nuint destinationPixelsByteSize = flushContext.CompositeDestinationPixelsByteSize;
        if (destinationPixelsBuffer is null)
        {
            TextureView* sourceTextureView = flushContext.TargetView;
            if (!flushContext.CanSampleTargetTexture)
            {
                if (!TryCreateCompositionTexture(
                        flushContext,
                        targetLocalBounds.Width,
                        targetLocalBounds.Height,
                        out Texture* sourceTexture,
                        out sourceTextureView,
                        out error))
                {
                    return false;
                }

                CopyTextureRegion(flushContext, flushContext.TargetTexture, sourceTexture, targetLocalBounds);
            }

            if (!TryCreateDestinationPixelsBuffer(
                    flushContext,
                    targetLocalBounds.Width,
                    targetLocalBounds.Height,
                    out destinationPixelsBuffer,
                    out destinationPixelsByteSize,
                    out error) ||
                !TryInitializeDestinationPixels(
                    flushContext,
                    sourceTextureView,
                    destinationPixelsBuffer,
                    targetLocalBounds.Width,
                    targetLocalBounds.Height,
                    destinationPixelsByteSize,
                    out error))
            {
                return false;
            }

            flushContext.CompositeDestinationPixelsBuffer = destinationPixelsBuffer;
            flushContext.CompositeDestinationPixelsByteSize = destinationPixelsByteSize;
        }

        if (!this.TryRunTiledCompositeComputePass<TPixel>(
                flushContext,
                coverageView,
                destinationPixelsBuffer,
                destinationPixelsByteSize,
                commands,
                targetLocalBounds.Width,
                targetLocalBounds.Height,
                out error))
        {
            return false;
        }

        this.TestingComputePathBatchCount++;

        if (blitToTarget &&
            !TryBlitDestinationPixelsToTarget(
                flushContext,
                destinationPixelsBuffer,
                destinationPixelsByteSize,
                targetLocalBounds,
                out error))
        {
            return false;
        }

        return true;
    }

    private bool TryRunTiledCompositeComputePass<TPixel>(
        WebGPUFlushContext flushContext,
        TextureView* coverageView,
        WgpuBuffer* destinationPixelsBuffer,
        nuint destinationPixelsByteSize,
        IReadOnlyList<PreparedCompositionCommand> commands,
        int destinationWidth,
        int destinationHeight,
        out string? error)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        error = null;
        int commandCount = commands.Count;
        if (commandCount == 0)
        {
            return true;
        }

        int tilesX = (destinationWidth + TiledCompositeTileSize - 1) / TiledCompositeTileSize;
        int tilesY = (destinationHeight + TiledCompositeTileSize - 1) / TiledCompositeTileSize;
        if (tilesX <= 0 || tilesY <= 0)
        {
            return true;
        }

        int tileCount = checked(tilesX * tilesY);
        int[] rentedTileCounts = ArrayPool<int>.Shared.Rent(tileCount);
        Array.Clear(rentedTileCounts, 0, tileCount);
        Span<int> tileCommandCounts = rentedTileCounts.AsSpan(0, tileCount);

        TiledCompositeCommandData[] commandData = new TiledCompositeCommandData[commandCount];
        List<TiledCompositeBrushData> brushData = [];
        List<TiledSourceLayer<TPixel>> sourceLayers = [];
        Dictionary<Image, int> sourceImageLayers = new(ReferenceEqualityComparer.Instance);
        Dictionary<TPixel, int> solidColorLayers = [];

        try
        {
            for (int commandIndex = 0; commandIndex < commandCount; commandIndex++)
            {
                PreparedCompositionCommand command = commands[commandIndex];
                Rectangle destinationRegion = command.DestinationRegion;
                if (destinationRegion.Width <= 0 || destinationRegion.Height <= 0)
                {
                    continue;
                }

                int minTileX = Math.Clamp(destinationRegion.X / TiledCompositeTileSize, 0, tilesX - 1);
                int minTileY = Math.Clamp(destinationRegion.Y / TiledCompositeTileSize, 0, tilesY - 1);
                int maxTileX = Math.Clamp((destinationRegion.Right - 1) / TiledCompositeTileSize, 0, tilesX - 1);
                int maxTileY = Math.Clamp((destinationRegion.Bottom - 1) / TiledCompositeTileSize, 0, tilesY - 1);
                for (int tileY = minTileY; tileY <= maxTileY; tileY++)
                {
                    int rowStart = checked(tileY * tilesX);
                    for (int tileX = minTileX; tileX <= maxTileX; tileX++)
                    {
                        tileCommandCounts[rowStart + tileX]++;
                    }
                }

                int sourceLayer;
                Rectangle sourceRegion;
                int brushOriginX;
                int brushOriginY;
                if (command.Brush is ImageBrush imageBrush)
                {
                    Image<TPixel> sourceImage = (Image<TPixel>)imageBrush.SourceImage;
                    if (!sourceImageLayers.TryGetValue(sourceImage, out sourceLayer))
                    {
                        sourceLayer = sourceLayers.Count;
                        sourceImageLayers.Add(sourceImage, sourceLayer);
                        sourceLayers.Add(TiledSourceLayer<TPixel>.CreateImage(sourceImage));
                    }

                    sourceRegion = Rectangle.Intersect(sourceImage.Bounds, (Rectangle)imageBrush.SourceRegion);
                    brushOriginX = checked(command.BrushBounds.Left + imageBrush.Offset.X - flushContext.TargetBounds.X);
                    brushOriginY = checked(command.BrushBounds.Top + imageBrush.Offset.Y - flushContext.TargetBounds.Y);
                }
                else if (command.Brush is SolidBrush solidBrush)
                {
                    TPixel solidPixel = solidBrush.Color.ToPixel<TPixel>();
                    if (!solidColorLayers.TryGetValue(solidPixel, out sourceLayer))
                    {
                        sourceLayer = sourceLayers.Count;
                        solidColorLayers.Add(solidPixel, sourceLayer);
                        sourceLayers.Add(TiledSourceLayer<TPixel>.CreateSolid(solidPixel));
                    }

                    sourceRegion = new Rectangle(0, 0, 1, 1);
                    brushOriginX = 0;
                    brushOriginY = 0;
                }
                else
                {
                    error = $"Unsupported brush type for tiled composition: '{command.Brush.GetType().FullName}'.";
                    return false;
                }

                int brushDataIndex = brushData.Count;
                brushData.Add(
                    new TiledCompositeBrushData(
                        sourceRegion.X,
                        sourceRegion.Y,
                        sourceRegion.Width,
                        sourceRegion.Height,
                        brushOriginX,
                        brushOriginY,
                        sourceLayer));

                GraphicsOptions options = command.GraphicsOptions;
                commandData[commandIndex] = new TiledCompositeCommandData(
                    command.SourceOffset.X,
                    command.SourceOffset.Y,
                    destinationRegion.X,
                    destinationRegion.Y,
                    destinationRegion.Width,
                    destinationRegion.Height,
                    options.BlendPercentage,
                    (int)options.ColorBlendingMode,
                    (int)options.AlphaCompositionMode,
                    brushDataIndex);
            }

            TiledCompositeTileRange[] tileRanges = new TiledCompositeTileRange[tileCount];
            int totalTileCommandRefs = 0;
            for (int tileIndex = 0; tileIndex < tileCount; tileIndex++)
            {
                int count = tileCommandCounts[tileIndex];
                tileRanges[tileIndex] = new TiledCompositeTileRange((uint)totalTileCommandRefs, (uint)count);
                tileCommandCounts[tileIndex] = totalTileCommandRefs;
                totalTileCommandRefs = checked(totalTileCommandRefs + count);
            }

            uint[] tileCommandIndices = new uint[Math.Max(totalTileCommandRefs, 1)];
            for (int commandIndex = 0; commandIndex < commandCount; commandIndex++)
            {
                Rectangle destinationRegion = commands[commandIndex].DestinationRegion;
                if (destinationRegion.Width <= 0 || destinationRegion.Height <= 0)
                {
                    continue;
                }

                int minTileX = Math.Clamp(destinationRegion.X / TiledCompositeTileSize, 0, tilesX - 1);
                int minTileY = Math.Clamp(destinationRegion.Y / TiledCompositeTileSize, 0, tilesY - 1);
                int maxTileX = Math.Clamp((destinationRegion.Right - 1) / TiledCompositeTileSize, 0, tilesX - 1);
                int maxTileY = Math.Clamp((destinationRegion.Bottom - 1) / TiledCompositeTileSize, 0, tilesY - 1);
                for (int tileY = minTileY; tileY <= maxTileY; tileY++)
                {
                    int rowStart = checked(tileY * tilesX);
                    for (int tileX = minTileX; tileX <= maxTileX; tileX++)
                    {
                        int tileIndex = rowStart + tileX;
                        int writeIndex = tileCommandCounts[tileIndex]++;
                        tileCommandIndices[writeIndex] = (uint)commandIndex;
                    }
                }
            }

            if (!TryCreateSourceLayerTextureArray(flushContext, sourceLayers, out TextureView* sourceLayerView, out error) ||
                !TryCreateAndUploadBuffer<TiledCompositeCommandData>(
                    flushContext,
                    BufferUsage.Storage,
                    commandData.AsSpan(),
                    out WgpuBuffer* commandBuffer,
                    out nuint commandBufferBytes,
                    out error) ||
                !TryCreateAndUploadBuffer<TiledCompositeTileRange>(
                    flushContext,
                    BufferUsage.Storage,
                    tileRanges.AsSpan(),
                    out WgpuBuffer* tileRangeBuffer,
                    out nuint tileRangeBufferBytes,
                    out error) ||
                !TryCreateAndUploadBuffer<uint>(
                    flushContext,
                    BufferUsage.Storage,
                    tileCommandIndices.AsSpan(),
                    out WgpuBuffer* tileCommandIndexBuffer,
                    out nuint tileCommandIndexBufferBytes,
                    out error) ||
                !TryCreateAndUploadBuffer<TiledCompositeBrushData>(
                    flushContext,
                    BufferUsage.Storage,
                    CollectionsMarshal.AsSpan(brushData),
                    out WgpuBuffer* brushDataBuffer,
                    out nuint brushDataBufferBytes,
                    out error))
            {
                return false;
            }

            TiledCompositeParameters parameters = new(destinationWidth, destinationHeight, tilesX, TiledCompositeTileSize);
            if (!TryCreateAndUploadBuffer<TiledCompositeParameters>(
                    flushContext,
                    BufferUsage.Uniform,
                    MemoryMarshal.CreateReadOnlySpan(ref parameters, 1),
                    out WgpuBuffer* parameterBuffer,
                    out nuint parameterBufferBytes,
                    out error))
            {
                return false;
            }

            if (!flushContext.DeviceState.TryGetOrCreateCompositeComputePipeline(
                    TiledCompositePipelineKey,
                    TiledCompositeComputeShader.Code,
                    TryCreateTiledCompositeBindGroupLayout,
                    out BindGroupLayout* bindGroupLayout,
                    out ComputePipeline* pipeline,
                    out error))
            {
                return false;
            }

            BindGroupEntry* entries = stackalloc BindGroupEntry[8];
            entries[0] = new BindGroupEntry
            {
                Binding = 0,
                TextureView = coverageView
            };
            entries[1] = new BindGroupEntry
            {
                Binding = 1,
                Buffer = commandBuffer,
                Offset = 0,
                Size = commandBufferBytes
            };
            entries[2] = new BindGroupEntry
            {
                Binding = 2,
                Buffer = tileRangeBuffer,
                Offset = 0,
                Size = tileRangeBufferBytes
            };
            entries[3] = new BindGroupEntry
            {
                Binding = 3,
                Buffer = tileCommandIndexBuffer,
                Offset = 0,
                Size = tileCommandIndexBufferBytes
            };
            entries[4] = new BindGroupEntry
            {
                Binding = 4,
                Buffer = brushDataBuffer,
                Offset = 0,
                Size = brushDataBufferBytes
            };
            entries[5] = new BindGroupEntry
            {
                Binding = 5,
                TextureView = sourceLayerView
            };
            entries[6] = new BindGroupEntry
            {
                Binding = 6,
                Buffer = destinationPixelsBuffer,
                Offset = 0,
                Size = destinationPixelsByteSize
            };
            entries[7] = new BindGroupEntry
            {
                Binding = 7,
                Buffer = parameterBuffer,
                Offset = 0,
                Size = parameterBufferBytes
            };

            BindGroupDescriptor bindGroupDescriptor = new()
            {
                Layout = bindGroupLayout,
                EntryCount = 8,
                Entries = entries
            };

            BindGroup* bindGroup = flushContext.Api.DeviceCreateBindGroup(flushContext.Device, in bindGroupDescriptor);
            if (bindGroup is null)
            {
                error = "Failed to create tiled composite bind group.";
                return false;
            }

            flushContext.TrackBindGroup(bindGroup);

            ComputePassDescriptor passDescriptor = default;
            ComputePassEncoder* passEncoder = flushContext.Api.CommandEncoderBeginComputePass(flushContext.CommandEncoder, in passDescriptor);
            if (passEncoder is null)
            {
                error = "Failed to begin tiled composite compute pass.";
                return false;
            }

            try
            {
                flushContext.Api.ComputePassEncoderSetPipeline(passEncoder, pipeline);
                flushContext.Api.ComputePassEncoderSetBindGroup(passEncoder, 0, bindGroup, 0, null);
                flushContext.Api.ComputePassEncoderDispatchWorkgroups(passEncoder, (uint)tilesX, (uint)tilesY, 1);
            }
            finally
            {
                flushContext.Api.ComputePassEncoderEnd(passEncoder);
                flushContext.Api.ComputePassEncoderRelease(passEncoder);
            }

            return true;
        }
        finally
        {
            ArrayPool<int>.Shared.Return(rentedTileCounts);
        }
    }

    private static bool TryCreateSourceLayerTextureArray<TPixel>(
        WebGPUFlushContext flushContext,
        List<TiledSourceLayer<TPixel>> sourceLayers,
        out TextureView* sourceLayerView,
        out string? error)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        int layerCount = Math.Max(1, sourceLayers.Count);
        int maxWidth = 1;
        int maxHeight = 1;
        for (int i = 0; i < sourceLayers.Count; i++)
        {
            TiledSourceLayer<TPixel> layer = sourceLayers[i];
            if (layer.Image is null)
            {
                continue;
            }

            if (layer.Image.Width > maxWidth)
            {
                maxWidth = layer.Image.Width;
            }

            if (layer.Image.Height > maxHeight)
            {
                maxHeight = layer.Image.Height;
            }
        }

        TextureDescriptor descriptor = new()
        {
            Usage = TextureUsage.TextureBinding | TextureUsage.CopyDst,
            Dimension = TextureDimension.Dimension2D,
            Size = new Extent3D((uint)maxWidth, (uint)maxHeight, (uint)layerCount),
            Format = flushContext.TextureFormat,
            MipLevelCount = 1,
            SampleCount = 1
        };

        Texture* texture = flushContext.Api.DeviceCreateTexture(flushContext.Device, in descriptor);
        if (texture is null)
        {
            sourceLayerView = null;
            error = "Failed to create source-layer texture array.";
            return false;
        }

        TextureViewDescriptor viewDescriptor = new()
        {
            Format = flushContext.TextureFormat,
            Dimension = TextureViewDimension.Dimension2DArray,
            BaseMipLevel = 0,
            MipLevelCount = 1,
            BaseArrayLayer = 0,
            ArrayLayerCount = (uint)layerCount,
            Aspect = TextureAspect.All
        };

        sourceLayerView = flushContext.Api.TextureCreateView(texture, in viewDescriptor);
        if (sourceLayerView is null)
        {
            flushContext.Api.TextureRelease(texture);
            error = "Failed to create source-layer texture array view.";
            return false;
        }

        try
        {
            if (sourceLayers.Count == 0)
            {
                UploadSolidSourceLayer(flushContext, texture, default(TPixel), 0);
            }
            else
            {
                for (int i = 0; i < sourceLayers.Count; i++)
                {
                    TiledSourceLayer<TPixel> layer = sourceLayers[i];
                    if (layer.Image is not null)
                    {
                        Buffer2DRegion<TPixel> sourceRegion = new(layer.Image.Frames.RootFrame.PixelBuffer, layer.Image.Bounds);
                        WebGPUFlushContext.UploadTextureFromRegion(
                            flushContext.Api,
                            flushContext.Queue,
                            texture,
                            sourceRegion,
                            0,
                            0,
                            (uint)i);
                    }
                    else
                    {
                        UploadSolidSourceLayer(flushContext, texture, layer.SolidPixel, (uint)i);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            flushContext.Api.TextureViewRelease(sourceLayerView);
            flushContext.Api.TextureRelease(texture);
            sourceLayerView = null;
            error = $"Failed to upload source layers for tiled composition. {ex.Message}";
            return false;
        }

        flushContext.TrackTexture(texture);
        flushContext.TrackTextureView(sourceLayerView);
        error = null;
        return true;
    }

    private static void UploadSolidSourceLayer<TPixel>(
        WebGPUFlushContext flushContext,
        Texture* texture,
        TPixel pixel,
        uint layer)
        where TPixel : unmanaged
    {
        ImageCopyTexture destination = new()
        {
            Texture = texture,
            MipLevel = 0,
            Origin = new Origin3D(0, 0, layer),
            Aspect = TextureAspect.All
        };

        TextureDataLayout layout = new()
        {
            Offset = 0,
            BytesPerRow = (uint)Unsafe.SizeOf<TPixel>(),
            RowsPerImage = 1
        };

        Extent3D size = new(1, 1, 1);
        TPixel copy = pixel;
        flushContext.Api.QueueWriteTexture(
            flushContext.Queue,
            in destination,
            &copy,
            (nuint)Unsafe.SizeOf<TPixel>(),
            in layout,
            in size);
    }

    private static bool TryCreateAndUploadBuffer<T>(
        WebGPUFlushContext flushContext,
        BufferUsage usage,
        ReadOnlySpan<T> sourceData,
        out WgpuBuffer* buffer,
        out nuint bufferSize,
        out string? error)
        where T : unmanaged
    {
        nuint elementSize = (nuint)Unsafe.SizeOf<T>();
        nuint writeSize = checked((nuint)sourceData.Length * elementSize);
        bufferSize = Math.Max(writeSize, Math.Max(elementSize, (nuint)16));

        BufferDescriptor descriptor = new()
        {
            Usage = usage | BufferUsage.CopyDst,
            Size = bufferSize
        };

        buffer = flushContext.Api.DeviceCreateBuffer(flushContext.Device, in descriptor);
        if (buffer is null)
        {
            error = "Failed to create tiled composite buffer.";
            return false;
        }

        flushContext.TrackBuffer(buffer);
        if (!sourceData.IsEmpty)
        {
            fixed (T* sourcePtr = sourceData)
            {
                flushContext.Api.QueueWriteBuffer(flushContext.Queue, buffer, 0, sourcePtr, writeSize);
            }
        }

        error = null;
        return true;
    }

    private static bool TryCreateTiledCompositeBindGroupLayout(
        WebGPU api,
        Device* device,
        out BindGroupLayout* layout,
        out string? error)
    {
        BindGroupLayoutEntry* entries = stackalloc BindGroupLayoutEntry[8];
        entries[0] = new BindGroupLayoutEntry
        {
            Binding = 0,
            Visibility = ShaderStage.Compute,
            Texture = new TextureBindingLayout
            {
                SampleType = TextureSampleType.Float,
                ViewDimension = TextureViewDimension.Dimension2D,
                Multisampled = false
            }
        };
        entries[1] = new BindGroupLayoutEntry
        {
            Binding = 1,
            Visibility = ShaderStage.Compute,
            Buffer = new BufferBindingLayout
            {
                Type = BufferBindingType.ReadOnlyStorage,
                HasDynamicOffset = false,
                MinBindingSize = 0
            }
        };
        entries[2] = new BindGroupLayoutEntry
        {
            Binding = 2,
            Visibility = ShaderStage.Compute,
            Buffer = new BufferBindingLayout
            {
                Type = BufferBindingType.ReadOnlyStorage,
                HasDynamicOffset = false,
                MinBindingSize = 0
            }
        };
        entries[3] = new BindGroupLayoutEntry
        {
            Binding = 3,
            Visibility = ShaderStage.Compute,
            Buffer = new BufferBindingLayout
            {
                Type = BufferBindingType.ReadOnlyStorage,
                HasDynamicOffset = false,
                MinBindingSize = 0
            }
        };
        entries[4] = new BindGroupLayoutEntry
        {
            Binding = 4,
            Visibility = ShaderStage.Compute,
            Buffer = new BufferBindingLayout
            {
                Type = BufferBindingType.ReadOnlyStorage,
                HasDynamicOffset = false,
                MinBindingSize = 0
            }
        };
        entries[5] = new BindGroupLayoutEntry
        {
            Binding = 5,
            Visibility = ShaderStage.Compute,
            Texture = new TextureBindingLayout
            {
                SampleType = TextureSampleType.Float,
                ViewDimension = TextureViewDimension.Dimension2DArray,
                Multisampled = false
            }
        };
        entries[6] = new BindGroupLayoutEntry
        {
            Binding = 6,
            Visibility = ShaderStage.Compute,
            Buffer = new BufferBindingLayout
            {
                Type = BufferBindingType.Storage,
                HasDynamicOffset = false,
                MinBindingSize = 0
            }
        };
        entries[7] = new BindGroupLayoutEntry
        {
            Binding = 7,
            Visibility = ShaderStage.Compute,
            Buffer = new BufferBindingLayout
            {
                Type = BufferBindingType.Uniform,
                HasDynamicOffset = false,
                MinBindingSize = (nuint)Unsafe.SizeOf<TiledCompositeParameters>()
            }
        };

        BindGroupLayoutDescriptor descriptor = new()
        {
            EntryCount = 8,
            Entries = entries
        };

        layout = api.DeviceCreateBindGroupLayout(device, in descriptor);
        if (layout is null)
        {
            error = "Failed to create tiled composite bind-group layout.";
            return false;
        }

        error = null;
        return true;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct TiledCompositeCommandData(
        int sourceOffsetX,
        int sourceOffsetY,
        int destinationX,
        int destinationY,
        int destinationWidth,
        int destinationHeight,
        float blendPercentage,
        int colorBlendingMode,
        int alphaCompositionMode,
        int brushDataIndex)
    {
        public readonly int SourceOffsetX = sourceOffsetX;
        public readonly int SourceOffsetY = sourceOffsetY;
        public readonly int DestinationX = destinationX;
        public readonly int DestinationY = destinationY;
        public readonly int DestinationWidth = destinationWidth;
        public readonly int DestinationHeight = destinationHeight;
        public readonly float BlendPercentage = blendPercentage;
        public readonly int ColorBlendingMode = colorBlendingMode;
        public readonly int AlphaCompositionMode = alphaCompositionMode;
        public readonly int BrushDataIndex = brushDataIndex;
        public readonly int Padding0 = 0;
        public readonly int Padding1 = 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct TiledCompositeTileRange(uint startIndex, uint count)
    {
        public readonly uint StartIndex = startIndex;
        public readonly uint Count = count;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct TiledCompositeBrushData(
        int sourceRegionX,
        int sourceRegionY,
        int sourceRegionWidth,
        int sourceRegionHeight,
        int brushOriginX,
        int brushOriginY,
        int sourceLayer)
    {
        public readonly int SourceRegionX = sourceRegionX;
        public readonly int SourceRegionY = sourceRegionY;
        public readonly int SourceRegionWidth = sourceRegionWidth;
        public readonly int SourceRegionHeight = sourceRegionHeight;
        public readonly int BrushOriginX = brushOriginX;
        public readonly int BrushOriginY = brushOriginY;
        public readonly int SourceLayer = sourceLayer;
        public readonly int Padding0 = 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct TiledCompositeParameters(
        int destinationWidth,
        int destinationHeight,
        int tilesX,
        int tileSize)
    {
        public readonly int DestinationWidth = destinationWidth;
        public readonly int DestinationHeight = destinationHeight;
        public readonly int TilesX = tilesX;
        public readonly int TileSize = tileSize;
    }

    private readonly struct TiledSourceLayer<TPixel>
        where TPixel : unmanaged, IPixel<TPixel>
    {
        public TiledSourceLayer(Image<TPixel> image)
        {
            this.Image = image;
            this.SolidPixel = default;
        }

        public TiledSourceLayer(TPixel solidPixel)
        {
            this.Image = null;
            this.SolidPixel = solidPixel;
        }

        public Image<TPixel>? Image { get; }

        public TPixel SolidPixel { get; }

        public static TiledSourceLayer<TPixel> CreateImage(Image<TPixel> image) => new(image);

        public static TiledSourceLayer<TPixel> CreateSolid(TPixel solidPixel) => new(solidPixel);
    }
}
