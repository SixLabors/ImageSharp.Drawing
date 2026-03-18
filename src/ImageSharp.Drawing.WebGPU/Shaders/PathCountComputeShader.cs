// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Runtime.CompilerServices;
using Silk.NET.WebGPU;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// GPU stage that counts traversed tile slices from scene lines and writes segment-count records.
/// </summary>
internal static unsafe class PathCountComputeShader
{
    public static ReadOnlySpan<byte> ShaderCode => GeneratedWgslShaderSources.PathCountCode;

    public static ReadOnlySpan<byte> EntryPoint => "main\0"u8;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint GetDispatchX(uint lineCount)
        => (lineCount + 255U) / 256U;

    public static bool TryCreateBindGroupLayout(
        WebGPU api,
        Device* device,
        out BindGroupLayout* layout,
        out string? error)
    {
        BindGroupLayoutEntry* entries = stackalloc BindGroupLayoutEntry[6];
        entries[0] = CreateUniformEntry(0, (nuint)sizeof(GpuSceneConfig));
        entries[1] = CreateStorageEntry(1, BufferBindingType.Storage, (nuint)sizeof(GpuSceneBumpAllocators));
        entries[2] = CreateStorageEntry(2, BufferBindingType.ReadOnlyStorage, 0);
        entries[3] = CreateStorageEntry(3, BufferBindingType.ReadOnlyStorage, 0);
        entries[4] = CreateStorageEntry(4, BufferBindingType.Storage, 0);
        entries[5] = CreateStorageEntry(5, BufferBindingType.Storage, 0);

        BindGroupLayoutDescriptor descriptor = new()
        {
            EntryCount = 6,
            Entries = entries
        };

        layout = api.DeviceCreateBindGroupLayout(device, in descriptor);
        if (layout is null)
        {
            error = "Failed to create the WebGPU path-count bind-group layout.";
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
