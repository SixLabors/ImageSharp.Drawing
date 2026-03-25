// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using Silk.NET.WebGPU;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// GPU stage that prefix-scans packed path tags, with both normal and small generated variants.
/// </summary>
internal static unsafe class PathtagScanComputeShader
{
    /// <summary>
    /// Gets the generated WGSL source bytes for the normal pathtag-scan variant.
    /// </summary>
    public static ReadOnlySpan<byte> ShaderCode => GeneratedWgslShaderSources.PathtagScanCode;

    /// <summary>
    /// Gets the generated WGSL source bytes for the small pathtag-scan variant.
    /// </summary>
    public static ReadOnlySpan<byte> SmallShaderCode => GeneratedWgslShaderSources.PathtagScanSmallCode;

    /// <summary>
    /// Gets the WGSL entry point shared by both pathtag-scan variants.
    /// </summary>
    public static ReadOnlySpan<byte> EntryPoint => "main\0"u8;

    /// <summary>
    /// Creates the bind-group layout required by both pathtag-scan variants.
    /// </summary>
    public static bool TryCreateBindGroupLayout(
        WebGPU api,
        Device* device,
        out BindGroupLayout* layout,
        out string? error)
    {
        BindGroupLayoutEntry* entries = stackalloc BindGroupLayoutEntry[4];
        entries[0] = SceneShaderBindingLayoutHelper.CreateUniformEntry(0, (nuint)sizeof(GpuSceneConfig));
        entries[1] = SceneShaderBindingLayoutHelper.CreateStorageEntry(1, BufferBindingType.ReadOnlyStorage);
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
            error = "Failed to create the pathtag-scan bind-group layout.";
            return false;
        }

        error = null;
        return true;
    }
}
