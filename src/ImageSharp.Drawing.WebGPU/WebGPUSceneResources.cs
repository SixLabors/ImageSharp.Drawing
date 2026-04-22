// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

#pragma warning disable SA1201 // Staged scene types are grouped by pipeline role.

using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Silk.NET.WebGPU;
using SixLabors.ImageSharp.PixelFormats;
using WgpuBuffer = Silk.NET.WebGPU.Buffer;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Uploads one encoded scene into flush-scoped GPU resources for the staged WebGPU rasterizer.
/// </summary>
internal static unsafe class WebGPUSceneResources
{
    /// <summary>
    /// Creates the flush-scoped GPU resources required by the staged scene pipeline.
    /// </summary>
    public static bool TryCreate<TPixel>(
        WebGPUFlushContext flushContext,
        WebGPUEncodedScene scene,
        WebGPUSceneConfig config,
        uint baseColor,
        [NotNullWhen(true)] ref WebGPUSceneResourceArena? arena,
        out WebGPUSceneResourceSet resources,
        out string? error)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        resources = default;

        if (!WebGPUDrawingBackend.TryGetCompositeTextureFormat<TPixel>(out WebGPUTextureFormatId expectedFormatId))
        {
            error = $"The staged WebGPU scene pipeline does not support pixel format '{typeof(TPixel).Name}'.";
            return false;
        }

        TextureFormat expectedTextureFormat = WebGPUTextureFormatMapper.ToSilk(expectedFormatId);
        if (flushContext.TextureFormat != expectedTextureFormat)
        {
            error = $"Scene resource texture format '{flushContext.TextureFormat}' does not match the required '{expectedTextureFormat}' for pixel type '{typeof(TPixel).Name}'.";
            return false;
        }

        // Textures are scene-dependent and not pooled.
        if (!TryCreateGradientTexture(flushContext, scene, out TextureView* gradientTextureView, out error))
        {
            return false;
        }

        if (!TryCreateImageAtlasTexture<TPixel>(flushContext, scene, expectedTextureFormat, out TextureView* imageAtlasTextureView, out error))
        {
            return false;
        }

        // Compute byte lengths for the two variable-size data buffers.
        nuint infoBinDataByteLength = checked(GetBindingByteLength<uint>(scene.InfoWordCount) + config.BufferSizes.BinData.ByteLength + config.BufferSizes.BinHeaders.ByteLength);
        nuint sceneByteLength = GetBindingByteLength<uint>(scene.SceneWordCount);

        // Reuse arena buffers if all capacities fit this scene.
        if (arena is not null && arena.CanReuse(flushContext, config.BufferSizes, infoBinDataByteLength, sceneByteLength))
        {
            // Upload new scene data and header into the existing arena buffers.
            using WebGPUHandle.HandleReference reuseQueueReference = flushContext.QueueHandle.AcquireReference();
            Queue* reuseQueue = (Queue*)reuseQueueReference.Handle;
            ReadOnlySpan<uint> sceneData = scene.SceneData.Span;
            fixed (uint* sceneDataPtr = sceneData)
            {
                flushContext.Api.QueueWriteBuffer(reuseQueue, arena.SceneBuffer, 0, sceneDataPtr, (nuint)(sceneData.Length * sizeof(uint)));
            }

            GpuSceneConfig header = CreateHeader(scene, config, baseColor);
            flushContext.Api.QueueWriteBuffer(reuseQueue, arena.HeaderBuffer, 0, &header, (nuint)sizeof(GpuSceneConfig));

            resources = new WebGPUSceneResourceSet(
                arena.HeaderBuffer,
                arena.SceneBuffer,
                arena.PathReducedBuffer,
                arena.PathReduced2Buffer,
                arena.PathReducedScanBuffer,
                arena.PathMonoidBuffer,
                arena.PathBboxBuffer,
                arena.DrawReducedBuffer,
                arena.DrawMonoidBuffer,
                arena.InfoBinDataBuffer,
                arena.ClipInputBuffer,
                arena.ClipElementBuffer,
                arena.ClipBicBuffer,
                arena.ClipBboxBuffer,
                arena.DrawBboxBuffer,
                arena.PathBuffer,
                arena.LineBuffer,
                gradientTextureView,
                imageAtlasTextureView);

            error = null;
            return true;
        }

        // Arena miss — create all buffers fresh and build a new arena.
        WebGPUSceneResourceArena.Dispose(arena);

        if (!TryCreateAndUploadCombinedInfoBinDataBuffer(
                flushContext,
                scene.InfoWordCount,
                checked(config.BufferSizes.BinData.ByteLength + config.BufferSizes.BinHeaders.ByteLength),
                out WgpuBuffer* infoBinDataBuffer,
                out error))
        {
            return false;
        }

        if (!TryCreateAndUploadBuffer<GpuTagMonoid>(flushContext, [], config.BufferSizes.PathReduced.Length, out WgpuBuffer* pathReducedBuffer, out error))
        {
            return false;
        }

        if (!TryCreateAndUploadBuffer<GpuTagMonoid>(flushContext, [], config.BufferSizes.PathReduced2.Length, out WgpuBuffer* pathReduced2Buffer, out error))
        {
            return false;
        }

        if (!TryCreateAndUploadBuffer<GpuTagMonoid>(flushContext, [], config.BufferSizes.PathReducedScan.Length, out WgpuBuffer* pathReducedScanBuffer, out error))
        {
            return false;
        }

        if (!TryCreateAndUploadBuffer<GpuTagMonoid>(flushContext, [], config.BufferSizes.PathMonoids.Length, out WgpuBuffer* pathMonoidBuffer, out error))
        {
            return false;
        }

        if (!TryCreateAndUploadBuffer<GpuPathBbox>(flushContext, [], config.BufferSizes.PathBboxes.Length, out WgpuBuffer* pathBboxBuffer, out error))
        {
            return false;
        }

        if (!TryCreateAndUploadBuffer<GpuSceneDrawMonoid>(flushContext, [], config.BufferSizes.DrawReduced.Length, out WgpuBuffer* drawReducedBuffer, out error))
        {
            return false;
        }

        if (!TryCreateAndUploadBuffer<GpuSceneDrawMonoid>(flushContext, [], config.BufferSizes.DrawMonoids.Length, out WgpuBuffer* drawMonoidBuffer, out error))
        {
            return false;
        }

        if (!TryCreateAndUploadBuffer<GpuClipInp>(flushContext, [], config.BufferSizes.ClipInputs.Length, out WgpuBuffer* clipInputBuffer, out error))
        {
            return false;
        }

        if (!TryCreateAndUploadBuffer<GpuClipElement>(flushContext, [], config.BufferSizes.ClipElements.Length, out WgpuBuffer* clipElementBuffer, out error))
        {
            return false;
        }

        if (!TryCreateAndUploadBuffer<GpuBic>(flushContext, [], config.BufferSizes.ClipBics.Length, out WgpuBuffer* clipBicBuffer, out error))
        {
            return false;
        }

        if (!TryCreateAndUploadBuffer<Vector4>(flushContext, [], config.BufferSizes.ClipBboxes.Length, out WgpuBuffer* clipBboxBuffer, out error))
        {
            return false;
        }

