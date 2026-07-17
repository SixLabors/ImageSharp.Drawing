// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

#pragma warning disable SA1201 // Staged scene types are grouped by pipeline role.

using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Uploads one encoded scene into flush-scoped GPU resources for the staged WebGPU rasterizer.
/// </summary>
internal static unsafe class WebGPUSceneResources
{
    /// <summary>
    /// Creates the flush-scoped GPU resources required by the staged scene pipeline.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format of the target and of any sampled image brushes.</typeparam>
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
    /// <typeparam name="TPixel">The pixel format of the target and of any sampled image brushes.</typeparam>
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
    /// <typeparam name="TPixel">The pixel format of the target and of any sampled image brushes.</typeparam>
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

    /// <summary>
    /// Creates the staged-scene GPU resources, reusing an existing arena when its capacities fit.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format of the target and of any sampled image brushes.</typeparam>
    /// <param name="flushContext">The active WebGPU flush context.</param>
    /// <param name="scene">The encoded scene to stage.</param>
    /// <param name="range">The scene range to stage, or <see langword="null"/> for the full scene.</param>
    /// <param name="config">The scene configuration.</param>
    /// <param name="baseColor">The packed base color for the target.</param>
    /// <param name="externalTextureView">The target texture view supplied by the caller, or <see langword="null"/>.</param>
    /// <param name="arena">The reusable resource arena for this staging operation.</param>
    /// <param name="resources">The staged resource set.</param>
    /// <param name="error">The error message when resource creation fails.</param>
    /// <returns><see langword="true"/> when the resources were created.</returns>
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

        // Compute byte lengths for the two variable-size data buffers. The combined
        // info/bin-data buffer is laid out as [info | brush data | bin data | bin headers],
        // matching config.bin_data_start and config.brush_data_base in Shared/config.wgsl.
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
            UploadBrushData<TPixel>(flushContext, arena.InfoBinDataBuffer, infoWordCount, scene);

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

        WgpuBuffer* infoBinDataBuffer = CreateAndUploadCombinedInfoBinDataBuffer<TPixel>(
            flushContext,
            infoWordCount,
            scene,
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
        // The ptcl_dyn_start value (sixth layout argument) marks where the bump-allocated
        // ptcl tail begins: after the fixed 64-word (WebGPUSceneDispatch.PtclInitialAlloc)
        // reservation for every tile slot in the chunk window.
        GpuSceneLayout layout = new(
            scene.Layout.DrawObjectCount,
            scene.Layout.PathCount,
            scene.Layout.ClipCount,
            scene.Layout.BinDataStart,
            scene.Layout.BrushDataBase,
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
        // Tiles are 16x16 pixels (TILE_WIDTH/TILE_HEIGHT in Shared/config.wgsl).
        int tileCountX = (range.TargetBounds.Width + 15) / 16;
        int tileCountY = (range.TargetBounds.Height + 15) / 16;

        // The combined info/bin-data buffer is [info | path-gradient | bin data | bin headers],
        // so bin data starts after both ranges and the gradient data starts after the info
        // words. The ptcl_dyn_start value (sixth layout argument) marks where the
        // bump-allocated ptcl tail begins: after the fixed 64-word
        // (WebGPUSceneDispatch.PtclInitialAlloc) reservation for every tile slot.
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

    /// <summary>
    /// Creates the sampled image-atlas texture and patches each image's draw-data words
    /// with its atlas placement, extents, and sample-info word.
    /// </summary>
    /// <remarks>
    /// Entries are stacked vertically in a single column: the atlas is as wide as the
    /// widest entry and as tall as the sum of entry heights. When an external texture view
    /// is supplied it is bound directly and only the draw-data words are rewritten.
    /// </remarks>
    /// <typeparam name="TPixel">The pixel format of the target and of any sampled image brushes.</typeparam>
    /// <param name="flushContext">The active WebGPU flush context.</param>
    /// <param name="scene">The encoded scene containing image descriptors.</param>
    /// <param name="range">The scene range to stage, or <see langword="null"/> for the full scene.</param>
    /// <param name="textureFormat">The texture format to create.</param>
    /// <param name="externalTextureView">The caller-supplied texture view, or <see langword="null"/>.</param>
    /// <param name="textureView">The created or supplied texture view.</param>
    /// <param name="error">The error message when texture creation fails.</param>
    /// <returns><see langword="true"/> when the texture view was produced.</returns>
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
        WebGPUTargetNumericEncoding pixelNumericEncoding = WebGPUDrawingBackend.CreateOffscreenTargetDescriptor(
            flushContext.TargetDescriptor.Format,
            flushContext.TargetDescriptor.AlphaRepresentation).NumericEncoding;

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
                    scene.SetSceneWord(
                        sceneIndex + 2,
                        PackImageSampleInfo(
                            MapImageWrapMode(descriptor.Source.WrapX),
                            MapImageWrapMode(descriptor.Source.WrapY),
                            flushContext.TargetDescriptor.AlphaRepresentation,
                            flushContext.TargetDescriptor.NumericEncoding));
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
            // Sampled image textures use the target pixel format's native numeric encoding.
            // Constructing the placeholder through TPixel maps logical transparent black to the
            // physical zero point required by signed-unit formats.
            TPixel transparentPixel = TPixel.FromScaledVector4(Vector4.Zero);

            return TryCreateSinglePixelSampledTexture(flushContext, textureFormat, transparentPixel, out _, out textureView, out error);
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
            scene.SetSceneWord(
                sceneIndex + 2,
                PackImageSampleInfo(
                    MapImageWrapMode(wrapX),
                    MapImageWrapMode(wrapY),
                    flushContext.TargetDescriptor.AlphaRepresentation,
                    pixelNumericEncoding));
            atlasY += entryHeight;
        }

        error = null;
        return true;
    }

    /// <summary>
    /// Determines whether an image descriptor's draw data falls inside the staged range.
    /// </summary>
    /// <param name="descriptor">The image descriptor to test.</param>
    /// <param name="range">The scene range to stage, or <see langword="null"/> for the full scene.</param>
    /// <returns><see langword="true"/> when the descriptor belongs to the staged range.</returns>
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
    /// <remarks>
    /// Each gradient occupies one 512-texel RGBA16Float row; the shader's ramp index selects the row.
    /// </remarks>
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
            // Gradient rows contain associated binary16 components, for which physical zero is transparent.
            ulong transparentPixel = 0;

