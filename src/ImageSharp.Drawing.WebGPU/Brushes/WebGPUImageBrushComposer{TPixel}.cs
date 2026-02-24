// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Silk.NET.WebGPU;
using SixLabors.ImageSharp.PixelFormats;
using WgpuBuffer = Silk.NET.WebGPU.Buffer;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends.Brushes;

/// <summary>
/// GPU brush composer for image brushes.
/// </summary>
/// <typeparam name="TPixel">The pixel type used by the target composition surface.</typeparam>
internal sealed unsafe class WebGPUImageBrushComposer<TPixel> : IWebGPUBrushComposer
    where TPixel : unmanaged, IPixel<TPixel>
{
    private const string PipelineKey = "image-brush";
    private readonly TextureView* sourceTextureView;
    private readonly Rectangle sourceRegion;
    private readonly int imageBrushOriginX;
    private readonly int imageBrushOriginY;
    private BindGroup* cachedBindGroup;
    private nint cachedCoverageView;
    private nint cachedDestinationBuffer;
    private nuint cachedInstanceBytes;
    private BindGroupLayout* bindGroupLayout;
    private ComputePipeline* computePipeline;

    private WebGPUImageBrushComposer(
        TextureView* sourceTextureView,
        in Rectangle sourceRegion,
        int imageBrushOriginX,
        int imageBrushOriginY)
    {
        this.sourceTextureView = sourceTextureView;
        this.sourceRegion = sourceRegion;
        this.imageBrushOriginX = imageBrushOriginX;
        this.imageBrushOriginY = imageBrushOriginY;
    }

    /// <inheritdoc />
    public nuint InstanceDataSizeInBytes => (nuint)Unsafe.SizeOf<ImageBrushInstanceData>();

    /// <inheritdoc />
    public bool TryGetOrCreatePipeline(
        WebGPUFlushContext flushContext,
        out ComputePipeline* pipeline,
        out string? error)
    {
        if (this.computePipeline is not null)
        {
            pipeline = this.computePipeline;
            error = null;
            return true;
        }

        bool success = flushContext.DeviceState.TryGetOrCreateCompositeComputePipeline(
            PipelineKey,
            ImageBrushCompositeComputeShader.Code,
            TryCreateBindGroupLayout,
            out this.bindGroupLayout,
            out pipeline,
            out error);

        if (success)
        {
            this.computePipeline = pipeline;
        }

        return success;
    }

    /// <summary>
    /// Creates a composer for one image brush command.
    /// </summary>
    public static WebGPUImageBrushComposer<TPixel> Create(
        WebGPUFlushContext flushContext,
        ImageBrush imageBrush,
        Rectangle brushBounds)
    {
        Guard.NotNull(flushContext, nameof(flushContext));
        Guard.NotNull(imageBrush, nameof(imageBrush));

        // Invariant: image brushes have already been normalized for the target TPixel path.
        Image<TPixel> sourceImage = (Image<TPixel>)imageBrush.SourceImage;

        Rectangle sourceRegion = Rectangle.Intersect(sourceImage.Bounds, (Rectangle)imageBrush.SourceRegion);
        if (!flushContext.TryGetOrCreateSourceTextureView(sourceImage, out TextureView* sourceView))
        {
            throw new InvalidOperationException("Failed to acquire source texture view for image brush composition.");
        }

        int imageBrushOriginX = checked(brushBounds.Left + imageBrush.Offset.X - flushContext.TargetBounds.X);
        int imageBrushOriginY = checked(brushBounds.Top + imageBrush.Offset.Y - flushContext.TargetBounds.Y);
        return new WebGPUImageBrushComposer<TPixel>(sourceView, in sourceRegion, imageBrushOriginX, imageBrushOriginY);
    }

    /// <inheritdoc />
    public void WriteInstanceData(in WebGPUCompositeCommonParameters common, Span<byte> destination)
    {
        ImageBrushInstanceData data = new()
        {
            SourceOffsetX = common.SourceOffsetX,
            SourceOffsetY = common.SourceOffsetY,
            DestinationX = common.DestinationX,
            DestinationY = common.DestinationY,
            DestinationWidth = common.DestinationWidth,
            DestinationHeight = common.DestinationHeight,
            DestinationBufferWidth = common.DestinationBufferWidth,
            DestinationBufferHeight = common.DestinationBufferHeight,
            BlendPercentage = common.BlendPercentage,
            ColorBlendingMode = common.ColorBlendingMode,
            AlphaCompositionMode = common.AlphaCompositionMode,
            CommonPadding0 = 0,
            ImageRegionX = this.sourceRegion.X,
            ImageRegionY = this.sourceRegion.Y,
            ImageRegionWidth = this.sourceRegion.Width,
            ImageRegionHeight = this.sourceRegion.Height,
            ImageBrushOriginX = this.imageBrushOriginX - common.DestinationBufferOriginX,
            ImageBrushOriginY = this.imageBrushOriginY - common.DestinationBufferOriginY,
            Padding0 = 0,
            Padding1 = 0
        };

        MemoryMarshal.Write(destination, in data);
    }

    /// <inheritdoc />
    public BindGroup* CreateBindGroup(
        WebGPUFlushContext flushContext,
        TextureView* coverageView,
        WgpuBuffer* destinationPixelsBuffer,
        nuint destinationPixelsByteSize,
        nuint instanceOffset,
        nuint instanceBytes)
    {
        _ = instanceOffset;
        nint coverageKey = (nint)coverageView;
        nint destinationBufferKey = (nint)destinationPixelsBuffer;
        if (this.cachedBindGroup is not null &&
            this.cachedCoverageView == coverageKey &&
            this.cachedDestinationBuffer == destinationBufferKey &&
            this.cachedInstanceBytes == instanceBytes)
        {
            return this.cachedBindGroup;
        }

        BindGroupEntry* bindGroupEntries = stackalloc BindGroupEntry[4];
        bindGroupEntries[0] = new BindGroupEntry
        {
            Binding = 0,
            TextureView = coverageView
        };
        bindGroupEntries[1] = new BindGroupEntry
        {
            Binding = 1,
            Buffer = flushContext.InstanceBuffer,
            Offset = 0,
            Size = instanceBytes
        };
        bindGroupEntries[2] = new BindGroupEntry
        {
            Binding = 2,
            TextureView = this.sourceTextureView
        };
        bindGroupEntries[3] = new BindGroupEntry
        {
            Binding = 3,
            Buffer = destinationPixelsBuffer,
            Size = destinationPixelsByteSize
        };

        BindGroupDescriptor bindGroupDescriptor = new()
        {
            Layout = this.bindGroupLayout,
            EntryCount = 4,
            Entries = bindGroupEntries
        };

        BindGroup* bindGroup = flushContext.Api.DeviceCreateBindGroup(flushContext.Device, in bindGroupDescriptor);
        if (bindGroup is null)
        {
            throw new InvalidOperationException("Failed to create image brush bind group.");
        }

        flushContext.TrackBindGroup(bindGroup);
        this.cachedBindGroup = bindGroup;
        this.cachedCoverageView = coverageKey;
        this.cachedDestinationBuffer = destinationBufferKey;
        this.cachedInstanceBytes = instanceBytes;
        return bindGroup;
    }

    private static bool TryCreateBindGroupLayout(
        WebGPU api,
        Device* device,
        out BindGroupLayout* layout,
        out string? error)
    {
        BindGroupLayoutEntry* layoutEntries = stackalloc BindGroupLayoutEntry[4];
        layoutEntries[0] = new BindGroupLayoutEntry
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
        layoutEntries[1] = new BindGroupLayoutEntry
        {
            Binding = 1,
            Visibility = ShaderStage.Compute,
            Buffer = new BufferBindingLayout
            {
                Type = BufferBindingType.ReadOnlyStorage,
                HasDynamicOffset = true,
                MinBindingSize = 0
            }
        };
        layoutEntries[2] = new BindGroupLayoutEntry
        {
            Binding = 2,
            Visibility = ShaderStage.Compute,
            Texture = new TextureBindingLayout
            {
                SampleType = TextureSampleType.Float,
                ViewDimension = TextureViewDimension.Dimension2D,
                Multisampled = false
            }
        };
        layoutEntries[3] = new BindGroupLayoutEntry
        {
            Binding = 3,
            Visibility = ShaderStage.Compute,
            Buffer = new BufferBindingLayout
            {
                Type = BufferBindingType.Storage,
                HasDynamicOffset = false,
                MinBindingSize = 0
            }
        };

        BindGroupLayoutDescriptor layoutDescriptor = new()
        {
            EntryCount = 4,
            Entries = layoutEntries
        };

        layout = api.DeviceCreateBindGroupLayout(device, in layoutDescriptor);
        if (layout is null)
        {
            error = "Failed to create image composite bind group layout.";
            return false;
        }

        error = null;
        return true;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ImageBrushInstanceData
    {
        public int SourceOffsetX;
        public int SourceOffsetY;
        public int DestinationX;
        public int DestinationY;
        public int DestinationWidth;
        public int DestinationHeight;
        public int DestinationBufferWidth;
        public int DestinationBufferHeight;
        public float BlendPercentage;
        public int ColorBlendingMode;
        public int AlphaCompositionMode;
        public int CommonPadding0;
        public int ImageRegionX;
        public int ImageRegionY;
        public int ImageRegionWidth;
        public int ImageRegionHeight;
        public int ImageBrushOriginX;
        public int ImageBrushOriginY;
        public int Padding0;
        public int Padding1;
    }
}
