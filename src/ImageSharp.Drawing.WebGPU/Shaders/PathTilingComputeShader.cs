// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Runtime.CompilerServices;
using Silk.NET.WebGPU;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// GPU stage that writes final tile-relative segments from counted line slices.
/// </summary>
internal static unsafe class PathTilingComputeShader
{
    public static ReadOnlySpan<byte> ShaderCode => GeneratedWgslShaderSources.PathTilingCode;

    public static ReadOnlySpan<byte> EntryPoint => "main\0"u8;

    public static bool TryCreateBindGroupLayout(
        WebGPU api,
        Device* device,
        out BindGroupLayout* layout,
        out string? error)
    {
        BindGroupLayoutEntry* entries = stackalloc BindGroupLayoutEntry[6];
        entries[0] = SceneShaderBindingLayoutHelper.CreateStorageEntry(0, BufferBindingType.Storage, (nuint)sizeof(GpuSceneBumpAllocators));
        entries[1] = SceneShaderBindingLayoutHelper.CreateStorageEntry(1, BufferBindingType.ReadOnlyStorage);
        entries[2] = SceneShaderBindingLayoutHelper.CreateStorageEntry(2, BufferBindingType.ReadOnlyStorage);
        entries[3] = SceneShaderBindingLayoutHelper.CreateStorageEntry(3, BufferBindingType.ReadOnlyStorage);
        entries[4] = SceneShaderBindingLayoutHelper.CreateStorageEntry(4, BufferBindingType.ReadOnlyStorage);
        entries[5] = SceneShaderBindingLayoutHelper.CreateStorageEntry(5, BufferBindingType.Storage);

        BindGroupLayoutDescriptor descriptor = new()
        {
            EntryCount = 6,
            Entries = entries
        };

        layout = api.DeviceCreateBindGroupLayout(device, in descriptor);
        if (layout is null)
        {
            error = "Failed to create the WebGPU path-tiling bind-group layout.";
            return false;
        }

        error = null;
        return true;
    }
}