            return TryCreateSinglePixelSampledTexture(flushContext, TextureFormat.RGBA16Float, transparentPixel, out _, out textureView, out error);
        }

        TextureDescriptor textureDescriptor = new()
        {
            usage = (ulong)(TextureUsage.TextureBinding | TextureUsage.CopyDst),
            dimension = TextureDimension._2D,

            // 512 must match GRADIENT_WIDTH in fine.wgsl and the encoder's ramp width.
            size = new Extent3D(512, (uint)scene.GradientRowCount, 1),
            format = TextureFormat.RGBA16Float,
            mipLevelCount = 1,
            sampleCount = 1
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
            format = TextureFormat.RGBA16Float,
            dimension = TextureViewDimension._2D,
            baseMipLevel = 0,
            mipLevelCount = 1,
            baseArrayLayer = 0,
            arrayLayerCount = 1,
            aspect = TextureAspect.All
        };

        textureView = flushContext.Api.TextureCreateView(texture, &textureViewDescriptor);
        if (textureView is null)
        {
            flushContext.Api.TextureRelease(texture);
            error = "Failed to create a gradient texture view.";
            return false;
        }

        TextureDataLayout layout = new()
        {
            offset = 0,
            bytesPerRow = 512 * 8,
            rowsPerImage = (uint)scene.GradientRowCount
        };

        ImageCopyTexture destination = new()
        {
            texture = texture,
            mipLevel = 0,
            origin = new Origin3D(0, 0, 0),
            aspect = TextureAspect.All
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

    /// <summary>
    /// Uploads one sampled brush entry into the image atlas at the given vertical offset.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format of the target and of any sampled image brushes.</typeparam>
    /// <param name="flushContext">The active WebGPU flush context.</param>
    /// <param name="texture">The atlas texture.</param>
    /// <param name="brush">The sampled brush; an <see cref="ImageBrush"/> or <see cref="PatternBrush"/>.</param>
    /// <param name="atlasY">The vertical atlas offset of the entry in pixels.</param>
    /// <param name="rowBuffer">The scratch row buffer used to convert pattern colors.</param>
    /// <param name="entryWidth">The uploaded entry width in pixels.</param>
    /// <param name="entryHeight">The uploaded entry height in pixels.</param>
    /// <param name="error">The error message when the upload fails.</param>
    /// <returns><see langword="true"/> when the entry was uploaded.</returns>
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

    /// <summary>
    /// Converts a pattern brush's color matrix to target pixels and uploads it row by row.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format of the target and of any sampled image brushes.</typeparam>
    /// <param name="flushContext">The active WebGPU flush context.</param>
    /// <param name="texture">The atlas texture.</param>
    /// <param name="patternBrush">The pattern brush to upload.</param>
    /// <param name="atlasY">The vertical atlas offset of the entry in pixels.</param>
    /// <param name="rowBuffer">The scratch row buffer used to convert pattern colors.</param>
    /// <param name="entryWidth">The uploaded entry width in pixels.</param>
    /// <param name="entryHeight">The uploaded entry height in pixels.</param>
    /// <param name="error">The error message when the upload fails.</param>
    /// <returns><see langword="true"/> when the entry was uploaded.</returns>
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

    /// <summary>
    /// Uploads the source region of an image brush into the atlas row by row.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format of the target and of any sampled image brushes.</typeparam>
    /// <param name="flushContext">The active WebGPU flush context.</param>
    /// <param name="texture">The atlas texture.</param>
    /// <param name="imageBrush">The image brush to upload.</param>
    /// <param name="atlasY">The vertical atlas offset of the entry in pixels.</param>
    /// <param name="entryWidth">The uploaded entry width in pixels.</param>
    /// <param name="entryHeight">The uploaded entry height in pixels.</param>
    /// <param name="error">The error message when the upload fails.</param>
    /// <returns><see langword="true"/> when the entry was uploaded.</returns>
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
    /// <remarks>
    /// The buffer layout is [info | path-gradient | bin data | bin headers]; only the
    /// path-gradient words are uploaded here, the rest is GPU-written scratch.
    /// </remarks>
    /// <param name="flushContext">The active WebGPU flush context.</param>
    /// <param name="infoWordCount">The number of GPU-written info words preceding the gradient data.</param>
    /// <param name="scene">The encoded scene supplying auxiliary brush data.</param>
    /// <param name="dynamicBinByteLength">The combined bin-data and bin-header byte length appended after the gradient data.</param>
    /// <returns>The created buffer.</returns>
    private static WgpuBuffer* CreateAndUploadCombinedInfoBinDataBuffer<TPixel>(
        WebGPUFlushContext flushContext,
        int infoWordCount,
        WebGPUEncodedScene scene,
        nuint dynamicBinByteLength)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        ReadOnlySpan<uint> brushData = scene.PathGradientData.Span;
        nuint infoByteLength = checked((nuint)(infoWordCount + brushData.Length) * (nuint)Unsafe.SizeOf<uint>());
        nuint totalByteLength = checked(infoByteLength + dynamicBinByteLength);
        if (totalByteLength == 0)
        {
            totalByteLength = (nuint)Unsafe.SizeOf<uint>();
        }

        BufferDescriptor descriptor = new()
        {
            usage = (ulong)(BufferUsage.Storage | BufferUsage.CopyDst),
            size = totalByteLength
        };

        using (WebGPUHandle.HandleReference deviceReference = flushContext.DeviceHandle.AcquireReference())
        {
            WgpuBuffer* buffer = flushContext.Api.DeviceCreateBuffer((Device*)deviceReference.Handle, in descriptor);
            UploadBrushData<TPixel>(flushContext, buffer, infoWordCount, scene);
            return buffer;
        }
    }

    /// <summary>
    /// Writes auxiliary brush data immediately after the info region, at the
    /// word offset published as <c>config.brush_data_base</c> in Shared/config.wgsl.
    /// </summary>
    /// <param name="flushContext">The active WebGPU flush context.</param>
    /// <param name="buffer">The combined info/bin-data buffer.</param>
    /// <param name="infoWordCount">The number of info words preceding the brush data.</param>
    /// <param name="scene">The encoded scene supplying static and target-specialized brush data.</param>
    private static void UploadBrushData<TPixel>(
        WebGPUFlushContext flushContext,
        WgpuBuffer* buffer,
        int infoWordCount,
        WebGPUEncodedScene scene)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        ReadOnlySpan<uint> brushData = scene.PathGradientData.Span;
        if (brushData.IsEmpty)
        {
            return;
        }

        nuint offset = checked((nuint)infoWordCount * (nuint)Unsafe.SizeOf<uint>());
        nuint byteLength = checked((nuint)brushData.Length * (nuint)Unsafe.SizeOf<uint>());

        using WebGPUHandle.HandleReference queueReference = flushContext.QueueHandle.AcquireReference();
        Queue* queue = (Queue*)queueReference.Handle;
        fixed (uint* dataPtr = brushData)
        {
            flushContext.Api.QueueWriteBuffer(queue, buffer, offset, dataPtr, byteLength);
        }

        PixelAlphaRepresentation alphaRepresentation = TPixel.GetPixelTypeInfo().AlphaRepresentation;
        IReadOnlyList<GpuRecolorDescriptor> recolors = scene.Recolors;
        Span<uint> payload = stackalloc uint[9];
        for (int i = 0; i < recolors.Count; i++)
        {
            GpuRecolorDescriptor descriptor = recolors[i];

            // These are the exact two CPU conversions performed when RecolorBrush creates its
            // TPixel renderer. Specializing here retains Color precision without exposing its
            // private storage-association state to the scene format or shader.
            Vector4 source = descriptor.SourceColor.ToScaledVector4(alphaRepresentation);
            TPixel targetPixel = descriptor.TargetColor.ToPixel<TPixel>();
            Vector4 target = targetPixel.ToScaledVector4();
            payload[0] = BitConverter.SingleToUInt32Bits(source.X);
            payload[1] = BitConverter.SingleToUInt32Bits(source.Y);
            payload[2] = BitConverter.SingleToUInt32Bits(source.Z);
            payload[3] = BitConverter.SingleToUInt32Bits(source.W);
            payload[4] = BitConverter.SingleToUInt32Bits(target.X);
            payload[5] = BitConverter.SingleToUInt32Bits(target.Y);
            payload[6] = BitConverter.SingleToUInt32Bits(target.Z);
            payload[7] = BitConverter.SingleToUInt32Bits(target.W);
            payload[8] = BitConverter.SingleToUInt32Bits(descriptor.Threshold);

            nuint payloadOffset = checked((nuint)(infoWordCount + descriptor.BrushDataWordOffset) * (nuint)Unsafe.SizeOf<uint>());
            fixed (uint* payloadPtr = payload)
            {
                flushContext.Api.QueueWriteBuffer(queue, buffer, payloadOffset, payloadPtr, 9U * (nuint)Unsafe.SizeOf<uint>());
            }
        }
    }

    /// <summary>
    /// Creates a one-pixel fallback texture so shader bindings stay valid when a scene omits that input.
    /// </summary>
    /// <param name="flushContext">The active WebGPU flush context.</param>
    /// <param name="textureFormat">The texture format to create.</param>
    /// <param name="pixel">The physical texel value to upload.</param>
    /// <param name="texture">The created texture.</param>
    /// <param name="textureView">The created texture view.</param>
    /// <param name="error">The error message when texture creation fails.</param>
    /// <returns><see langword="true"/> when the texture and view were created.</returns>
    private static bool TryCreateSinglePixelSampledTexture<TPixel>(
        WebGPUFlushContext flushContext,
        TextureFormat textureFormat,
        TPixel pixel,
        out Texture* texture,
        out TextureView* textureView,
        out string? error)
        where TPixel : unmanaged
    {
        TextureDescriptor textureDescriptor = new()
        {
            usage = (ulong)(TextureUsage.TextureBinding | TextureUsage.CopyDst),
            dimension = TextureDimension._2D,
            size = new Extent3D(1, 1, 1),
            format = textureFormat,
            mipLevelCount = 1,
            sampleCount = 1
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
            format = textureFormat,
            dimension = TextureViewDimension._2D,
            baseMipLevel = 0,
            mipLevelCount = 1,
            baseArrayLayer = 0,
            arrayLayerCount = 1,
            aspect = TextureAspect.All
        };

        textureView = flushContext.Api.TextureCreateView(texture, &textureViewDescriptor);
        if (textureView is null)
        {
            flushContext.Api.TextureRelease(texture);
            texture = null;
            error = "Failed to create a sampled scene texture view.";
            return false;
        }

        nuint pixelSize = (nuint)Unsafe.SizeOf<TPixel>();
        ImageCopyTexture destination = new()
        {
            texture = texture,
            mipLevel = 0,
            origin = new Origin3D(0, 0, 0),
            aspect = TextureAspect.All
        };

        TextureDataLayout layout = new()
        {
            offset = 0,
            bytesPerRow = (uint)pixelSize,
            rowsPerImage = 1
        };

        Extent3D size = new(1, 1, 1);
        using (WebGPUHandle.HandleReference queueReference = flushContext.QueueHandle.AcquireReference())
        {
            flushContext.Api.QueueWriteTexture((Queue*)queueReference.Handle, in destination, &pixel, pixelSize, in layout, in size);
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
            usage = (ulong)(TextureUsage.TextureBinding | TextureUsage.CopyDst),
            dimension = TextureDimension._2D,
            size = new Extent3D((uint)width, (uint)height, 1),
            format = textureFormat,
            mipLevelCount = 1,
            sampleCount = 1
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
            format = textureFormat,
            dimension = TextureViewDimension._2D,
            baseMipLevel = 0,
            mipLevelCount = 1,
            baseArrayLayer = 0,
            arrayLayerCount = 1,
            aspect = TextureAspect.All
        };

        textureView = flushContext.Api.TextureCreateView(texture, &textureViewDescriptor);
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

    /// <summary>
    /// Writes a rectangular block of pixels into a texture through the queue.
    /// </summary>
    /// <typeparam name="TPixel">The unmanaged pixel type being uploaded.</typeparam>
    /// <param name="flushContext">The active WebGPU flush context.</param>
    /// <param name="texture">The destination texture.</param>
    /// <param name="x">The destination x origin in pixels.</param>
    /// <param name="y">The destination y origin in pixels.</param>
    /// <param name="width">The region width in pixels.</param>
    /// <param name="height">The region height in pixels.</param>
    /// <param name="pixels">The tightly packed source pixels.</param>
    /// <param name="error">The error message when the write fails.</param>
    /// <returns><see langword="true"/> when the region was written.</returns>
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
            offset = 0,
            bytesPerRow = (uint)(width * Unsafe.SizeOf<TPixel>()),
            rowsPerImage = (uint)height
        };

        fixed (TPixel* pixelPtr = pixels)
        {
            ImageCopyTexture destination = new()
            {
                texture = texture,
                mipLevel = 0,
                origin = new Origin3D((uint)x, (uint)y, 0),
                aspect = TextureAspect.All
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
    /// Unpacked as x = high 16 bits, y = low 16 bits by <c>read_image</c> in fine.wgsl.
    /// </summary>
    /// <param name="x">The atlas x origin in pixels.</param>
    /// <param name="y">The atlas y origin in pixels.</param>
    /// <returns>The packed origin word.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint PackImageAtlasOffset(int x, int y)
        => ((uint)x << 16) | (uint)y;

    /// <summary>
    /// Packs the sampled image extents stored in draw data for sampled image brushes.
    /// Unpacked as width = high 16 bits, height = low 16 bits by <c>read_image</c> in fine.wgsl.
    /// </summary>
    /// <param name="width">The entry width in pixels.</param>
    /// <param name="height">The entry height in pixels.</param>
    /// <returns>The packed extents word.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint PackImageExtents(int width, int height)
        => ((uint)width << 16) | (uint)height;

    /// <summary>
    /// Maps an ImageSharp wrap mode to the shader atlas extend mode.
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

    /// <summary>
    /// Packs the image sample-info word decoded by <c>read_image</c> in fine.wgsl:
    /// bits 0-7 alpha, 8-9 y extend, 10-11 x extend, 14 alpha type, 15 pixel format,
    /// and 16 signed-unit numeric encoding.
    /// </summary>
    /// <param name="xExtendMode">The horizontal atlas extend mode.</param>
    /// <param name="yExtendMode">The vertical atlas extend mode.</param>
    /// <param name="alphaRepresentation">The alpha representation stored by the atlas texels.</param>
    /// <param name="numericEncoding">The mapping between the atlas's native and unit channel values.</param>
    /// <returns>The packed sample-info word.</returns>
    private static uint PackImageSampleInfo(
        uint xExtendMode,
        uint yExtendMode,
        PixelAlphaRepresentation alphaRepresentation,
        WebGPUTargetNumericEncoding numericEncoding)
    {
        const uint alpha = 0xFFU;
        const uint formatRgba = 0U;
        uint alphaType = alphaRepresentation == PixelAlphaRepresentation.Associated ? 1U : 0U;
        uint signedUnit = numericEncoding == WebGPUTargetNumericEncoding.SignedUnit ? 1U : 0U;

        // WebGPU texture sampling returns logical RGBA values regardless of the texture's byte layout.
        // The Bgra32/Rgba32 distinction only belongs to CPU upload/readback memory, not shader colors.
        return alpha
            | (yExtendMode << 8)
            | (xExtendMode << 10)
            | (alphaType << 14)
            | (formatRgba << 15)
            | (signedUnit << 16);
    }

    /// <summary>
    /// Creates a buffer sized for exactly one value and uploads it.
    /// Used for the scene-config header, which binds as both storage and uniform.
    /// </summary>
    /// <typeparam name="T">The unmanaged value type to upload.</typeparam>
    /// <param name="flushContext">The active WebGPU flush context.</param>
    /// <param name="value">The value to upload.</param>
    /// <returns>The created buffer.</returns>
    private static WgpuBuffer* CreateAndUploadScalarBuffer<T>(
        WebGPUFlushContext flushContext,
        in T value)
        where T : unmanaged
    {
        nuint byteLength = (nuint)Unsafe.SizeOf<T>();
        BufferDescriptor descriptor = new()
        {
            usage = (ulong)(BufferUsage.Storage | BufferUsage.Uniform | BufferUsage.CopyDst),
            size = byteLength
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
    /// <typeparam name="T">The unmanaged element type of the buffer.</typeparam>
    /// <param name="flushContext">The active WebGPU flush context.</param>
    /// <param name="values">The initial contents to upload; may be empty for scratch buffers.</param>
    /// <param name="minimumLength">The minimum element capacity to reserve.</param>
    /// <returns>The created buffer.</returns>
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
            usage = (ulong)(BufferUsage.Storage | BufferUsage.CopyDst),
            size = byteLength
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
    /// <typeparam name="T">The unmanaged element type of the binding.</typeparam>
    /// <param name="count">The element count; zero is clamped to one.</param>
    /// <returns>The binding size in bytes.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static nuint GetBindingByteLength<T>(int count)
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
    /// <param name="clipBicBuffer">The clip bic (stack-monoid) reduction buffer.</param>
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
    /// <summary>
    /// Initializes a new instance of the <see cref="WebGPUSceneResourceArena"/> class.
    /// </summary>
    /// <param name="api">The WebGPU API instance used to release the buffers.</param>
    /// <param name="device">The device handle the buffers were created on.</param>
    /// <param name="capacitySizes">The buffer sizes the arena was allocated with.</param>
    /// <param name="infoBinDataByteCapacity">The combined info/bin-data buffer capacity in bytes.</param>
    /// <param name="sceneByteCapacity">The scene-data buffer capacity in bytes.</param>
    /// <param name="headerBuffer">The root scene-config buffer.</param>
    /// <param name="sceneBuffer">The packed scene-data buffer.</param>
    /// <param name="pathReducedBuffer">The first pathtag-reduction scratch buffer.</param>
    /// <param name="pathReduced2Buffer">The second pathtag-reduction scratch buffer.</param>
    /// <param name="pathReducedScanBuffer">The pathtag scan scratch buffer.</param>
    /// <param name="pathMonoidBuffer">The final pathtag monoid buffer.</param>
    /// <param name="pathBboxBuffer">The per-path bounding-box buffer.</param>
    /// <param name="drawReducedBuffer">The draw reduction buffer.</param>
    /// <param name="drawMonoidBuffer">The final draw monoid buffer.</param>
    /// <param name="infoBinDataBuffer">The combined info/bin-data buffer.</param>
    /// <param name="clipInputBuffer">The clip input buffer.</param>
    /// <param name="clipElementBuffer">The clip element buffer.</param>
    /// <param name="clipBicBuffer">The clip bic (stack-monoid) reduction buffer.</param>
    /// <param name="clipBboxBuffer">The clip bounding-box buffer.</param>
    /// <param name="drawBboxBuffer">The draw bounding-box buffer.</param>
    /// <param name="pathBuffer">The per-path scheduling buffer.</param>
    /// <param name="lineBuffer">The flattened line buffer.</param>
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

    /// <summary>
    /// Gets the WebGPU API instance used to release the buffers.
    /// </summary>
    public WebGPU Api { get; }

    /// <summary>
    /// Gets the device handle the buffers were created on; reuse requires the same device.
    /// </summary>
    public WebGPUDeviceHandle Device { get; }

    /// <summary>
    /// Gets the buffer sizes the arena was allocated with.
    /// </summary>
    public WebGPUSceneBufferSizes CapacitySizes { get; }

    /// <summary>
    /// Gets the combined info/bin-data buffer capacity in bytes.
    /// </summary>
    public nuint InfoBinDataByteCapacity { get; }

    /// <summary>
    /// Gets the scene-data buffer capacity in bytes.
    /// </summary>
    public nuint SceneByteCapacity { get; }

    /// <summary>
    /// Gets the root scene-config buffer.
    /// </summary>
    public WgpuBuffer* HeaderBuffer { get; }

    /// <summary>
    /// Gets the packed scene-data buffer.
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
    /// Gets the pathtag scan scratch buffer.
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
    /// Gets the draw reduction buffer.
    /// </summary>
    public WgpuBuffer* DrawReducedBuffer { get; }

    /// <summary>
    /// Gets the final draw monoid buffer.
    /// </summary>
    public WgpuBuffer* DrawMonoidBuffer { get; }

    /// <summary>
    /// Gets the combined info/bin-data buffer.
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
    /// Gets the clip bic (stack-monoid) reduction buffer.
    /// </summary>
    public WgpuBuffer* ClipBicBuffer { get; }

    /// <summary>
    /// Gets the clip bounding-box buffer.
    /// </summary>
    public WgpuBuffer* ClipBboxBuffer { get; }

    /// <summary>
    /// Gets the draw bounding-box buffer.
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
/// <remarks>
/// Mirrors <c>BumpAllocators</c> in Shared/bump.wgsl (each field is an <c>atomic&lt;u32&gt;</c>
/// there); field order must match. The C# side reads this block back to detect overflow
/// and grow the scratch buffers before retrying the render.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
internal struct GpuSceneBumpAllocators
{
    /// <summary>
    /// The bitmask of <c>STAGE_*</c> flags for stages that failed allocation.
    /// </summary>
    public uint Failed;

    /// <summary>
    /// The bin-data word allocation head within the combined info/bin-data buffer.
    /// </summary>
    public uint Binning;

    /// <summary>
    /// The per-tile command list word allocation head.
    /// </summary>
    public uint Ptcl;

    /// <summary>
    /// The sparse path-row record allocation head.
    /// </summary>
    public uint PathRows;

    /// <summary>
    /// The tile record allocation head.
    /// </summary>
    public uint Tile;

    /// <summary>
    /// The segment-count record allocation head.
    /// </summary>
    public uint SegCounts;

    /// <summary>
    /// The segment record allocation head.
    /// </summary>
    public uint Segments;

    /// <summary>
    /// The allocation head for blend stack slots spilled past <c>BLEND_STACK_SPLIT</c>.
    /// </summary>
    public uint BlendSpill;

    /// <summary>
    /// The flattened line record allocation head.
    /// </summary>
    public uint Lines;
}

/// <summary>
/// Prefix-scan monoid emitted from the packed path-tag stream.
/// </summary>
/// <remarks>
/// Mirrors <c>TagMonoid</c> in Shared/pathtag.wgsl; field order must match.
/// Each field counts the stream elements of its kind preceding the current position.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
internal readonly struct GpuTagMonoid
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GpuTagMonoid"/> struct.
    /// </summary>
    /// <param name="transIndex">The transform marker count.</param>
    /// <param name="pathSegmentOffset">The path data stream offset in words.</param>
    /// <param name="styleIndex">The style marker count premultiplied by the style word size.</param>
    /// <param name="pathIndex">The path marker count.</param>
    public GpuTagMonoid(uint transIndex, uint pathSegmentOffset, uint styleIndex, uint pathIndex)
    {
        this.TransIndex = transIndex;
        this.PathSegmentOffset = pathSegmentOffset;
        this.StyleIndex = styleIndex;
        this.PathIndex = pathIndex;
    }

    /// <summary>
    /// Gets the transform marker count (index into the transform stream).
    /// </summary>
    public uint TransIndex { get; }

    /// <summary>
    /// Gets the offset into the path data stream, in u32 words.
    /// </summary>
    public uint PathSegmentOffset { get; }

    /// <summary>
    /// Gets the style marker count premultiplied by <c>STYLE_SIZE_IN_WORDS</c>.
    /// </summary>
    public uint StyleIndex { get; }

    /// <summary>
    /// Gets the path marker count (index of the current path).
    /// </summary>
    public uint PathIndex { get; }
}

/// <summary>
/// Per-path bounding box and scheduling data written by the flatten pass.
/// </summary>
/// <remarks>
/// Mirrors <c>PathBbox</c> in Shared/bbox.wgsl; field order and the 48-byte stride must
/// match. The padding word keeps <see cref="Interest"/> at offset 32, satisfying the
/// 16-byte alignment WGSL requires for <c>vec4&lt;f32&gt;</c>.
/// </remarks>
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
/// <remarks>
/// Mirrors <c>ClipInp</c> in Shared/clip.wgsl; field order must match.
/// </remarks>
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
/// Stack-monoid element (the "bicyclic semigroup") used by the clip reduction passes.
/// </summary>
/// <remarks>
/// Mirrors <c>Bic</c> in Shared/clip.wgsl; field order must match.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
internal readonly struct GpuBic
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GpuBic"/> struct.
    /// </summary>
    /// <param name="a">The number of unmatched end-clip records (pops) in the range.</param>
    /// <param name="b">The number of unmatched begin-clip records (pushes) in the range.</param>
    public GpuBic(uint a, uint b)
    {
        this.A = a;
        this.B = b;
    }

    /// <summary>
    /// Gets the number of unmatched end-clip records (pops) in the range.
    /// </summary>
    public uint A { get; }

    /// <summary>
    /// Gets the number of unmatched begin-clip records (pushes) in the range.
    /// </summary>
    public uint B { get; }
}

/// <summary>
/// Reduced clip element containing the parent link and accumulated clip bounds.
/// </summary>
/// <remarks>
/// Mirrors <c>ClipEl</c> in Shared/clip.wgsl; field order and the 32-byte stride must match.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
internal readonly struct GpuClipElement
{
    // ClipEl in clip.wgsl places bbox at offset 16 because vec4<f32> has 16-byte
    // alignment in storage buffers. These fields are part of the GPU ABI.
    private readonly uint padding0;
    private readonly uint padding1;
    private readonly uint padding2;

    /// <summary>
    /// Initializes a new instance of the <see cref="GpuClipElement"/> struct.
    /// </summary>
    /// <param name="parentIndex">The index of the enclosing clip stream element.</param>
    /// <param name="bbox">The clip path bounds used to narrow descendant draw bounds.</param>
    public GpuClipElement(uint parentIndex, Vector4 bbox)
    {
        this.ParentIndex = parentIndex;
        this.padding0 = 0;
        this.padding1 = 0;
        this.padding2 = 0;
        this.Bbox = bbox;
    }

    /// <summary>
    /// Gets the index of the enclosing clip stream element.
    /// </summary>
    public uint ParentIndex { get; }

    /// <summary>
    /// Gets the clip path bounds used to narrow descendant draw bounds.
    /// </summary>
    public Vector4 Bbox { get; }
}

/// <summary>
/// Bounding box emitted per draw object after draw reduction.
/// </summary>
/// <remarks>
/// Stored as the raw <c>vec4&lt;f32&gt;</c> elements of the <c>draw_bboxes</c> binding
/// written by draw_leaf.wgsl.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
internal readonly struct GpuDrawBbox
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GpuDrawBbox"/> struct.
    /// </summary>
    /// <param name="bbox">The draw bounds as (x0, y0, x1, y1) in pixels.</param>
    public GpuDrawBbox(Vector4 bbox) => this.Bbox = bbox;

    /// <summary>
    /// Gets the draw bounds as (x0, y0, x1, y1) in pixels.
    /// </summary>
    public Vector4 Bbox { get; }
}

/// <summary>
/// One bin-header entry describing how many elements belong to a scheduling chunk.
/// </summary>
/// <remarks>
/// Mirrors <c>BinHeader</c> in coarse.wgsl; the headers are stored as raw word pairs
/// in the tail of the combined info/bin-data buffer.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
internal readonly struct GpuSceneBinHeader
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GpuSceneBinHeader"/> struct.
    /// </summary>
    /// <param name="elementCount">The number of draw elements recorded for the bin.</param>
    /// <param name="chunkOffset">The word offset of the bin's element list within the bin data.</param>
    public GpuSceneBinHeader(uint elementCount, uint chunkOffset)
    {
        this.ElementCount = elementCount;
        this.ChunkOffset = chunkOffset;
    }

    /// <summary>
    /// Gets the number of draw elements recorded for the bin.
    /// </summary>
    public uint ElementCount { get; }

    /// <summary>
    /// Gets the word offset of the bin's element list within the bin data.
    /// </summary>
    public uint ChunkOffset { get; }
}

