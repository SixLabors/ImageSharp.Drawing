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
    /// <param name="flushContext">The active WebGPU flush context.</param>
    /// <param name="scene">The encoded scene to stage.</param>
    /// <param name="config">The scene configuration.</param>
    /// <param name="baseColor">The packed base color for the target.</param>
    /// <param name="arena">The reusable resource arena for this staging operation.</param>
    /// <param name="resources">The staged resource set.</param>
    /// <param name="error">The error message when resource creation fails.</param>
    /// <returns><see langword="true"/> when the resources were created.</returns>
    public static bool TryCreate<TPixel>(
        WebGPUFlushContext flushContext,
        WebGPUEncodedScene scene,
        WebGPUSceneConfig config,
        uint baseColor,
        [NotNullWhen(true)] ref WebGPUSceneResourceArena? arena,
        out WebGPUSceneResourceSet resources,
        out string? error)
        where TPixel : unmanaged, IPixel<TPixel>
        => TryCreate<TPixel>(
            flushContext,
            scene,
            config,
            baseColor,
            externalTextureView: null,
            ref arena,
            out resources,
            out error);

    /// <summary>
    /// Creates the flush-scoped GPU resources required by the staged scene pipeline for one range.
    /// </summary>
    /// <param name="flushContext">The active WebGPU flush context.</param>
    /// <param name="scene">The encoded scene to stage.</param>
    /// <param name="range">The scene range to stage.</param>
    /// <param name="config">The scene configuration.</param>
    /// <param name="baseColor">The packed base color for the target.</param>
    /// <param name="externalTextureView">The target texture view supplied by the caller.</param>
    /// <param name="arena">The reusable resource arena for this staging operation.</param>
    /// <param name="resources">The staged resource set.</param>
    /// <param name="error">The error message when resource creation fails.</param>
    /// <returns><see langword="true"/> when the resources were created.</returns>
    public static bool TryCreate<TPixel>(
        WebGPUFlushContext flushContext,
        WebGPUEncodedScene scene,
        WebGPUSceneRange range,
        WebGPUSceneConfig config,
        uint baseColor,
        TextureView* externalTextureView,
        [NotNullWhen(true)] ref WebGPUSceneResourceArena? arena,
        out WebGPUSceneResourceSet resources,
        out string? error)
        where TPixel : unmanaged, IPixel<TPixel>
        => TryCreateCore<TPixel>(
            flushContext,
            scene,
            range,
            config,
            baseColor,
            externalTextureView,
            ref arena,
            out resources,
            out error);

    /// <summary>
    /// Creates the flush-scoped GPU resources required by the staged scene pipeline.
    /// </summary>
    /// <param name="flushContext">The active WebGPU flush context.</param>
    /// <param name="scene">The encoded scene to stage.</param>
    /// <param name="config">The scene configuration.</param>
    /// <param name="baseColor">The packed base color for the target.</param>
    /// <param name="externalTextureView">The target texture view supplied by the caller.</param>
    /// <param name="arena">The reusable resource arena for this staging operation.</param>
    /// <param name="resources">The staged resource set.</param>
    /// <param name="error">The error message when resource creation fails.</param>
    /// <returns><see langword="true"/> when the resources were created.</returns>
    public static bool TryCreate<TPixel>(
        WebGPUFlushContext flushContext,
        WebGPUEncodedScene scene,
        WebGPUSceneConfig config,
        uint baseColor,
        TextureView* externalTextureView,
        [NotNullWhen(true)] ref WebGPUSceneResourceArena? arena,
        out WebGPUSceneResourceSet resources,
        out string? error)
        where TPixel : unmanaged, IPixel<TPixel>
        => TryCreateCore<TPixel>(
            flushContext,
            scene,
            null,
            config,
            baseColor,
            externalTextureView,
            ref arena,
            out resources,
            out error);

    private static bool TryCreateCore<TPixel>(
        WebGPUFlushContext flushContext,
        WebGPUEncodedScene scene,
        WebGPUSceneRange? range,
        WebGPUSceneConfig config,
        uint baseColor,
        TextureView* externalTextureView,
        [NotNullWhen(true)] ref WebGPUSceneResourceArena? arena,
        out WebGPUSceneResourceSet resources,
        out string? error)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        resources = default;
        int infoWordCount = range?.InfoWordCount ?? scene.InfoWordCount;

        // Textures are scene-dependent and not pooled.
        if (!TryCreateGradientTexture(flushContext, scene, out TextureView* gradientTextureView, out error))
        {
            return false;
        }

        if (!TryCreateImageAtlasTexture<TPixel>(flushContext, scene, range, flushContext.TextureFormat, externalTextureView, out TextureView* imageAtlasTextureView, out error))
        {
            return false;
        }

        // Compute byte lengths for the two variable-size data buffers.
        nuint infoBinDataByteLength = checked(GetBindingByteLength<uint>(infoWordCount + scene.PathGradientDataWordCount) + config.BufferSizes.BinData.ByteLength + config.BufferSizes.BinHeaders.ByteLength);
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

            GpuSceneConfig header = range.HasValue
                ? CreateHeader(scene, range.Value, config, baseColor)
                : CreateHeader(scene, config, baseColor);
            flushContext.Api.QueueWriteBuffer(reuseQueue, arena.HeaderBuffer, 0, &header, (nuint)sizeof(GpuSceneConfig));
            UploadPathGradientData(flushContext, arena.InfoBinDataBuffer, infoWordCount, scene.PathGradientData.Span);

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

        // Arena miss; create all buffers fresh and build a new arena.
        WebGPUSceneResourceArena.Dispose(arena);
        arena = null;

        WgpuBuffer* infoBinDataBuffer = CreateAndUploadCombinedInfoBinDataBuffer(
            flushContext,
            infoWordCount,
            scene.PathGradientData.Span,
            checked(config.BufferSizes.BinData.ByteLength + config.BufferSizes.BinHeaders.ByteLength));

        WgpuBuffer* pathReducedBuffer = CreateAndUploadBuffer<GpuTagMonoid>(flushContext, [], config.BufferSizes.PathReduced.Length);
        WgpuBuffer* pathReduced2Buffer = CreateAndUploadBuffer<GpuTagMonoid>(flushContext, [], config.BufferSizes.PathReduced2.Length);
        WgpuBuffer* pathReducedScanBuffer = CreateAndUploadBuffer<GpuTagMonoid>(flushContext, [], config.BufferSizes.PathReducedScan.Length);
        WgpuBuffer* pathMonoidBuffer = CreateAndUploadBuffer<GpuTagMonoid>(flushContext, [], config.BufferSizes.PathMonoids.Length);
        WgpuBuffer* pathBboxBuffer = CreateAndUploadBuffer<GpuPathBbox>(flushContext, [], config.BufferSizes.PathBboxes.Length);
        WgpuBuffer* drawReducedBuffer = CreateAndUploadBuffer<GpuSceneDrawMonoid>(flushContext, [], config.BufferSizes.DrawReduced.Length);
        WgpuBuffer* drawMonoidBuffer = CreateAndUploadBuffer<GpuSceneDrawMonoid>(flushContext, [], config.BufferSizes.DrawMonoids.Length);
        WgpuBuffer* clipInputBuffer = CreateAndUploadBuffer<GpuClipInp>(flushContext, [], config.BufferSizes.ClipInputs.Length);
        WgpuBuffer* clipElementBuffer = CreateAndUploadBuffer<GpuClipElement>(flushContext, [], config.BufferSizes.ClipElements.Length);
        WgpuBuffer* clipBicBuffer = CreateAndUploadBuffer<GpuBic>(flushContext, [], config.BufferSizes.ClipBics.Length);
        WgpuBuffer* clipBboxBuffer = CreateAndUploadBuffer<Vector4>(flushContext, [], config.BufferSizes.ClipBboxes.Length);
        WgpuBuffer* drawBboxBuffer = CreateAndUploadBuffer<GpuDrawBbox>(flushContext, [], config.BufferSizes.DrawBboxes.Length);
        WgpuBuffer* pathBuffer = CreateAndUploadBuffer<GpuScenePath>(flushContext, [], config.BufferSizes.Paths.Length);
        WgpuBuffer* lineBuffer = CreateAndUploadBuffer<GpuSceneLine>(flushContext, [], config.BufferSizes.Lines.Length);
        WgpuBuffer* sceneBuffer = CreateAndUploadBuffer(flushContext, scene.SceneData.Span, (uint)scene.SceneData.Length);

        GpuSceneConfig newHeader = range.HasValue
            ? CreateHeader(scene, range.Value, config, baseColor)
            : CreateHeader(scene, config, baseColor);
        WgpuBuffer* headerBuffer = CreateAndUploadScalarBuffer(flushContext, in newHeader);

        // Build the new arena from the freshly created buffers.
        // These buffers are NOT tracked by the flush context; the arena owns them.
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
            scene.Layout.PathGradientDataBase,
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

    /// <summary>
    /// Creates the root config block uploaded to staged-scene shaders for one render range.
    /// </summary>
    /// <param name="scene">The encoded scene whose global scene buffer is being rendered.</param>
    /// <param name="range">The range inside <paramref name="scene"/> to render.</param>
    /// <param name="config">The attempt-specific dispatch, scratch, and chunk-window configuration.</param>
    /// <param name="baseColor">The packed base color used by the fine pass.</param>
    /// <returns>The config block matching the WGSL <c>Config</c> layout.</returns>
    public static GpuSceneConfig CreateHeader(WebGPUEncodedScene scene, WebGPUSceneRange range, WebGPUSceneConfig config, uint baseColor)
    {
        int tileCountX = (range.TargetBounds.Width + 15) / 16;
        int tileCountY = (range.TargetBounds.Height + 15) / 16;
        GpuSceneLayout layout = new(
            checked((uint)range.DrawTagCount),
            checked((uint)range.PathCount),
            checked((uint)range.ClipCount),
            checked((uint)(range.InfoWordCount + scene.PathGradientDataWordCount)),
            checked((uint)range.InfoWordCount),
            checked((uint)tileCountX * config.ChunkWindow.TileBufferHeight * 64U),
            checked(scene.Layout.PathTagBase + (uint)range.PathTagWordStart),
            checked(scene.Layout.PathDataBase + (uint)range.PathDataWordStart),
            checked(scene.Layout.DrawTagBase + (uint)range.DrawTagStart),
            checked(scene.Layout.DrawDataBase + (uint)range.DrawDataWordStart),
            checked(scene.Layout.TransformBase + (uint)range.TransformWordStart),
            checked(scene.Layout.StyleBase + (uint)range.StyleWordStart));

        return new GpuSceneConfig(
            checked((uint)tileCountX),
            checked((uint)tileCountY),
            checked((uint)range.TargetBounds.Width),
            checked((uint)range.TargetBounds.Height),
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
        WebGPUSceneRange? range,
        TextureFormat textureFormat,
        TextureView* externalTextureView,
        out TextureView* textureView,
        out string? error)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        if (externalTextureView is not null)
        {
            foreach (GpuImageDescriptor descriptor in scene.Images)
            {
                if (IsImageDescriptorInRange(descriptor, range) &&
                    descriptor.Source.IsExternalTexture)
                {
                    int sceneIndex = (int)scene.Layout.DrawDataBase + descriptor.DrawDataWordOffset;
                    scene.SetSceneWord(sceneIndex, PackImageAtlasOffset(0, 0));
                    scene.SetSceneWord(sceneIndex + 1, PackImageExtents(descriptor.Source.Size.Width, descriptor.Source.Size.Height));
                    scene.SetSceneWord(sceneIndex + 2, PackImageSampleInfo(MapImageWrapMode(descriptor.Source.WrapX), MapImageWrapMode(descriptor.Source.WrapY)));
                }
            }

            textureView = externalTextureView;
            error = null;
            return true;
        }

        int imageCount = 0;
        foreach (GpuImageDescriptor descriptor in scene.Images)
        {
            if (IsImageDescriptorInRange(descriptor, range) &&
                descriptor.Source.Brush is ImageBrush or PatternBrush)
            {
                imageCount++;
            }
        }

        if (imageCount == 0)
        {
            return TryCreateTransparentSampledTexture(flushContext, textureFormat, out _, out textureView, out error);
        }

        int atlasWidth = 1;
        int atlasHeight = 0;
        foreach (GpuImageDescriptor descriptor in scene.Images)
        {
            if (!IsImageDescriptorInRange(descriptor, range) ||
                descriptor.Source.Brush is not (ImageBrush or PatternBrush))
            {
                continue;
            }

            GetImageEntrySize(descriptor.Source.Brush, out int width, out int height);
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
            if (!IsImageDescriptorInRange(descriptor, range) ||
                descriptor.Source.Brush is not (ImageBrush or PatternBrush))
            {
                continue;
            }

            if (!TryUploadImageEntry(
                flushContext,
                texture,
                descriptor.Source.Brush,
                atlasY,
                rowBuffer,
                out int entryWidth,
                out int entryHeight,
                out error))
            {
                return false;
            }

            WrapMode wrapX = WrapMode.Repeat;
            WrapMode wrapY = WrapMode.Repeat;
            if (descriptor.Source.Brush is ImageBrush imageBrush)
            {
                wrapX = imageBrush.WrapX;
                wrapY = imageBrush.WrapY;
            }

            int sceneIndex = (int)scene.Layout.DrawDataBase + descriptor.DrawDataWordOffset;
            scene.SetSceneWord(sceneIndex, PackImageAtlasOffset(0, atlasY));
            scene.SetSceneWord(sceneIndex + 1, PackImageExtents(entryWidth, entryHeight));
            scene.SetSceneWord(sceneIndex + 2, PackImageSampleInfo(MapImageWrapMode(wrapX), MapImageWrapMode(wrapY)));
            atlasY += entryHeight;
        }

        error = null;
        return true;
    }

    private static bool IsImageDescriptorInRange(GpuImageDescriptor descriptor, WebGPUSceneRange? range)
    {
        if (!range.HasValue)
        {
            return true;
        }

        WebGPUSceneRange value = range.Value;
        return descriptor.DrawDataWordOffset >= value.DrawDataWordStart &&
            descriptor.DrawDataWordOffset < value.DrawDataWordStart + value.DrawDataWordCount;
    }

    /// <summary>
    /// Creates and uploads the packed gradient-ramp texture used by gradient draw records.
    /// </summary>
    /// <param name="flushContext">The active WebGPU flush context.</param>
    /// <param name="scene">The encoded scene containing gradient rows.</param>
    /// <param name="textureView">The created texture view.</param>
    /// <param name="error">The error message when texture creation fails.</param>
    /// <returns><see langword="true"/> when the texture was created.</returns>
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
    /// <param name="brush">The sampled brush.</param>
    /// <param name="width">The atlas entry width in pixels.</param>
    /// <param name="height">The atlas entry height in pixels.</param>
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
    private static WgpuBuffer* CreateAndUploadCombinedInfoBinDataBuffer(
        WebGPUFlushContext flushContext,
        int infoWordCount,
        ReadOnlySpan<uint> pathGradientData,
        nuint dynamicBinByteLength)
    {
        nuint infoByteLength = checked((nuint)(infoWordCount + pathGradientData.Length) * (nuint)Unsafe.SizeOf<uint>());
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
            WgpuBuffer* buffer = flushContext.Api.DeviceCreateBuffer((Device*)deviceReference.Handle, in descriptor);
            UploadPathGradientData(flushContext, buffer, infoWordCount, pathGradientData);
            return buffer;
        }
    }

    private static void UploadPathGradientData(
        WebGPUFlushContext flushContext,
        WgpuBuffer* buffer,
        int infoWordCount,
        ReadOnlySpan<uint> pathGradientData)
    {
        if (pathGradientData.IsEmpty)
        {
            return;
        }

        nuint offset = checked((nuint)infoWordCount * (nuint)Unsafe.SizeOf<uint>());
        nuint byteLength = checked((nuint)pathGradientData.Length * (nuint)Unsafe.SizeOf<uint>());

        using WebGPUHandle.HandleReference queueReference = flushContext.QueueHandle.AcquireReference();
        fixed (uint* dataPtr = pathGradientData)
        {
            flushContext.Api.QueueWriteBuffer((Queue*)queueReference.Handle, buffer, offset, dataPtr, byteLength);
        }
    }

    /// <summary>
    /// Creates a one-pixel transparent fallback texture so shader bindings stay valid when a scene omits that input.
    /// </summary>
    /// <param name="flushContext">The active WebGPU flush context.</param>
    /// <param name="textureFormat">The texture format to create.</param>
    /// <param name="texture">The created texture.</param>
    /// <param name="textureView">The created texture view.</param>
    /// <param name="error">The error message when texture creation fails.</param>
    /// <returns><see langword="true"/> when the texture and view were created.</returns>
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
    /// <param name="flushContext">The active WebGPU flush context.</param>
    /// <param name="textureFormat">The texture format to create.</param>
    /// <param name="width">The texture width in pixels.</param>
    /// <param name="height">The texture height in pixels.</param>
    /// <param name="textureName">The texture name used in error messages.</param>
    /// <param name="texture">The created texture.</param>
    /// <param name="textureView">The created texture view.</param>
    /// <param name="error">The error message when texture creation fails.</param>
    /// <returns><see langword="true"/> when the texture and view were created.</returns>
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
    /// <param name="mode">The ImageSharp wrap mode.</param>
    /// <returns>The shader atlas extend mode.</returns>
    private static uint MapImageWrapMode(WrapMode mode)
        => mode switch
        {
            WrapMode.Clamp => 0U,   // EXTEND_PAD (samples the nearest edge pixel)
            WrapMode.Repeat => 1U,  // EXTEND_REPEAT
            WrapMode.Mirror => 2U,  // EXTEND_REFLECT
            _ => 3U,                // None -> EXTEND_DECAL (transparent outside the source region)
        };

    private static uint PackImageSampleInfo(uint xExtendMode, uint yExtendMode)
    {
        const uint alpha = 0xFFU;
        const uint alphaTypeStraight = 0U;
        const uint formatRgba = 0U;

        // WebGPU texture sampling returns logical RGBA values regardless of the texture's byte layout.
        // The Bgra32/Rgba32 distinction only belongs to CPU upload/readback memory, not shader colors.
        return alpha
            | (yExtendMode << 8)
            | (xExtendMode << 10)
            | (alphaTypeStraight << 14)
            | (formatRgba << 15);
    }

    private static WgpuBuffer* CreateAndUploadScalarBuffer<T>(
        WebGPUFlushContext flushContext,
        in T value)
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
            WgpuBuffer* buffer = flushContext.Api.DeviceCreateBuffer((Device*)deviceReference.Handle, in descriptor);
            using WebGPUHandle.HandleReference queueReference = flushContext.QueueHandle.AcquireReference();
            flushContext.Api.QueueWriteBuffer(
                (Queue*)queueReference.Handle,
                buffer,
                0,
                Unsafe.AsPointer(ref Unsafe.AsRef(in value)),
                byteLength);
            return buffer;
        }
    }

    /// <summary>
    /// Creates one flush-scoped storage/copy buffer and uploads initial contents when present.
    /// </summary>
    /// <remarks>
    /// Many staging buffers are scratch-only, so the upload branch is intentionally skipped for empty spans.
    /// The method still creates the buffer because later GPU passes depend on the binding existing for the full flush.
    /// </remarks>
    private static WgpuBuffer* CreateAndUploadBuffer<T>(
        WebGPUFlushContext flushContext,
        ReadOnlySpan<T> values,
        uint minimumLength)
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
            WgpuBuffer* buffer = flushContext.Api.DeviceCreateBuffer((Device*)deviceReference.Handle, in descriptor);
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

            return buffer;
        }
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
internal readonly unsafe struct WebGPUSceneResourceSet
{
    /// <summary>
    /// Initializes a new instance of the <see cref="WebGPUSceneResourceSet"/> struct.
    /// </summary>
    /// <param name="headerBuffer">The root scene-config buffer.</param>
    /// <param name="sceneBuffer">The packed scene-data buffer.</param>
    /// <param name="pathReducedBuffer">The first pathtag-reduction scratch buffer.</param>
    /// <param name="pathReduced2Buffer">The second pathtag-reduction scratch buffer.</param>
    /// <param name="pathReducedScanBuffer">The pathtag scan scratch buffer.</param>
    /// <param name="pathMonoidBuffer">The final pathtag monoid buffer.</param>
    /// <param name="pathBboxBuffer">The per-path bounding-box buffer.</param>
    /// <param name="drawReducedBuffer">The draw reduction buffer.</param>
    /// <param name="drawMonoidBuffer">The final draw monoid buffer.</param>
    /// <param name="infoBinDataBuffer">The packed info-bin data buffer.</param>
    /// <param name="clipInputBuffer">The clip input buffer.</param>
    /// <param name="clipElementBuffer">The clip element buffer.</param>
    /// <param name="clipBicBuffer">The clip bicubic coefficient buffer.</param>
    /// <param name="clipBboxBuffer">The clip bounding-box buffer.</param>
    /// <param name="drawBboxBuffer">The draw bounding-box buffer.</param>
    /// <param name="pathBuffer">The flattened path buffer.</param>
    /// <param name="lineBuffer">The flattened line buffer.</param>
    /// <param name="gradientTextureView">The gradient texture view.</param>
    /// <param name="imageAtlasTextureView">The image atlas texture view.</param>
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
/// Reusable scene resource buffers owned by either a retained scene or the backend eager cache.
/// </summary>
/// <remarks>
/// These buffers hold per-render resource contents after each upload, so callers must rent one
/// arena for exclusive use during staging and return it only after the staged scene has finished.
/// Textures are intentionally excluded because gradient and image-atlas contents are scene-dependent.
/// </remarks>
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
    /// <param name="flushContext">The active WebGPU flush context.</param>
    /// <param name="bufferSizes">The required buffer sizes.</param>
    /// <param name="infoBinDataByteLength">The required info-bin data length in bytes.</param>
    /// <param name="sceneByteLength">The required scene-data length in bytes.</param>
    /// <returns><see langword="true"/> when the arena can be reused.</returns>
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
    /// <param name="arena">The arena to dispose.</param>
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
/// Per-path bounding box and scheduling data written by the flatten pass.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal readonly struct GpuPathBbox
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GpuPathBbox"/> struct.
    /// </summary>
    /// <param name="x0">The left edge of the transformed integer path bounds.</param>
    /// <param name="y0">The top edge of the transformed integer path bounds.</param>
    /// <param name="x1">The right edge of the transformed integer path bounds.</param>
    /// <param name="y1">The bottom edge of the transformed integer path bounds.</param>
    /// <param name="drawFlags">The draw flags associated with the path.</param>
    /// <param name="transIndex">The transform index associated with the path.</param>
    /// <param name="coverageThreshold">The aliased coverage threshold for the path.</param>
    /// <param name="padding">The reserved layout slot matching <c>PathBbox._padding</c> in <c>bbox.wgsl</c>.</param>
    /// <param name="interest">The root-target-local raster interest rectangle.</param>
    public GpuPathBbox(
        int x0,
        int y0,
        int x1,
        int y1,
        uint drawFlags,
        uint transIndex,
        float coverageThreshold,
        uint padding,
        Vector4 interest)
    {
        this.X0 = x0;
        this.Y0 = y0;
        this.X1 = x1;
        this.Y1 = y1;
        this.DrawFlags = drawFlags;
        this.TransIndex = transIndex;
        this.CoverageThreshold = coverageThreshold;
        this.Padding = padding;
        this.Interest = interest;
    }

    /// <summary>
    /// Gets the left edge of the transformed integer path bounds.
    /// </summary>
    public int X0 { get; }

    /// <summary>
    /// Gets the top edge of the transformed integer path bounds.
    /// </summary>
    public int Y0 { get; }

    /// <summary>
    /// Gets the right edge of the transformed integer path bounds.
    /// </summary>
    public int X1 { get; }

    /// <summary>
    /// Gets the bottom edge of the transformed integer path bounds.
    /// </summary>
    public int Y1 { get; }

    /// <summary>
    /// Gets the draw flags associated with this path.
    /// </summary>
    public uint DrawFlags { get; }

    /// <summary>
    /// Gets the transform index associated with this path.
    /// </summary>
    public uint TransIndex { get; }

    /// <summary>
    /// Gets the aliased coverage threshold for this path.
    /// </summary>
    public float CoverageThreshold { get; }

    /// <summary>
    /// Gets the reserved layout slot matching <c>PathBbox._padding</c> in <c>bbox.wgsl</c>.
    /// </summary>
    public uint Padding { get; }

    /// <summary>
    /// Gets the root-target-local raster interest rectangle.
    /// </summary>
    public Vector4 Interest { get; }
}