        if (!TryCreateAndUploadBuffer<GpuDrawBbox>(flushContext, [], config.BufferSizes.DrawBboxes.Length, out WgpuBuffer* drawBboxBuffer, out error))
        {
            return false;
        }

        if (!TryCreateAndUploadBuffer<GpuScenePath>(flushContext, [], config.BufferSizes.Paths.Length, out WgpuBuffer* pathBuffer, out error))
        {
            return false;
        }

        if (!TryCreateAndUploadBuffer<GpuSceneLine>(flushContext, [], config.BufferSizes.Lines.Length, out WgpuBuffer* lineBuffer, out error))
        {
            return false;
        }

        if (!TryCreateAndUploadBuffer(flushContext, scene.SceneData.Span, (uint)scene.SceneData.Length, out WgpuBuffer* sceneBuffer, out error))
        {
            return false;
        }

        GpuSceneConfig newHeader = CreateHeader(scene, config, baseColor);
        if (!TryCreateAndUploadScalarBuffer(flushContext, in newHeader, out WgpuBuffer* headerBuffer, out error))
        {
            return false;
        }

        // Build the new arena from the freshly created buffers.
        // These buffers are NOT tracked by the flush context — the arena owns them.
        arena = new WebGPUSceneResourceArena(
            flushContext.Api,
            flushContext.DeviceHandle,
            config.BufferSizes,
            infoBinDataByteLength,
            sceneByteLength,
            headerBuffer,
            sceneBuffer,
            pathReducedBuffer,
            pathReduced2Buffer,
            pathReducedScanBuffer,
            pathMonoidBuffer,
            pathBboxBuffer,
            drawReducedBuffer,
            drawMonoidBuffer,
            infoBinDataBuffer,
            clipInputBuffer,
            clipElementBuffer,
            clipBicBuffer,
            clipBboxBuffer,
            drawBboxBuffer,
            pathBuffer,
            lineBuffer);

        resources = new WebGPUSceneResourceSet(
            headerBuffer,
            sceneBuffer,
            pathReducedBuffer,
            pathReduced2Buffer,
            pathReducedScanBuffer,
            pathMonoidBuffer,
            pathBboxBuffer,
            drawReducedBuffer,
            drawMonoidBuffer,
            infoBinDataBuffer,
            clipInputBuffer,
            clipElementBuffer,
            clipBicBuffer,
            clipBboxBuffer,
            drawBboxBuffer,
            pathBuffer,
            lineBuffer,
            gradientTextureView,
            imageAtlasTextureView);