/// <summary>
/// Indirect-dispatch argument buffer layout used by later scheduling passes.
/// </summary>
/// <remarks>
/// Mirrors <c>IndirectCount</c> in Shared/bump.wgsl, which has only the three count
/// words; <see cref="Pad0"/> is a C#-side trailing pad that rounds the binding size
/// up to 16 bytes and is never read by the shaders.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
internal struct GpuSceneIndirectCount
{
    /// <summary>
    /// The X workgroup count for the indirect dispatch.
    /// </summary>
    public uint CountX;

    /// <summary>
    /// The Y workgroup count for the indirect dispatch.
    /// </summary>
    public uint CountY;

    /// <summary>
    /// The Z workgroup count for the indirect dispatch.
    /// </summary>
    public uint CountZ;

    /// <summary>
    /// The unused trailing pad word; not part of the WGSL struct.
    /// </summary>
    public uint Pad0;
}

/// <summary>
/// Scene-buffer layout metadata consumed by every staged-scene shader.
/// </summary>
/// <remarks>
/// These fields are embedded inline in <c>Config</c> in Shared/config.wgsl, between
/// <c>base_color</c> and <c>lines_size</c> (<c>n_drawobj</c> through <c>style_base</c>);
/// field order must match. All offsets are in u32 words.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
internal readonly struct GpuSceneLayout
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GpuSceneLayout"/> struct.
    /// </summary>
    /// <param name="drawObjectCount">The number of draw objects in the scene.</param>
    /// <param name="pathCount">The number of paths in the scene.</param>
    /// <param name="clipCount">The number of clip begin/end records in the scene.</param>
    /// <param name="binDataStart">The start of the bump-allocated bin data within the combined info/bin-data buffer.</param>
    /// <param name="brushDataBase">The start of auxiliary brush data within the combined info/bin-data buffer.</param>
    /// <param name="ptclDynamicStart">The start of the bump-allocated region of the PTCL buffer.</param>
    /// <param name="pathTagBase">The scene-buffer offset of the path tag stream.</param>
    /// <param name="pathDataBase">The scene-buffer offset of the path data stream.</param>
    /// <param name="drawTagBase">The scene-buffer offset of the draw tag stream.</param>
    /// <param name="drawDataBase">The scene-buffer offset of the draw data stream.</param>
    /// <param name="transformBase">The scene-buffer offset of the transform stream.</param>
    /// <param name="styleBase">The scene-buffer offset of the style stream.</param>
    public GpuSceneLayout(
        uint drawObjectCount,
        uint pathCount,
        uint clipCount,
        uint binDataStart,
        uint brushDataBase,
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
        this.BrushDataBase = brushDataBase;
        this.PtclDynamicStart = ptclDynamicStart;
        this.PathTagBase = pathTagBase;
        this.PathDataBase = pathDataBase;
        this.DrawTagBase = drawTagBase;
        this.DrawDataBase = drawDataBase;
        this.TransformBase = transformBase;
        this.StyleBase = styleBase;
    }

    /// <summary>
    /// Gets the number of draw objects in the scene.
    /// </summary>
    public uint DrawObjectCount { get; }

    /// <summary>
    /// Gets the number of paths in the scene.
    /// </summary>
    public uint PathCount { get; }

    /// <summary>
    /// Gets the number of clip begin/end records in the scene.
    /// </summary>
    public uint ClipCount { get; }

    /// <summary>
    /// Gets the start of the bump-allocated bin data within the combined info/bin-data buffer.
    /// </summary>
    public uint BinDataStart { get; }

    /// <summary>
    /// Gets the start of the path-gradient edge data within the combined info/bin-data buffer.
    /// </summary>
    public uint BrushDataBase { get; }

    /// <summary>
    /// Gets the start of the bump-allocated region of the PTCL buffer.
    /// </summary>
    public uint PtclDynamicStart { get; }

    /// <summary>
    /// Gets the scene-buffer offset of the path tag stream.
    /// </summary>
    public uint PathTagBase { get; }

    /// <summary>
    /// Gets the scene-buffer offset of the path data stream.
    /// </summary>
    public uint PathDataBase { get; }

    /// <summary>
    /// Gets the scene-buffer offset of the draw tag stream.
    /// </summary>
    public uint DrawTagBase { get; }

    /// <summary>
    /// Gets the scene-buffer offset of the draw data stream.
    /// </summary>
    public uint DrawDataBase { get; }

    /// <summary>
    /// Gets the scene-buffer offset of the transform stream.
    /// </summary>
    public uint TransformBase { get; }

    /// <summary>
    /// Gets the scene-buffer offset of the style stream.
    /// </summary>
    public uint StyleBase { get; }
}