/// <summary>
/// Clip input record mapping one draw object to the path that defines its clip stack entry.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal readonly struct GpuClipInp
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GpuClipInp"/> struct.
    /// </summary>
    /// <param name="drawIndex">The draw object index for the clip record.</param>
    /// <param name="pathIndex">The begin-clip path index, or the bitwise-not end-clip draw object index.</param>
    /// <param name="operation">The ImageSharp render clip operation for begin-clip records.</param>
    public GpuClipInp(uint drawIndex, int pathIndex, uint operation)
    {
        this.DrawIndex = drawIndex;
        this.PathIndex = pathIndex;
        this.Operation = operation;
    }

    /// <summary>
    /// Gets the draw object index for the clip record.
    /// </summary>
    public uint DrawIndex { get; }

    /// <summary>
    /// Gets the begin-clip path index, or the bitwise-not end-clip draw object index.
    /// </summary>
    public int PathIndex { get; }

    /// <summary>
    /// Gets the ImageSharp render clip operation.
    /// </summary>
    /// <remarks>
    /// Vello's source clip record stores only <c>DrawIndex</c> and <c>PathIndex</c>.
    /// This field is ImageSharp's extension for <see cref="ClipOperation.Difference"/>.
    /// </remarks>
    public uint Operation { get; }
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
    // ClipEl in clip.wgsl places bbox at offset 16 because vec4<f32> has 16-byte
    // alignment in storage buffers. These fields are part of the GPU ABI.
    private readonly uint padding0;
    private readonly uint padding1;
    private readonly uint padding2;

    public GpuClipElement(uint parentIndex, Vector4 bbox)
    {
        this.ParentIndex = parentIndex;
        this.padding0 = 0;
        this.padding1 = 0;
        this.padding2 = 0;
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
        uint pathGradientDataBase,
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
        this.PathGradientDataBase = pathGradientDataBase;
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

    public uint PathGradientDataBase { get; }

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
/// Per-path scheduling record used after draw and clip reduction have established final bounds.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal readonly struct GpuScenePath
{
    // Path in tile.wgsl has a vec4<u32> followed by one u32 and therefore a
    // 32-byte array stride. The padding keeps reusable GPU buffer sizing exact.
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
    // LineSoup in segment.wgsl has a u32 followed by vec2<f32> values. WGSL
    // aligns the first vec2 to offset 8, so this field is part of the buffer ABI.
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
    // Segment in segment.wgsl has two vec2<f32> values followed by one f32 and
    // a 24-byte array stride. The final slot preserves that stride from C#.
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