        error = null;
        return true;
    }

    /// <summary>
    /// Creates the root config block uploaded to staged-scene shaders for one render attempt.
    /// </summary>
    /// <param name="scene">The encoded scene whose global layout is being rendered.</param>
    /// <param name="config">The attempt-specific dispatch, scratch, and chunk-window configuration.</param>
    /// <param name="baseColor">The packed base color used by the fine pass.</param>
    /// <returns>The config block matching the WGSL <c>Config</c> layout.</returns>
    public static GpuSceneConfig CreateHeader(WebGPUEncodedScene scene, WebGPUSceneConfig config, uint baseColor)
    {
        GpuSceneLayout layout = new(
            scene.Layout.DrawObjectCount,
            scene.Layout.PathCount,
            scene.Layout.ClipCount,
            scene.Layout.BinDataStart,
            checked((uint)scene.TileCountX * config.ChunkWindow.TileBufferHeight * 64U),
            scene.Layout.PathTagBase,
            scene.Layout.PathDataBase,
            scene.Layout.DrawTagBase,
            scene.Layout.DrawDataBase,
            scene.Layout.TransformBase,
            scene.Layout.StyleBase);

        return new GpuSceneConfig(
            (uint)scene.TileCountX,
            (uint)scene.TileCountY,
            (uint)scene.TargetSize.Width,
            (uint)scene.TargetSize.Height,
            config.ChunkWindow.TileYStart,
            config.ChunkWindow.TileHeight,
            baseColor,
            layout,
            config.BufferSizes.Lines.Length,
            config.BumpSizes.Binning,
            config.BumpSizes.PathRows,
            config.BumpSizes.PathTiles,
            config.BumpSizes.SegCounts,
            config.BumpSizes.Segments,
            config.BumpSizes.BlendSpill,
            config.BumpSizes.Ptcl,
            scene.FineCoverageThreshold);
    }

    private static bool TryCreateImageAtlasTexture<TPixel>(
        WebGPUFlushContext flushContext,
        WebGPUEncodedScene scene,
        TextureFormat textureFormat,
        out TextureView* textureView,
        out string? error)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        if (scene.Images.Count == 0)
        {
            return TryCreateTransparentSampledTexture(flushContext, textureFormat, out _, out textureView, out error);
        }

        int atlasWidth = 1;
        int atlasHeight = 0;
        foreach (GpuImageDescriptor descriptor in scene.Images)
        {
            GetImageEntrySize(descriptor.Brush, out int width, out int height);
            atlasWidth = Math.Max(atlasWidth, width);
            atlasHeight += height;
        }

        if (!TryCreateTexture(flushContext, textureFormat, atlasWidth, atlasHeight, "image atlas", out Texture* texture, out textureView, out error))
        {
            return false;
        }

        TPixel[] rowBuffer = GC.AllocateUninitializedArray<TPixel>(atlasWidth);
        int atlasY = 0;
        foreach (GpuImageDescriptor descriptor in scene.Images)
        {
            if (!TryUploadImageEntry(
                flushContext,
                texture,
                descriptor.Brush,
                atlasY,
                rowBuffer,
                out int entryWidth,
                out int entryHeight,
                out error))
            {
                return false;
            }

            int sceneIndex = (int)scene.Layout.DrawDataBase + descriptor.DrawDataWordOffset;
            scene.SetSceneWord(sceneIndex, PackImageAtlasOffset(0, atlasY));
            scene.SetSceneWord(sceneIndex + 1, PackImageExtents(entryWidth, entryHeight));
            scene.SetSceneWord(sceneIndex + 2, PackImageSampleInfo(textureFormat, xExtendMode: 1U, yExtendMode: 1U));
            atlasY += entryHeight;
        }

        error = null;
        return true;
    }

    /// <summary>
    /// Creates and uploads the packed gradient-ramp texture used by gradient draw records.
    /// </summary>
    private static bool TryCreateGradientTexture(
        WebGPUFlushContext flushContext,
        WebGPUEncodedScene scene,
        out TextureView* textureView,
        out string? error)
    {
        if (scene.GradientRowCount == 0)
        {
            return TryCreateTransparentSampledTexture(flushContext, TextureFormat.Rgba8Unorm, out _, out textureView, out error);
        }

        TextureDescriptor textureDescriptor = new()
        {
            Usage = TextureUsage.TextureBinding | TextureUsage.CopyDst,
            Dimension = TextureDimension.Dimension2D,
            Size = new Extent3D(512, (uint)scene.GradientRowCount, 1),
            Format = TextureFormat.Rgba8Unorm,
            MipLevelCount = 1,
            SampleCount = 1
        };

        Texture* texture;
        using (WebGPUHandle.HandleReference deviceReference = flushContext.DeviceHandle.AcquireReference())
        {
            texture = flushContext.Api.DeviceCreateTexture((Device*)deviceReference.Handle, in textureDescriptor);
        }

        if (texture is null)
        {
            textureView = null;
            error = "Failed to create a gradient texture.";
            return false;
        }

        TextureViewDescriptor textureViewDescriptor = new()
        {
            Format = TextureFormat.Rgba8Unorm,
            Dimension = TextureViewDimension.Dimension2D,
            BaseMipLevel = 0,
            MipLevelCount = 1,
            BaseArrayLayer = 0,
            ArrayLayerCount = 1,
            Aspect = TextureAspect.All
        };

        textureView = flushContext.Api.TextureCreateView(texture, in textureViewDescriptor);
        if (textureView is null)
        {
            flushContext.Api.TextureRelease(texture);
            error = "Failed to create a gradient texture view.";
            return false;
        }

        TextureDataLayout layout = new()
        {
            Offset = 0,
            BytesPerRow = 512 * 4,
            RowsPerImage = (uint)scene.GradientRowCount
        };

        ImageCopyTexture destination = new()
        {
            Texture = texture,
            MipLevel = 0,
            Origin = new Origin3D(0, 0, 0),
            Aspect = TextureAspect.All
        };

        fixed (uint* pixelPtr = scene.GradientPixels.Span)
        {
            Extent3D extent = new(512, (uint)scene.GradientRowCount, 1);
            using WebGPUHandle.HandleReference queueReference = flushContext.QueueHandle.AcquireReference();
            flushContext.Api.QueueWriteTexture(
                (Queue*)queueReference.Handle,
                in destination,
                pixelPtr,
                (nuint)(scene.GradientPixels.Length * sizeof(uint)),
                in layout,
                in extent);
        }

        flushContext.TrackTexture(texture);
        flushContext.TrackTextureView(textureView);
        error = null;
        return true;
    }

    /// <summary>
    /// Gets the atlas footprint for one sampled image or pattern brush entry.
    /// </summary>
    private static void GetImageEntrySize(Brush brush, out int width, out int height)
    {
        if (brush is PatternBrush patternBrush)
        {
            width = patternBrush.Pattern.Columns;
            height = patternBrush.Pattern.Rows;
            return;
        }

        ImageBrush imageBrush = (ImageBrush)brush;
        Rectangle sourceRegion = Rectangle.Intersect(imageBrush.UntypedImage.Bounds, (Rectangle)imageBrush.SourceRegion);
        width = sourceRegion.Width;
        height = sourceRegion.Height;
    }

    private static bool TryUploadImageEntry<TPixel>(
        WebGPUFlushContext flushContext,
        Texture* texture,
        Brush brush,
        int atlasY,
        TPixel[] rowBuffer,
        out int entryWidth,
        out int entryHeight,
        out string? error)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        if (brush is PatternBrush patternBrush)
        {
            return TryUploadPatternEntry(flushContext, texture, patternBrush, atlasY, rowBuffer, out entryWidth, out entryHeight, out error);
        }

        // We can safely cast the untyped image to a typed image because the type constraint is tightly
        // controlled by the caller based on the flush context's texture format, which is determined by the pixel type.
        return TryUploadImageBrushEntry(flushContext, texture, (ImageBrush<TPixel>)brush, atlasY, out entryWidth, out entryHeight, out error);
    }

    private static bool TryUploadPatternEntry<TPixel>(
        WebGPUFlushContext flushContext,
        Texture* texture,
        PatternBrush patternBrush,
        int atlasY,
        TPixel[] rowBuffer,
        out int entryWidth,
        out int entryHeight,
        out string? error)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        DenseMatrix<Color> pattern = patternBrush.Pattern;
        entryWidth = pattern.Columns;
        entryHeight = pattern.Rows;

        for (int y = 0; y < entryHeight; y++)
        {
            Span<TPixel> rowPixels = rowBuffer.AsSpan(0, entryWidth);
            for (int x = 0; x < entryWidth; x++)
            {
                rowPixels[x] = pattern[y, x].ToPixel<TPixel>();
            }

            if (!TryWriteTextureRegion<TPixel>(flushContext, texture, 0, atlasY + y, entryWidth, 1, rowPixels, out error))
            {
                return false;
            }
        }

        error = null;
        return true;
    }

    private static bool TryUploadImageBrushEntry<TPixel>(
        WebGPUFlushContext flushContext,
        Texture* texture,
        ImageBrush<TPixel> imageBrush,
        int atlasY,
        out int entryWidth,
        out int entryHeight,
        out string? error)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        Rectangle sourceRegion = Rectangle.Intersect(imageBrush.UntypedImage.Bounds, (Rectangle)imageBrush.SourceRegion);
        entryWidth = sourceRegion.Width;
        entryHeight = sourceRegion.Height;

        ImageFrame<TPixel> sourceFrame = imageBrush.SourceImage.Frames.RootFrame;
        for (int y = 0; y < entryHeight; y++)
        {
            ReadOnlySpan<TPixel> sourceRow = sourceFrame.PixelBuffer.DangerousGetRowSpan(sourceRegion.Y + y).Slice(sourceRegion.X, entryWidth);

            if (!TryWriteTextureRegion(flushContext, texture, 0, atlasY + y, entryWidth, 1, sourceRow, out error))
            {
                return false;
            }
        }

        error = null;
        return true;
    }

    /// <summary>
    /// Creates the combined info/bin-data scratch buffer expected by the scheduling passes.
    /// </summary>
    private static bool TryCreateAndUploadCombinedInfoBinDataBuffer(
        WebGPUFlushContext flushContext,
        int infoWordCount,
        nuint dynamicBinByteLength,
        out WgpuBuffer* buffer,
        out string? error)
    {
        nuint infoByteLength = checked((nuint)infoWordCount * (nuint)Unsafe.SizeOf<uint>());
        nuint totalByteLength = checked(infoByteLength + dynamicBinByteLength);
        if (totalByteLength == 0)
        {
            totalByteLength = (nuint)Unsafe.SizeOf<uint>();
        }

        BufferDescriptor descriptor = new()
        {
            Usage = BufferUsage.Storage | BufferUsage.CopyDst,
            Size = totalByteLength
        };

        using (WebGPUHandle.HandleReference deviceReference = flushContext.DeviceHandle.AcquireReference())
        {
            buffer = flushContext.Api.DeviceCreateBuffer((Device*)deviceReference.Handle, in descriptor);
        }

        if (buffer is null)
        {
            error = "Failed to create the staged-scene info/bin-data buffer.";
            return false;
        }

        error = null;
        return true;
    }

    /// <summary>
    /// Creates a one-pixel transparent fallback texture so shader bindings stay valid when a scene omits that input.
    /// </summary>
    private static bool TryCreateTransparentSampledTexture(
        WebGPUFlushContext flushContext,
        TextureFormat textureFormat,
        out Texture* texture,
        out TextureView* textureView,
        out string? error)
    {
        TextureDescriptor textureDescriptor = new()
        {
            Usage = TextureUsage.TextureBinding | TextureUsage.CopyDst,
            Dimension = TextureDimension.Dimension2D,
            Size = new Extent3D(1, 1, 1),
            Format = textureFormat,
            MipLevelCount = 1,
            SampleCount = 1
        };

        using (WebGPUHandle.HandleReference deviceReference = flushContext.DeviceHandle.AcquireReference())
        {
            texture = flushContext.Api.DeviceCreateTexture((Device*)deviceReference.Handle, in textureDescriptor);
        }

        if (texture is null)
        {
            textureView = null;
            error = "Failed to create a sampled scene texture.";
            return false;
        }

        TextureViewDescriptor textureViewDescriptor = new()
        {
            Format = textureFormat,
            Dimension = TextureViewDimension.Dimension2D,
            BaseMipLevel = 0,
            MipLevelCount = 1,
            BaseArrayLayer = 0,
            ArrayLayerCount = 1,
            Aspect = TextureAspect.All
        };

        textureView = flushContext.Api.TextureCreateView(texture, in textureViewDescriptor);
        if (textureView is null)
        {
            flushContext.Api.TextureRelease(texture);
            texture = null;
            error = "Failed to create a sampled scene texture view.";
            return false;
        }

        uint pixel = 0;
        ImageCopyTexture destination = new()
        {
            Texture = texture,
            MipLevel = 0,
            Origin = new Origin3D(0, 0, 0),
            Aspect = TextureAspect.All
        };

        TextureDataLayout layout = new()
        {
            Offset = 0,
            BytesPerRow = 4,
            RowsPerImage = 1
        };

        Extent3D size = new(1, 1, 1);
        using (WebGPUHandle.HandleReference queueReference = flushContext.QueueHandle.AcquireReference())
        {
            flushContext.Api.QueueWriteTexture((Queue*)queueReference.Handle, in destination, &pixel, 4, in layout, in size);
        }

        flushContext.TrackTexture(texture);
        flushContext.TrackTextureView(textureView);
        error = null;
        return true;
    }

    /// <summary>
    /// Creates one sampled texture and its default 2D view.
    /// </summary>
    private static bool TryCreateTexture(
        WebGPUFlushContext flushContext,
        TextureFormat textureFormat,
        int width,
        int height,
        string textureName,
        out Texture* texture,
        out TextureView* textureView,
        out string? error)
    {
        TextureDescriptor textureDescriptor = new()
        {
            Usage = TextureUsage.TextureBinding | TextureUsage.CopyDst,
            Dimension = TextureDimension.Dimension2D,
            Size = new Extent3D((uint)width, (uint)height, 1),
            Format = textureFormat,
            MipLevelCount = 1,
            SampleCount = 1
        };

        using (WebGPUHandle.HandleReference deviceReference = flushContext.DeviceHandle.AcquireReference())
        {
            texture = flushContext.Api.DeviceCreateTexture((Device*)deviceReference.Handle, in textureDescriptor);
        }

        if (texture is null)
        {
            textureView = null;
            error = $"Failed to create a {textureName} texture.";
            return false;
        }

        TextureViewDescriptor textureViewDescriptor = new()
        {
            Format = textureFormat,
            Dimension = TextureViewDimension.Dimension2D,
            BaseMipLevel = 0,
            MipLevelCount = 1,
            BaseArrayLayer = 0,
            ArrayLayerCount = 1,
            Aspect = TextureAspect.All
        };

        textureView = flushContext.Api.TextureCreateView(texture, in textureViewDescriptor);
        if (textureView is null)
        {
            flushContext.Api.TextureRelease(texture);
            texture = null;
            error = $"Failed to create a {textureName} texture view.";
            return false;
        }

        flushContext.TrackTexture(texture);
        flushContext.TrackTextureView(textureView);
        error = null;
        return true;
    }

    private static bool TryWriteTextureRegion<TPixel>(
        WebGPUFlushContext flushContext,
        Texture* texture,
        int x,
        int y,
        int width,
        int height,
        ReadOnlySpan<TPixel> pixels,
        out string? error)
        where TPixel : unmanaged
    {
        TextureDataLayout layout = new()
        {
            Offset = 0,
            BytesPerRow = (uint)(width * Unsafe.SizeOf<TPixel>()),
            RowsPerImage = (uint)height
        };

        fixed (TPixel* pixelPtr = pixels)
        {
            ImageCopyTexture destination = new()
            {
                Texture = texture,
                MipLevel = 0,
                Origin = new Origin3D((uint)x, (uint)y, 0),
                Aspect = TextureAspect.All
            };

            Extent3D extent = new((uint)width, (uint)height, 1);
            using WebGPUHandle.HandleReference queueReference = flushContext.QueueHandle.AcquireReference();
            flushContext.Api.QueueWriteTexture(
                (Queue*)queueReference.Handle,
                in destination,
                pixelPtr,
                (nuint)(pixels.Length * Unsafe.SizeOf<TPixel>()),
                in layout,
                in extent);
        }

        error = null;
        return true;
    }

    /// <summary>
    /// Packs the atlas-space origin stored in draw data for sampled image brushes.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint PackImageAtlasOffset(int x, int y)
        => ((uint)x << 16) | (uint)y;

    /// <summary>
    /// Packs the sampled image extents stored in draw data for sampled image brushes.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint PackImageExtents(int width, int height)
        => ((uint)width << 16) | (uint)height;

    /// <summary>
    /// Packs the texture-format and extend-mode bits consumed by the image sampling shader path.
    /// </summary>
    private static uint PackImageSampleInfo(TextureFormat textureFormat, uint xExtendMode, uint yExtendMode)
    {
        const uint alpha = 0xFFU;
        const uint qualityLow = 0U;
        const uint alphaTypeStraight = 0U;
        uint format = textureFormat == TextureFormat.Bgra8Unorm ? 1U : 0U;
        return alpha
            | (yExtendMode << 8)
            | (xExtendMode << 10)
            | (qualityLow << 12)
            | (alphaTypeStraight << 14)
            | (format << 15);
    }

    private static bool TryCreateAndUploadScalarBuffer<T>(
        WebGPUFlushContext flushContext,
        in T value,
        out WgpuBuffer* buffer,
        out string? error)
        where T : unmanaged
    {
        nuint byteLength = (nuint)Unsafe.SizeOf<T>();
        BufferDescriptor descriptor = new()
        {
            Usage = BufferUsage.Storage | BufferUsage.Uniform | BufferUsage.CopyDst,
            Size = byteLength
        };

        using (WebGPUHandle.HandleReference deviceReference = flushContext.DeviceHandle.AcquireReference())
        {
            buffer = flushContext.Api.DeviceCreateBuffer((Device*)deviceReference.Handle, in descriptor);
        }

        if (buffer is null)
        {
            error = $"Failed to create a staged-scene scalar buffer for '{typeof(T).Name}'.";
            return false;
        }

        using WebGPUHandle.HandleReference queueReference = flushContext.QueueHandle.AcquireReference();
        flushContext.Api.QueueWriteBuffer(
            (Queue*)queueReference.Handle,
            buffer,
            0,
            Unsafe.AsPointer(ref Unsafe.AsRef(in value)),
            byteLength);
        error = null;
        return true;
    }

    /// <summary>
    /// Creates one flush-scoped storage/copy buffer and uploads initial contents when present.
    /// </summary>
    /// <remarks>
    /// Many staging buffers are scratch-only, so the upload branch is intentionally skipped for empty spans.
    /// The method still creates the buffer because later GPU passes depend on the binding existing for the full flush.
    /// </remarks>
    private static bool TryCreateAndUploadBuffer<T>(
        WebGPUFlushContext flushContext,
        ReadOnlySpan<T> values,
        uint minimumLength,
        out WgpuBuffer* buffer,
        out string? error)
        where T : unmanaged
    {
        uint elementCount = Math.Max(Math.Max((uint)values.Length, minimumLength), 1U);
        nuint byteLength = checked(elementCount * (nuint)Unsafe.SizeOf<T>());
        BufferDescriptor descriptor = new()
        {
            Usage = BufferUsage.Storage | BufferUsage.CopyDst,
            Size = byteLength
        };

        using (WebGPUHandle.HandleReference deviceReference = flushContext.DeviceHandle.AcquireReference())
        {
            buffer = flushContext.Api.DeviceCreateBuffer((Device*)deviceReference.Handle, in descriptor);
        }

        if (buffer is null)
        {
            error = $"Failed to create a staged-scene buffer for '{typeof(T).Name}'.";
            return false;
        }

        if (!values.IsEmpty)
        {
            nuint uploadByteLength = checked((nuint)values.Length * (nuint)Unsafe.SizeOf<T>());
            using WebGPUHandle.HandleReference queueReference = flushContext.QueueHandle.AcquireReference();
            fixed (T* dataPtr = values)
            {
                flushContext.Api.QueueWriteBuffer(
                    (Queue*)queueReference.Handle,
                    buffer,
                    0,
                    dataPtr,
                    uploadByteLength);
            }
        }

        error = null;
        return true;
    }

    /// <summary>
    /// Gets the byte length required to bind <paramref name="count"/> unmanaged elements,
    /// preserving WebGPU's non-zero binding rule.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static nuint GetBindingByteLength<T>(int count)
        where T : unmanaged
        => checked((nuint)Math.Max(count, 1) * (nuint)Unsafe.SizeOf<T>());
}

