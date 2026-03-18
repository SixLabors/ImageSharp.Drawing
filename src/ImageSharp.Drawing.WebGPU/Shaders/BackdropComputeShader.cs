// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Runtime.CompilerServices;
using Silk.NET.WebGPU;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// GPU stage that prefix-sums dynamically allocated per-path tile backdrops.
/// </summary>
internal static unsafe class BackdropComputeShader
{
    public static ReadOnlySpan<byte> ShaderCode => GeneratedWgslShaderSources.BackdropDynCode;

    public static ReadOnlySpan<byte> EntryPoint => "main\0"u8;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint GetDispatchX(uint heightInTiles)
        => heightInTiles;

    public static bool TryCreateBindGroupLayout(
        WebGPU api,
        Device* device,
        out BindGroupLayout* layout,
        out string? error)
    {
        BindGroupLayoutEntry* entries = stackalloc BindGroupLayoutEntry[4];
        entries[0] = SceneShaderBindingLayoutHelper.CreateUniformEntry(0, (nuint)sizeof(GpuSceneConfig));
        entries[1] = SceneShaderBindingLayoutHelper.CreateStorageEntry(1, BufferBindingType.Storage, (nuint)sizeof(GpuSceneBumpAllocators));
        entries[2] = SceneShaderBindingLayoutHelper.CreateStorageEntry(2, BufferBindingType.ReadOnlyStorage);
        entries[3] = SceneShaderBindingLayoutHelper.CreateStorageEntry(3, BufferBindingType.Storage);

        BindGroupLayoutDescriptor descriptor = new()
        {
            EntryCount = 4,
            Entries = entries
        };

        layout = api.DeviceCreateBindGroupLayout(device, in descriptor);
        if (layout is null)
        {
            error = "Failed to create the WebGPU backdrop bind-group layout.";
            return false;
        }

        error = null;
        return true;
    }
}
