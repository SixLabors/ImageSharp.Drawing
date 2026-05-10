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
    /// <summary>
    /// Gets the generated WGSL source bytes for the path-tiling stage.
    /// </summary>
    public static ReadOnlySpan<byte> ShaderCode => GeneratedWgslShaderSources.PathTilingCode;

    /// <summary>
    /// Gets the WGSL entry point used by this shader.
    /// </summary>
    public static ReadOnlySpan<byte> EntryPoint => "main\0"u8;

    /// <summary>
    /// Creates the bind-group layout required by the path-tiling stage.
    /// </summary>
    /// <param name="api">The WebGPU API facade.</param>
    /// <param name="device">The device that owns the staged-scene pipelines.</param>
    /// <param name="layout">Receives the created bind-group layout on success.</param>
    /// <param name="error">Receives the creation failure reason when layout creation fails.</param>
    /// <returns><see langword="true"/> when the bind-group layout was created successfully; otherwise, <see langword="false"/>.</returns>
    public static bool TryCreateBindGroupLayout(
        WebGPU api,
        Device* device,
        out BindGroupLayout* layout,
        out string? error)
    {
        BindGroupLayoutEntry* entries = stackalloc BindGroupLayoutEntry[7];
        entries[0] = SceneShaderBindingLayoutHelper.CreateStorageEntry(0, BufferBindingType.Storage, (nuint)sizeof(GpuSceneBumpAllocators));
        entries[1] = SceneShaderBindingLayoutHelper.CreateStorageEntry(1, BufferBindingType.ReadOnlyStorage);
        entries[2] = SceneShaderBindingLayoutHelper.CreateStorageEntry(2, BufferBindingType.ReadOnlyStorage);
        entries[3] = SceneShaderBindingLayoutHelper.CreateStorageEntry(3, BufferBindingType.ReadOnlyStorage);
        entries[4] = SceneShaderBindingLayoutHelper.CreateStorageEntry(4, BufferBindingType.ReadOnlyStorage);
        entries[5] = SceneShaderBindingLayoutHelper.CreateStorageEntry(5, BufferBindingType.ReadOnlyStorage);
        entries[6] = SceneShaderBindingLayoutHelper.CreateStorageEntry(6, BufferBindingType.Storage);

        BindGroupLayoutDescriptor descriptor = new()
        {
            EntryCount = 7,
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