/// <summary>
/// Flush-scoped GPU resources produced from one encoded scene.
/// </summary>
/// <summary>
/// Flush-scoped GPU resources created for one encoded staged scene.
/// </summary>
internal readonly unsafe struct WebGPUSceneResourceSet
{
    /// <summary>
    /// Initializes a new instance of the <see cref="WebGPUSceneResourceSet"/> struct.
    /// </summary>
    public WebGPUSceneResourceSet(
        WgpuBuffer* headerBuffer,
        WgpuBuffer* sceneBuffer,
        WgpuBuffer* pathReducedBuffer,
        WgpuBuffer* pathReduced2Buffer,
        WgpuBuffer* pathReducedScanBuffer,
        WgpuBuffer* pathMonoidBuffer,
        WgpuBuffer* pathBboxBuffer,
        WgpuBuffer* drawReducedBuffer,
        WgpuBuffer* drawMonoidBuffer,
        WgpuBuffer* infoBinDataBuffer,
        WgpuBuffer* clipInputBuffer,
        WgpuBuffer* clipElementBuffer,
        WgpuBuffer* clipBicBuffer,
        WgpuBuffer* clipBboxBuffer,
        WgpuBuffer* drawBboxBuffer,
        WgpuBuffer* pathBuffer,
        WgpuBuffer* lineBuffer,
        TextureView* gradientTextureView,
        TextureView* imageAtlasTextureView)
    {
        this.HeaderBuffer = headerBuffer;
        this.SceneBuffer = sceneBuffer;
        this.PathReducedBuffer = pathReducedBuffer;
        this.PathReduced2Buffer = pathReduced2Buffer;
        this.PathReducedScanBuffer = pathReducedScanBuffer;
        this.PathMonoidBuffer = pathMonoidBuffer;
        this.PathBboxBuffer = pathBboxBuffer;
        this.DrawReducedBuffer = drawReducedBuffer;
        this.DrawMonoidBuffer = drawMonoidBuffer;
        this.InfoBinDataBuffer = infoBinDataBuffer;
        this.ClipInputBuffer = clipInputBuffer;
        this.ClipElementBuffer = clipElementBuffer;
        this.ClipBicBuffer = clipBicBuffer;
        this.ClipBboxBuffer = clipBboxBuffer;
        this.DrawBboxBuffer = drawBboxBuffer;
        this.PathBuffer = pathBuffer;
        this.LineBuffer = lineBuffer;
        this.GradientTextureView = gradientTextureView;
        this.ImageAtlasTextureView = imageAtlasTextureView;
    }

    /// <summary>
    /// Gets the root scene-config buffer bound at slot zero by most staged-scene shaders.
    /// </summary>
    public WgpuBuffer* HeaderBuffer { get; }

    /// <summary>
    /// Gets the packed scene-data buffer produced by the CPU encoder.
    /// </summary>
    public WgpuBuffer* SceneBuffer { get; }

    /// <summary>
    /// Gets the first pathtag-reduction scratch buffer.
    /// </summary>
    public WgpuBuffer* PathReducedBuffer { get; }

    /// <summary>
    /// Gets the second pathtag-reduction scratch buffer.
    /// </summary>
    public WgpuBuffer* PathReduced2Buffer { get; }

    /// <summary>
    /// Gets the pathtag-scan prefix scratch buffer.
    /// </summary>
    public WgpuBuffer* PathReducedScanBuffer { get; }

    /// <summary>
    /// Gets the final pathtag monoid buffer.
    /// </summary>
    public WgpuBuffer* PathMonoidBuffer { get; }

    /// <summary>
    /// Gets the per-path bounding-box buffer.
    /// </summary>
    public WgpuBuffer* PathBboxBuffer { get; }

    /// <summary>
    /// Gets the draw-reduction scratch buffer.
    /// </summary>
    public WgpuBuffer* DrawReducedBuffer { get; }

    /// <summary>
    /// Gets the final draw monoid buffer.
    /// </summary>
    public WgpuBuffer* DrawMonoidBuffer { get; }

    /// <summary>
    /// Gets the combined info/bin-data scratch buffer.
    /// </summary>
    public WgpuBuffer* InfoBinDataBuffer { get; }

    /// <summary>
    /// Gets the clip input buffer.
    /// </summary>
    public WgpuBuffer* ClipInputBuffer { get; }

    /// <summary>
    /// Gets the clip element buffer.
    /// </summary>
    public WgpuBuffer* ClipElementBuffer { get; }

    /// <summary>
    /// Gets the clip bic reduction buffer.
    /// </summary>
    public WgpuBuffer* ClipBicBuffer { get; }

    /// <summary>
    /// Gets the reduced clip bounding-box buffer.
    /// </summary>
    public WgpuBuffer* ClipBboxBuffer { get; }

    /// <summary>
    /// Gets the per-draw bounding-box buffer.
    /// </summary>
    public WgpuBuffer* DrawBboxBuffer { get; }

    /// <summary>
    /// Gets the per-path scheduling buffer.
    /// </summary>
    public WgpuBuffer* PathBuffer { get; }

    /// <summary>
    /// Gets the flattened line buffer.
    /// </summary>
    public WgpuBuffer* LineBuffer { get; }

    /// <summary>
    /// Gets the sampled gradient-ramp texture view.
    /// </summary>
    public TextureView* GradientTextureView { get; }

    /// <summary>
    /// Gets the sampled image-atlas texture view.
    /// </summary>
    public TextureView* ImageAtlasTextureView { get; }
}

