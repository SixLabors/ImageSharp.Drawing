// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Runtime.CompilerServices;
using Silk.NET.WebGPU;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

internal static unsafe class PathtagReduceComputeShader
{
    public static ReadOnlySpan<byte> ShaderCode => GeneratedWgslShaderSources.PathtagReduceCode;

    public static ReadOnlySpan<byte> EntryPoint => "main\0"u8;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint GetDispatchX(uint pathTagWords)
        => (pathTagWords + 255U) / 256U;

    public static bool TryCreateBindGroupLayout(
        WebGPU api,
        Device* device,
        out BindGroupLayout* layout,
        out string? error)
    {
        BindGroupLayoutEntry* entries = stackalloc BindGroupLayoutEntry[3];
        entries[0] = SceneShaderBindingLayoutHelper.CreateUniformEntry(0, (nuint)sizeof(GpuSceneConfig));
        entries[1] = SceneShaderBindingLayoutHelper.CreateStorageEntry(1, BufferBindingType.ReadOnlyStorage);
        entries[2] = SceneShaderBindingLayoutHelper.CreateStorageEntry(2, BufferBindingType.Storage);

        BindGroupLayoutDescriptor descriptor = new()
        {
            EntryCount = 3,
            Entries = entries
        };

        layout = api.DeviceCreateBindGroupLayout(device, in descriptor);
        if (layout is null)
        {
            error = "Failed to create the pathtag-reduce bind-group layout.";
            return false;
        }

        error = null;
        return true;
    }
}