/// <summary>
/// Root scene configuration block bound at slot zero for most staged-scene shaders.
/// </summary>
/// <remarks>
/// Mirrors <c>Config</c> in Shared/config.wgsl; field order and sizes must match.
/// The embedded <see cref="GpuSceneLayout"/> expands to the <c>n_drawobj</c> through
/// <c>style_base</c> fields of that struct.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
internal readonly struct GpuSceneConfig
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GpuSceneConfig"/> struct.
    /// </summary>
    /// <param name="widthInTiles">The render width in 16x16 pixel tiles.</param>
    /// <param name="heightInTiles">The render height in 16x16 pixel tiles.</param>
    /// <param name="targetWidth">The target width in pixels.</param>
    /// <param name="targetHeight">The target height in pixels.</param>
    /// <param name="chunkTileYStart">The first global tile row rendered by this attempt.</param>
    /// <param name="chunkTileHeight">The number of real tile rows rendered by this attempt.</param>
    /// <param name="baseColor">The packed RGBA8 base color applied by the fine pass.</param>
    /// <param name="layout">The scene-buffer layout metadata.</param>
    /// <param name="linesSize">The flattened line buffer capacity in elements.</param>
    /// <param name="binningSize">The bin-data scratch capacity in words.</param>
    /// <param name="pathRowsSize">The sparse path-row buffer capacity in elements.</param>
    /// <param name="tilesSize">The path-tile buffer capacity in elements.</param>
    /// <param name="segCountsSize">The segment-count buffer capacity in elements.</param>
    /// <param name="segmentsSize">The segment buffer capacity in elements.</param>
    /// <param name="blendSize">The blend-spill buffer capacity in slots.</param>
    /// <param name="ptclSize">The PTCL buffer capacity in words.</param>
    /// <param name="fineCoverageThreshold">The scene-wide aliased coverage threshold.</param>
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

    /// <summary>
    /// Gets the render width in 16x16 pixel tiles.
    /// </summary>
    public uint WidthInTiles { get; }

    /// <summary>
    /// Gets the render height in 16x16 pixel tiles.
    /// </summary>
    public uint HeightInTiles { get; }

    /// <summary>
    /// Gets the target width in pixels.
    /// </summary>
    public uint TargetWidth { get; }

    /// <summary>
    /// Gets the target height in pixels.
    /// </summary>
    public uint TargetHeight { get; }

    /// <summary>
    /// Gets the first global tile row rendered by this attempt.
    /// </summary>
    public uint ChunkTileYStart { get; }

    /// <summary>
    /// Gets the number of real tile rows rendered by this attempt.
    /// </summary>
    public uint ChunkTileHeight { get; }

    /// <summary>
    /// Gets the packed RGBA8 (MSB order) base color applied by the fine pass.
    /// </summary>
    public uint BaseColor { get; }

    /// <summary>
    /// Gets the scene-buffer layout metadata.
    /// </summary>
    public GpuSceneLayout Layout { get; }

    /// <summary>
    /// Gets the flattened line buffer capacity in elements.
    /// </summary>
    public uint LinesSize { get; }

    /// <summary>
    /// Gets the bin-data scratch capacity in words.
    /// </summary>
    public uint BinningSize { get; }

    /// <summary>
    /// Gets the sparse path-row buffer capacity.
    /// </summary>
    public uint PathRowsSize { get; }

    /// <summary>
    /// Gets the path-tile buffer capacity in elements.
    /// </summary>
    public uint TilesSize { get; }

    /// <summary>
    /// Gets the segment-count buffer capacity in elements.
    /// </summary>
    public uint SegCountsSize { get; }

    /// <summary>
    /// Gets the segment buffer capacity in elements.
    /// </summary>
    public uint SegmentsSize { get; }

    /// <summary>
    /// Gets the blend-spill buffer capacity in slots.
    /// </summary>
    public uint BlendSize { get; }

    /// <summary>
    /// Gets the PTCL buffer capacity in words.
    /// </summary>
    public uint PtclSize { get; }

    /// <summary>
    /// Gets the scene-wide coverage threshold consumed by the aliased fine pass.
    /// </summary>
    public float FineCoverageThreshold { get; }
}