/// <summary>
/// Cross-flush reusable scene resource buffers. Cached on the backend via
/// <c>Interlocked.Exchange</c> so repeated renders of the same scene create
/// zero GPU buffers after the first frame. Textures are not pooled.
/// </summary>
internal sealed unsafe class WebGPUSceneResourceArena
{
    public WebGPUSceneResourceArena(
        WebGPU api,
        WebGPUDeviceHandle device,
        WebGPUSceneBufferSizes capacitySizes,
        nuint infoBinDataByteCapacity,
        nuint sceneByteCapacity,
        WgpuBuffer* headerBuffer,
        WgpuBuffer* sceneBuffer,
        WgpuBuffer* pathReducedBuffer,
        WgpuBuffer* pathReduced2Buffer,
        WgpuBuffer* pathReducedScanBuffer,
        WgpuBuffer* pathMonoidBuffer,
        WgpuBuffer* pathBboxBuffer,
        WgpuBuffer* drawReducedBuffer,
        WgpuBuffer* drawMonoidBuffer,
        WgpuBuffer* infoBinDataBuffer,
        WgpuBuffer* clipInputBuffer,
        WgpuBuffer* clipElementBuffer,
        WgpuBuffer* clipBicBuffer,
        WgpuBuffer* clipBboxBuffer,
        WgpuBuffer* drawBboxBuffer,
        WgpuBuffer* pathBuffer,
        WgpuBuffer* lineBuffer)
    {
        this.Api = api;
        this.Device = device;
        this.CapacitySizes = capacitySizes;
        this.InfoBinDataByteCapacity = infoBinDataByteCapacity;
        this.SceneByteCapacity = sceneByteCapacity;
        this.HeaderBuffer = headerBuffer;
        this.SceneBuffer = sceneBuffer;
        this.PathReducedBuffer = pathReducedBuffer;
        this.PathReduced2Buffer = pathReduced2Buffer;
        this.PathReducedScanBuffer = pathReducedScanBuffer;
        this.PathMonoidBuffer = pathMonoidBuffer;
        this.PathBboxBuffer = pathBboxBuffer;
        this.DrawReducedBuffer = drawReducedBuffer;
        this.DrawMonoidBuffer = drawMonoidBuffer;
        this.InfoBinDataBuffer = infoBinDataBuffer;
        this.ClipInputBuffer = clipInputBuffer;
        this.ClipElementBuffer = clipElementBuffer;
        this.ClipBicBuffer = clipBicBuffer;
        this.ClipBboxBuffer = clipBboxBuffer;
        this.DrawBboxBuffer = drawBboxBuffer;
        this.PathBuffer = pathBuffer;
        this.LineBuffer = lineBuffer;
    }

