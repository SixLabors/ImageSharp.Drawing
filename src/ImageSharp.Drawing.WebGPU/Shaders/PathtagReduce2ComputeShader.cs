// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using Silk.NET.WebGPU;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// GPU stage that performs the second-level reduction for the large pathtag scan variant,
/// reducing the first-level partials from pathtag-reduce by another factor of the workgroup
/// size for consumption by pathtag-scan1. Wraps <c>pathtag_reduce2.wgsl</c>.
/// </summary>
internal static unsafe class PathtagReduce2ComputeShader
{
    /// <summary>
    /// Gets the generated WGSL source bytes for the pathtag-reduce2 stage.
    /// </summary>
    public static ReadOnlySpan<byte> ShaderCode => GeneratedWgslShaderSources.PathtagReduce2Code;

    /// <summary>
    /// Gets the WGSL entry point used by this shader.
    /// </summary>
    public static ReadOnlySpan<byte> EntryPoint => "main\0"u8;

    /// <summary>
    /// Creates the bind-group layout required by the pathtag-reduce2 stage.
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
        // Bindings match pathtag_reduce2.wgsl:
        //   0 reduced_in (read-only first-level partials from pathtag_reduce)
        //   1 reduced (read-write; one second-level TagMonoid aggregate written per workgroup)
        BindGroupLayoutEntry* entries = stackalloc BindGroupLayoutEntry[2];
        entries[0] = SceneShaderBindingLayoutHelper.CreateStorageEntry(0, BufferBindingType.ReadOnlyStorage);
        entries[1] = SceneShaderBindingLayoutHelper.CreateStorageEntry(1, BufferBindingType.Storage);

        BindGroupLayoutDescriptor descriptor = new()
        {
            EntryCount = 2,
            Entries = entries
        };

        layout = api.DeviceCreateBindGroupLayout(device, in descriptor);
        if (layout is null)
        {
            error = "Failed to create the pathtag-reduce2 bind-group layout.";
            return false;
        }

        error = null;
        return true;
    }
}