/// <summary>
/// Per-path scheduling record used after draw and clip reduction have established final bounds.
/// </summary>
/// <remarks>
/// Mirrors <c>Path</c> in Shared/tile.wgsl; field order and the 32-byte stride must match.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
internal readonly struct GpuScenePath
{
    // Path in tile.wgsl has a vec4<u32> followed by one u32 and therefore a
    // 32-byte array stride. The padding keeps reusable GPU buffer sizing exact.
    private readonly uint padding0;
    private readonly uint padding1;
    private readonly uint padding2;

    /// <summary>
    /// Initializes a new instance of the <see cref="GpuScenePath"/> struct.
    /// </summary>
    /// <param name="bboxMinX">The minimum x of the path bounds in tiles.</param>
    /// <param name="bboxMinY">The minimum y of the path bounds in tiles.</param>
    /// <param name="bboxMaxX">The maximum x of the path bounds in tiles.</param>
    /// <param name="bboxMaxY">The maximum y of the path bounds in tiles.</param>
    /// <param name="rowOffset">The first sparse row record owned by the path.</param>
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

    /// <summary>
    /// Gets the minimum x of the path bounds in tiles.
    /// </summary>
    public uint BboxMinX { get; }

    /// <summary>
    /// Gets the minimum y of the path bounds in tiles.
    /// </summary>
    public uint BboxMinY { get; }

    /// <summary>
    /// Gets the maximum x of the path bounds in tiles.
    /// </summary>
    public uint BboxMaxX { get; }

    /// <summary>
    /// Gets the maximum y of the path bounds in tiles.
    /// </summary>
    public uint BboxMaxY { get; }

    /// <summary>
    /// Gets the first sparse row record owned by this path.
    /// </summary>
    public uint RowOffset { get; }
}

