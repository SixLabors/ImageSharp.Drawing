// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Silk.NET.WebGPU;
using WgpuBuffer = Silk.NET.WebGPU.Buffer;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends.Brushes;

/// <summary>
/// GPU brush composer for solid-color brushes.
/// </summary>
internal sealed unsafe class WebGPUSolidBrushComposer : IWebGPUBrushComposer
{
    private const string PipelineKey = "solid-brush";
    private readonly Vector4 color;
    private BindGroup* cachedBindGroup;
    private nint cachedCoverageView;
    private nint cachedDestinationBuffer;
    private nuint cachedInstanceBytes;
    private BindGroupLayout* bindGroupLayout;
    private ComputePipeline* computePipeline;

    public WebGPUSolidBrushComposer(SolidBrush brush)
    {
        Guard.NotNull(brush, nameof(brush));
        this.color = brush.Color.ToScaledVector4();
    }

    /// <inheritdoc />
    public nuint InstanceDataSizeInBytes => (nuint)Unsafe.SizeOf<SolidBrushInstanceData>();

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
            SolidBrushCompositeComputeShader.Code,
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

    /// <inheritdoc />
    public void WriteInstanceData(in WebGPUCompositeCommonParameters common, Span<byte> destination)
    {
        SolidBrushInstanceData data = new()
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
            Padding0 = 0,
            SolidBrushColor = this.color
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

        BindGroupEntry* bindGroupEntries = stackalloc BindGroupEntry[3];
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
            Buffer = destinationPixelsBuffer,
            Size = destinationPixelsByteSize
        };

        BindGroupDescriptor bindGroupDescriptor = new()
        {
            Layout = this.bindGroupLayout,
            EntryCount = 3,
            Entries = bindGroupEntries
        };

        BindGroup* bindGroup = flushContext.Api.DeviceCreateBindGroup(flushContext.Device, in bindGroupDescriptor);
        if (bindGroup is null)
        {
            throw new InvalidOperationException("Failed to create solid brush bind group.");
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
        BindGroupLayoutEntry* layoutEntries = stackalloc BindGroupLayoutEntry[3];
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
            Buffer = new BufferBindingLayout
            {
                Type = BufferBindingType.Storage,
                HasDynamicOffset = false,
                MinBindingSize = 0
            }
        };

        BindGroupLayoutDescriptor layoutDescriptor = new()
        {
            EntryCount = 3,
            Entries = layoutEntries
        };

        layout = api.DeviceCreateBindGroupLayout(device, in layoutDescriptor);
        if (layout is null)
        {
            error = "Failed to create solid composite bind group layout.";
            return false;
        }

        error = null;
        return true;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SolidBrushInstanceData
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
        public int Padding0;
        public Vector4 SolidBrushColor;
    }
}
