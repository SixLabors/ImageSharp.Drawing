// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Runtime.CompilerServices;
using Silk.NET.WebGPU;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// GPU stage that initializes exact per-tile edge counts before ordered work counting.
/// </summary>
internal static unsafe class PathCountSetupComputeShader
{
    public static ReadOnlySpan<byte> ShaderCode => GeneratedWgslShaderSources.PathCountSetupCode;

    public static ReadOnlySpan<byte> EntryPoint => "main\0"u8;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint GetDispatchX() => 1;

    public static bool TryCreateBindGroupLayout(
        WebGPU api,
        Device* device,
        out BindGroupLayout* layout,
        out string? error)
    {
        BindGroupLayoutEntry* entries = stackalloc BindGroupLayoutEntry[2];
        entries[0] = SceneShaderBindingLayoutHelper.CreateStorageEntry(0, BufferBindingType.Storage, (nuint)sizeof(GpuSceneBumpAllocators));
        entries[1] = SceneShaderBindingLayoutHelper.CreateStorageEntry(1, BufferBindingType.Storage, (nuint)sizeof(GpuSceneIndirectCount));

        BindGroupLayoutDescriptor descriptor = new()
        {
            EntryCount = 2,
            Entries = entries
        };

        layout = api.DeviceCreateBindGroupLayout(device, in descriptor);
        if (layout is null)
        {
            error = "Failed to create the WebGPU path-count-setup bind-group layout.";
            return false;
        }

        error = null;
        return true;
    }
}