/// <summary>
/// Per-path sparse row record used to allocate tiles only for the x-span actually touched on one tile row.
/// </summary>
/// <remarks>
/// Mirrors <c>PathRow</c> (and its <c>AtomicPathRow</c> view) in Shared/tile.wgsl;
/// field order must match.
/// </remarks>
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
/// <remarks>
/// Mirrors <c>LineSoup</c> in Shared/segment.wgsl; field order and the 24-byte stride must match.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
internal readonly struct GpuSceneLine
{
    // LineSoup in segment.wgsl has a u32 followed by vec2<f32> values. WGSL
    // aligns the first vec2 to offset 8, so this field is part of the buffer ABI.
    private readonly uint padding0;

    /// <summary>
    /// Initializes a new instance of the <see cref="GpuSceneLine"/> struct.
    /// </summary>
    /// <param name="pathIndex">The index of the path that produced the line.</param>
    /// <param name="point0">The line start point in pixels.</param>
    /// <param name="point1">The line end point in pixels.</param>
    public GpuSceneLine(uint pathIndex, Vector2 point0, Vector2 point1)
    {
        this.PathIndex = pathIndex;
        this.padding0 = 0;
        this.Point0 = point0;
        this.Point1 = point1;
    }

    /// <summary>
    /// Gets the index of the path that produced this line.
    /// </summary>
    public uint PathIndex { get; }

    /// <summary>
    /// Gets the line start point in pixels.
    /// </summary>
    public Vector2 Point0 { get; }

    /// <summary>
    /// Gets the line end point in pixels.
    /// </summary>
    public Vector2 Point1 { get; }
}