    public WebGPU Api { get; }

    public WebGPUDeviceHandle Device { get; }

    public WebGPUSceneBufferSizes CapacitySizes { get; }

    public nuint InfoBinDataByteCapacity { get; }

    public nuint SceneByteCapacity { get; }

    public WgpuBuffer* HeaderBuffer { get; }

    public WgpuBuffer* SceneBuffer { get; }

    public WgpuBuffer* PathReducedBuffer { get; }

    public WgpuBuffer* PathReduced2Buffer { get; }

    public WgpuBuffer* PathReducedScanBuffer { get; }

    public WgpuBuffer* PathMonoidBuffer { get; }

    public WgpuBuffer* PathBboxBuffer { get; }

    public WgpuBuffer* DrawReducedBuffer { get; }

    public WgpuBuffer* DrawMonoidBuffer { get; }

    public WgpuBuffer* InfoBinDataBuffer { get; }

    public WgpuBuffer* ClipInputBuffer { get; }

    public WgpuBuffer* ClipElementBuffer { get; }

    public WgpuBuffer* ClipBicBuffer { get; }

    public WgpuBuffer* ClipBboxBuffer { get; }

    public WgpuBuffer* DrawBboxBuffer { get; }

    public WgpuBuffer* PathBuffer { get; }

    public WgpuBuffer* LineBuffer { get; }

    /// <summary>
    /// Returns true if every buffer fits the required sizes for this scene.
    /// </summary>
    public bool CanReuse(WebGPUFlushContext flushContext, WebGPUSceneBufferSizes bufferSizes, nuint infoBinDataByteLength, nuint sceneByteLength)
        => ReferenceEquals(this.Device, flushContext.DeviceHandle) &&
           this.HeaderBuffer is not null &&
           this.SceneBuffer is not null &&
           infoBinDataByteLength <= this.InfoBinDataByteCapacity &&
           sceneByteLength <= this.SceneByteCapacity &&
           bufferSizes.PathReduced.ByteLength <= this.CapacitySizes.PathReduced.ByteLength &&
           bufferSizes.PathReduced2.ByteLength <= this.CapacitySizes.PathReduced2.ByteLength &&
           bufferSizes.PathReducedScan.ByteLength <= this.CapacitySizes.PathReducedScan.ByteLength &&
           bufferSizes.PathMonoids.ByteLength <= this.CapacitySizes.PathMonoids.ByteLength &&
           bufferSizes.PathBboxes.ByteLength <= this.CapacitySizes.PathBboxes.ByteLength &&
           bufferSizes.DrawReduced.ByteLength <= this.CapacitySizes.DrawReduced.ByteLength &&
           bufferSizes.DrawMonoids.ByteLength <= this.CapacitySizes.DrawMonoids.ByteLength &&
           bufferSizes.ClipInputs.ByteLength <= this.CapacitySizes.ClipInputs.ByteLength &&
           bufferSizes.ClipElements.ByteLength <= this.CapacitySizes.ClipElements.ByteLength &&
           bufferSizes.ClipBics.ByteLength <= this.CapacitySizes.ClipBics.ByteLength &&
           bufferSizes.ClipBboxes.ByteLength <= this.CapacitySizes.ClipBboxes.ByteLength &&
           bufferSizes.DrawBboxes.ByteLength <= this.CapacitySizes.DrawBboxes.ByteLength &&
           bufferSizes.Paths.ByteLength <= this.CapacitySizes.Paths.ByteLength &&
           bufferSizes.Lines.ByteLength <= this.CapacitySizes.Lines.ByteLength;

    /// <summary>
    /// Releases all GPU buffers owned by this arena.
    /// </summary>
    public static void Dispose(WebGPUSceneResourceArena? arena)
    {
        if (arena is null || arena.HeaderBuffer is null)
        {
            return;
        }

        WebGPU api = arena.Api;
        api.BufferRelease(arena.HeaderBuffer);
        api.BufferRelease(arena.SceneBuffer);
        api.BufferRelease(arena.PathReducedBuffer);
        api.BufferRelease(arena.PathReduced2Buffer);
        api.BufferRelease(arena.PathReducedScanBuffer);
        api.BufferRelease(arena.PathMonoidBuffer);
        api.BufferRelease(arena.PathBboxBuffer);
        api.BufferRelease(arena.DrawReducedBuffer);
        api.BufferRelease(arena.DrawMonoidBuffer);
        api.BufferRelease(arena.InfoBinDataBuffer);
        api.BufferRelease(arena.ClipInputBuffer);
        api.BufferRelease(arena.ClipElementBuffer);
        api.BufferRelease(arena.ClipBicBuffer);
        api.BufferRelease(arena.ClipBboxBuffer);
        api.BufferRelease(arena.DrawBboxBuffer);
        api.BufferRelease(arena.PathBuffer);
        api.BufferRelease(arena.LineBuffer);
    }
}

