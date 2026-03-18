// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Runtime.CompilerServices;
using Silk.NET.WebGPU;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// GPU stage that emits per-tile PTCL from bin-compacted draw-object work, matching Vello's coarse stage shape.
/// </summary>
internal static unsafe class CoarseComputeShader
{
    public static ReadOnlySpan<byte> ShaderCode => GeneratedWgslShaderSources.CoarseCode;

    public static ReadOnlySpan<byte> EntryPoint => "main\0"u8;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint GetDispatchX(uint widthInBins) => widthInBins;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint GetDispatchY(uint heightInBins) => heightInBins;

    public static bool TryCreateBindGroupLayout(
        WebGPU api,
        Device* device,
        out BindGroupLayout* layout,
        out string? error)
    {
        BindGroupLayoutEntry* entries = stackalloc BindGroupLayoutEntry[9];
        entries[0] = CreateUniformEntry(0, (nuint)sizeof(GpuSceneConfig));
        entries[1] = CreateStorageEntry(1, BufferBindingType.ReadOnlyStorage, 0);
        entries[2] = CreateStorageEntry(2, BufferBindingType.ReadOnlyStorage, 0);
        entries[3] = CreateStorageEntry(3, BufferBindingType.ReadOnlyStorage, 0);
        entries[4] = CreateStorageEntry(4, BufferBindingType.ReadOnlyStorage, 0);
        entries[5] = CreateStorageEntry(5, BufferBindingType.ReadOnlyStorage, 0);
        entries[6] = CreateStorageEntry(6, BufferBindingType.Storage, 0);
        entries[7] = CreateStorageEntry(7, BufferBindingType.Storage, (nuint)sizeof(GpuSceneBumpAllocators));
        entries[8] = CreateStorageEntry(8, BufferBindingType.Storage, 0);

        BindGroupLayoutDescriptor descriptor = new()
        {
            EntryCount = 9,
            Entries = entries
        };

        layout = api.DeviceCreateBindGroupLayout(device, in descriptor);
        if (layout is null)
        {
            error = "Failed to create the WebGPU coarse bind-group layout.";
            return false;
        }

        error = null;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static BindGroupLayoutEntry CreateStorageEntry(uint binding, BufferBindingType type, nuint minBindingSize)
        => new()
        {
            Binding = binding,
            Visibility = ShaderStage.Compute,
            Buffer = new BufferBindingLayout
            {
                Type = type,
                HasDynamicOffset = false,
                MinBindingSize = minBindingSize
            }
        };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static BindGroupLayoutEntry CreateUniformEntry(uint binding, nuint minBindingSize)
        => new()
        {
            Binding = binding,
            Visibility = ShaderStage.Compute,
            Buffer = new BufferBindingLayout
            {
                Type = BufferBindingType.Uniform,
                HasDynamicOffset = false,
                MinBindingSize = minBindingSize
            }
        };
}