/// <summary>
/// Per-tile path record containing the backdrop and either a segment count or a segment-list index.
/// </summary>
/// <remarks>
/// Mirrors <c>Tile</c> in Shared/tile.wgsl; field order must match.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
internal struct GpuPathTile
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GpuPathTile"/> struct.
    /// </summary>
    /// <param name="backdrop">The winding number carried into the tile's left edge.</param>
    /// <param name="segmentCountOrIndex">The segment count, or the bit-inverted segment-list index after coarse rasterization.</param>
    public GpuPathTile(int backdrop, uint segmentCountOrIndex)
    {
        this.Backdrop = backdrop;
        this.SegmentCountOrIndex = segmentCountOrIndex;
    }

    /// <summary>
    /// The winding number carried into the tile's left edge.
    /// </summary>
    public int Backdrop;

    /// <summary>
    /// The segment count up to coarse rasterization, then the bit-inverted segment-list
    /// index; the inversion lets path tiling detect whether the tile was allocated.
    /// </summary>
    public uint SegmentCountOrIndex;
}

/// <summary>
/// Per-line segment-count record emitted by the path-count stage.
/// </summary>
/// <remarks>
/// Mirrors <c>SegmentCount</c> in Shared/segment.wgsl; field order must match.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
internal readonly struct GpuSegmentCount
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GpuSegmentCount"/> struct.
    /// </summary>
    /// <param name="lineIndex">The index of the source line in the flattened line buffer.</param>
    /// <param name="counts">The packed segment indices; see <see cref="Counts"/>.</param>
    public GpuSegmentCount(uint lineIndex, uint counts)
    {
        this.LineIndex = lineIndex;
        this.Counts = counts;
    }

    /// <summary>
    /// Gets the index of the source line in the flattened line buffer.
    /// </summary>
    public uint LineIndex { get; }

    /// <summary>
    /// Gets two packed counts: the low 16 bits index the segment within its line and
    /// the high 16 bits index the segment within its segment slice.
    /// </summary>
    public uint Counts { get; }
}