/// <summary>
/// Flush-scoped bump allocator heads shared by the staged-scene scheduling passes.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct GpuSceneBumpAllocators
{
    public uint Failed;
    public uint Binning;
    public uint Ptcl;
    public uint PathRows;
    public uint Tile;
    public uint SegCounts;
    public uint Segments;
    public uint BlendSpill;
    public uint Lines;
}

/// <summary>
/// Prefix-scan monoid emitted from the packed path-tag stream.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal readonly struct GpuTagMonoid
{
    public GpuTagMonoid(uint transIndex, uint pathSegmentIndex, uint pathSegmentOffset, uint styleIndex, uint pathIndex)
    {
        this.TransIndex = transIndex;
        this.PathSegmentIndex = pathSegmentIndex;
        this.PathSegmentOffset = pathSegmentOffset;
        this.StyleIndex = styleIndex;
        this.PathIndex = pathIndex;
    }

    public uint TransIndex { get; }

    public uint PathSegmentIndex { get; }

    public uint PathSegmentOffset { get; }

    public uint StyleIndex { get; }

    public uint PathIndex { get; }
}

/// <summary>
/// Per-path bounding box and metadata consumed by later scheduling passes.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal readonly struct GpuPathBbox
{
    public GpuPathBbox(int x0, int y0, int x1, int y1, uint drawFlags, uint transIndex)
    {
        this.X0 = x0;
        this.Y0 = y0;
        this.X1 = x1;
        this.Y1 = y1;
        this.DrawFlags = drawFlags;
        this.TransIndex = transIndex;
    }

    public int X0 { get; }

    public int Y0 { get; }

    public int X1 { get; }

    public int Y1 { get; }

    public uint DrawFlags { get; }

    public uint TransIndex { get; }
}

/// <summary>
/// Clip input record mapping one draw object to the path that defines its clip stack entry.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal readonly struct GpuClipInp
{
    public GpuClipInp(uint drawIndex, int pathIndex)
    {
        this.DrawIndex = drawIndex;
        this.PathIndex = pathIndex;
    }

    public uint DrawIndex { get; }

    public int PathIndex { get; }
}

/// <summary>
/// Binary interval combination record used by the clip reduction passes.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal readonly struct GpuBic
{
    public GpuBic(uint a, uint b)
    {
        this.A = a;
        this.B = b;
    }

    public uint A { get; }

    public uint B { get; }
}

/// <summary>
/// Reduced clip element containing the parent link and accumulated clip bounds.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal readonly struct GpuClipElement
{
    public GpuClipElement(uint parentIndex, Vector4 bbox)
    {
        this.ParentIndex = parentIndex;
        this.Bbox = bbox;
    }

    public uint ParentIndex { get; }

    public Vector4 Bbox { get; }
}

/// <summary>
/// Bounding box emitted per draw object after draw reduction.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal readonly struct GpuDrawBbox
{
    public GpuDrawBbox(Vector4 bbox) => this.Bbox = bbox;

    public Vector4 Bbox { get; }
}

/// <summary>
/// One bin-header entry describing how many elements belong to a scheduling chunk.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal readonly struct GpuSceneBinHeader
{
    public GpuSceneBinHeader(uint elementCount, uint chunkOffset)
    {
        this.ElementCount = elementCount;
        this.ChunkOffset = chunkOffset;
    }

    public uint ElementCount { get; }

    public uint ChunkOffset { get; }
}

/// <summary>
/// Indirect-dispatch argument buffer layout used by later scheduling passes.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct GpuSceneIndirectCount
{
    public uint CountX;
    public uint CountY;
    public uint CountZ;
    public uint Pad0;
}

/// <summary>
/// Scene-buffer layout metadata consumed by every staged-scene shader.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal readonly struct GpuSceneLayout
{
    public GpuSceneLayout(
        uint drawObjectCount,
        uint pathCount,
        uint clipCount,
        uint binDataStart,
        uint ptclDynamicStart,
        uint pathTagBase,
        uint pathDataBase,
        uint drawTagBase,
        uint drawDataBase,
        uint transformBase,
        uint styleBase)
    {
        this.DrawObjectCount = drawObjectCount;
        this.PathCount = pathCount;
        this.ClipCount = clipCount;
        this.BinDataStart = binDataStart;
        this.PtclDynamicStart = ptclDynamicStart;
        this.PathTagBase = pathTagBase;
        this.PathDataBase = pathDataBase;
        this.DrawTagBase = drawTagBase;
        this.DrawDataBase = drawDataBase;
        this.TransformBase = transformBase;
        this.StyleBase = styleBase;
    }

    public uint DrawObjectCount { get; }

    public uint PathCount { get; }

    public uint ClipCount { get; }

    public uint BinDataStart { get; }

    public uint PtclDynamicStart { get; }

    public uint PathTagBase { get; }

    public uint PathDataBase { get; }

    public uint DrawTagBase { get; }

    public uint DrawDataBase { get; }

    public uint TransformBase { get; }

    public uint StyleBase { get; }
}

/// <summary>
/// Root scene configuration block bound at slot zero for most staged-scene shaders.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal readonly struct GpuSceneConfig
{
    public GpuSceneConfig(
        uint widthInTiles,
        uint heightInTiles,
        uint targetWidth,
        uint targetHeight,
        uint chunkTileYStart,
        uint chunkTileHeight,
        uint baseColor,
        GpuSceneLayout layout,
        uint linesSize,
        uint binningSize,
        uint pathRowsSize,
        uint tilesSize,
        uint segCountsSize,
        uint segmentsSize,
        uint blendSize,
        uint ptclSize,
        float fineCoverageThreshold)
    {
        this.WidthInTiles = widthInTiles;
        this.HeightInTiles = heightInTiles;
        this.TargetWidth = targetWidth;
        this.TargetHeight = targetHeight;
        this.ChunkTileYStart = chunkTileYStart;
        this.ChunkTileHeight = chunkTileHeight;
        this.BaseColor = baseColor;
        this.Layout = layout;
        this.LinesSize = linesSize;
        this.BinningSize = binningSize;
        this.PathRowsSize = pathRowsSize;
        this.TilesSize = tilesSize;
        this.SegCountsSize = segCountsSize;
        this.SegmentsSize = segmentsSize;
        this.BlendSize = blendSize;
        this.PtclSize = ptclSize;
        this.FineCoverageThreshold = fineCoverageThreshold;
    }

    public uint WidthInTiles { get; }

    public uint HeightInTiles { get; }

    public uint TargetWidth { get; }

    public uint TargetHeight { get; }

    /// <summary>
    /// Gets the first global tile row rendered by this attempt.
    /// </summary>
    public uint ChunkTileYStart { get; }

    /// <summary>
    /// Gets the number of real tile rows rendered by this attempt.
    /// </summary>
    public uint ChunkTileHeight { get; }

    public uint BaseColor { get; }

    public GpuSceneLayout Layout { get; }

    public uint LinesSize { get; }

    public uint BinningSize { get; }

    /// <summary>
    /// Gets the sparse path-row buffer capacity.
    /// </summary>
    public uint PathRowsSize { get; }

    public uint TilesSize { get; }

    public uint SegCountsSize { get; }

    public uint SegmentsSize { get; }

    public uint BlendSize { get; }

    public uint PtclSize { get; }

    /// <summary>
    /// Gets the scene-wide coverage threshold consumed by the aliased fine pass.
    /// </summary>
    public float FineCoverageThreshold { get; }
}

