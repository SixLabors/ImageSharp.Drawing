// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Silk.NET.WebGPU;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends.Brushes;

/// <summary>
/// GPU brush composer for solid-color brushes.
/// </summary>
internal sealed unsafe class WebGPUSolidBrushComposer : IWebGPUBrushComposer
{
    private const string PipelineKey = "solid-brush";
    private readonly Vector4 color;
    private BindGroupLayout* bindGroupLayout;

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
        out RenderPipeline* pipeline,
        out string? error)
        => flushContext.DeviceState.TryGetOrCreateCompositePipeline(
            PipelineKey,
            SolidBrushCompositeShader.Code,
            TryCreateBindGroupLayout,
            flushContext.TextureFormat,
            out this.bindGroupLayout,
            out pipeline,
            out error);

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
            TargetWidth = common.TargetWidth,
            TargetHeight = common.TargetHeight,
            BlendData = new Vector4(common.BlendPercentage, 0, 0, 0),
            SolidBrushColor = this.color
        };

        MemoryMarshal.Write(destination, in data);
    }

    /// <inheritdoc />
    public BindGroup* CreateBindGroup(
        WebGPUFlushContext flushContext,
        TextureView* coverageView,
        nuint instanceOffset,
        nuint instanceBytes)
    {
        BindGroupEntry* bindGroupEntries = stackalloc BindGroupEntry[2];
        bindGroupEntries[0] = new BindGroupEntry
        {
            Binding = 0,
            TextureView = coverageView
        };
        bindGroupEntries[1] = new BindGroupEntry
        {
            Binding = 1,
            Buffer = flushContext.InstanceBuffer,
            Offset = instanceOffset,
            Size = instanceBytes
        };

        BindGroupDescriptor bindGroupDescriptor = new()
        {
            Layout = this.bindGroupLayout,
            EntryCount = 2,
            Entries = bindGroupEntries
        };

        BindGroup* bindGroup = flushContext.Api.DeviceCreateBindGroup(flushContext.Device, in bindGroupDescriptor);
        if (bindGroup is null)
        {
            throw new InvalidOperationException("Failed to create solid brush bind group.");
        }

        return bindGroup;
    }

    private static bool TryCreateBindGroupLayout(
        WebGPU api,
        Device* device,
        out BindGroupLayout* layout,
        out string? error)
    {
        BindGroupLayoutEntry* layoutEntries = stackalloc BindGroupLayoutEntry[2];
        layoutEntries[0] = new BindGroupLayoutEntry
        {
            Binding = 0,
            Visibility = ShaderStage.Fragment,
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
            Visibility = ShaderStage.Vertex | ShaderStage.Fragment,
            Buffer = new BufferBindingLayout
            {
                Type = BufferBindingType.ReadOnlyStorage,
                HasDynamicOffset = false,
                MinBindingSize = 0
            }
        };

        BindGroupLayoutDescriptor layoutDescriptor = new()
        {
            EntryCount = 2,
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
        public int TargetWidth;
        public int TargetHeight;
        public Vector4 BlendData;
        public Vector4 SolidBrushColor;
    }
}