/// <summary>
/// Final per-segment record consumed by the fine rasterization stage.
/// </summary>
/// <remarks>
/// Mirrors <c>Segment</c> in Shared/segment.wgsl; field order and the 24-byte stride must match.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
internal readonly struct GpuPathSegment
{
    // Segment in segment.wgsl has two vec2<f32> values followed by one f32 and
    // a 24-byte array stride. The final slot preserves that stride from C#.
    private readonly float padding0;

    /// <summary>
    /// Initializes a new instance of the <see cref="GpuPathSegment"/> struct.
    /// </summary>
    /// <param name="point0">The segment start point relative to the tile origin.</param>
    /// <param name="point1">The segment end point relative to the tile origin.</param>
    /// <param name="yEdge">The tile-relative y at which the segment meets the tile's left edge, or the 1e9 sentinel.</param>
    public GpuPathSegment(Vector2 point0, Vector2 point1, float yEdge)
    {
        this.Point0 = point0;
        this.Point1 = point1;
        this.YEdge = yEdge;
        this.padding0 = 0;
    }

    /// <summary>
    /// Gets the segment start point relative to the tile origin.
    /// </summary>
    public Vector2 Point0 { get; }

    /// <summary>
    /// Gets the segment end point relative to the tile origin.
    /// </summary>
    public Vector2 Point1 { get; }

    /// <summary>
    /// Gets the tile-relative y at which the segment meets the tile's left edge, or 1e9
    /// if it does not; fine accumulates the implied vertical edge there to keep winding
    /// consistent after clipping.
    /// </summary>
    public float YEdge { get; }
}

#pragma warning restore SA1201