/// <summary>
/// Encoded draw-tag constants matching the staged-scene shader contract.
/// </summary>
internal static class GpuSceneDrawTag
{
    // These values are not a plain enum because each word also encodes the path-count, clip-count,
    // scene-word-count, and info-word-count increments consumed by the scan/reduction stages.
    public const uint Nop = 0U;
    public const uint FillColor = 0x44U;
    public const uint FillRecolor = 0x4CU;
    public const uint FillLinGradient = 0x114U;
    public const uint FillRadGradient = 0x29CU;
    public const uint FillEllipticGradient = 0x1DCU;
    public const uint FillSweepGradient = 0x254U;
    public const uint FillImage = 0x294U;
    public const uint BeginClip = 0x49U;
    public const uint EndClip = 0x21U;
    public const uint FillInfoFlagsFillRuleBit = 1U;

    /// <summary>
    /// Decodes one packed draw tag into the additive monoid scanned by later scheduling passes.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static GpuSceneDrawMonoid Map(uint tagWord)
        => new(
            tagWord != Nop ? 1U : 0U,
            tagWord & 1U,
            (tagWord >> 2) & 0x07U,
            (tagWord >> 6) & 0x0FU);
}

/// <summary>
/// Additive monoid scanned over the draw-tag stream to derive scene offsets for later stages.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal readonly struct GpuSceneDrawMonoid
{
    public GpuSceneDrawMonoid(uint pathIndex, uint clipIndex, uint sceneOffset, uint infoOffset)
    {
        this.PathIndex = pathIndex;
        this.ClipIndex = clipIndex;
        this.SceneOffset = sceneOffset;
        this.InfoOffset = infoOffset;
    }

    public uint PathIndex { get; }

    public uint ClipIndex { get; }

    public uint SceneOffset { get; }

    public uint InfoOffset { get; }

    /// <summary>
    /// Combines two draw monoids by adding each offset/count component independently.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static GpuSceneDrawMonoid Combine(in GpuSceneDrawMonoid a, in GpuSceneDrawMonoid b)
        => new(
            a.PathIndex + b.PathIndex,
            a.ClipIndex + b.ClipIndex,
            a.SceneOffset + b.SceneOffset,
            a.InfoOffset + b.InfoOffset);
}

/// <summary>
/// Per-path scheduling record used after draw and clip reduction have established final bounds.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal readonly struct GpuScenePath
{
    private readonly uint padding0;
    private readonly uint padding1;
    private readonly uint padding2;

    public GpuScenePath(uint bboxMinX, uint bboxMinY, uint bboxMaxX, uint bboxMaxY, uint rowOffset)
    {
        this.BboxMinX = bboxMinX;
        this.BboxMinY = bboxMinY;
        this.BboxMaxX = bboxMaxX;
        this.BboxMaxY = bboxMaxY;
        this.RowOffset = rowOffset;
        this.padding0 = 0;
        this.padding1 = 0;
        this.padding2 = 0;
    }

    public uint BboxMinX { get; }

    public uint BboxMinY { get; }

    public uint BboxMaxX { get; }

    public uint BboxMaxY { get; }

    /// <summary>
    /// Gets the first sparse row record owned by this path.
    /// </summary>
    public uint RowOffset { get; }
}

/// <summary>
/// Per-path sparse row record used to allocate tiles only for the x-span actually touched on one tile row.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct GpuPathRow
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GpuPathRow"/> struct.
    /// </summary>
    /// <param name="minTileX">The inclusive minimum tile X touched on this row.</param>
    /// <param name="maxTileX">The exclusive maximum tile X touched on this row.</param>
    /// <param name="backdrop">The backdrop winding carried into the first stored tile on this row.</param>
    /// <param name="tileOffset">The first tile record owned by this row.</param>
    public GpuPathRow(uint minTileX, uint maxTileX, int backdrop, uint tileOffset)
    {
        this.MinTileX = minTileX;
        this.MaxTileX = maxTileX;
        this.Backdrop = backdrop;
        this.TileOffset = tileOffset;
    }

    /// <summary>
    /// Gets or sets the inclusive minimum tile X touched on this row.
    /// </summary>
    public uint MinTileX;

    /// <summary>
    /// Gets or sets the exclusive maximum tile X touched on this row.
    /// </summary>
    public uint MaxTileX;

    /// <summary>
    /// Gets or sets the backdrop winding carried into the first stored tile on this row.
    /// </summary>
    public int Backdrop;

    /// <summary>
    /// Gets or sets the first tile record owned by this row.
    /// </summary>
    public uint TileOffset;
}

/// <summary>
/// Flattened line record emitted from the path stream for segment-counting and tiling.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal readonly struct GpuSceneLine
{
    private readonly uint padding0;

    public GpuSceneLine(uint pathIndex, Vector2 point0, Vector2 point1)
    {
        this.PathIndex = pathIndex;
        this.padding0 = 0;
        this.Point0 = point0;
        this.Point1 = point1;
    }

    public uint PathIndex { get; }

    public Vector2 Point0 { get; }

    public Vector2 Point1 { get; }
}

/// <summary>
/// Per-tile path record containing the backdrop and either a segment count or a segment-list index.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct GpuPathTile
{
    public GpuPathTile(int backdrop, uint segmentCountOrIndex)
    {
        this.Backdrop = backdrop;
        this.SegmentCountOrIndex = segmentCountOrIndex;
    }

    public int Backdrop;

    public uint SegmentCountOrIndex;
}

/// <summary>
/// Per-line segment-count record emitted by the path-count stage.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal readonly struct GpuSegmentCount
{
    public GpuSegmentCount(uint lineIndex, uint counts)
    {
        this.LineIndex = lineIndex;
        this.Counts = counts;
    }

    public uint LineIndex { get; }

    public uint Counts { get; }
}

/// <summary>
/// Final per-segment record consumed by the fine rasterization stage.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal readonly struct GpuPathSegment
{
    private readonly float padding0;

    public GpuPathSegment(Vector2 point0, Vector2 point1, float yEdge)
    {
        this.Point0 = point0;
        this.Point1 = point1;
        this.YEdge = yEdge;
        this.padding0 = 0;
    }

    public Vector2 Point0 { get; }

    public Vector2 Point1 { get; }

    public float YEdge { get; }
}

#pragma warning restore SA1201
