// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using Silk.NET.WebGPU;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// GPU stage that runs the middle level of the large pathtag scan: it combines the two levels
/// of reduction partials into an exclusive prefix per first-level partial, which the large
/// pathtag-scan variant consumes as its per-workgroup carry-in. Wraps <c>pathtag_scan1.wgsl</c>.
/// </summary>
internal static unsafe class PathtagScan1ComputeShader
{
    /// <summary>
    /// Gets the generated WGSL source bytes for the pathtag-scan1 stage.
    /// </summary>
    public static ReadOnlySpan<byte> ShaderCode => GeneratedWgslShaderSources.PathtagScan1Code;

    /// <summary>
    /// Gets the WGSL entry point used by this shader.
    /// </summary>
    public static ReadOnlySpan<byte> EntryPoint => "main\0"u8;

    /// <summary>
    /// Creates the bind-group layout required by the pathtag-scan1 stage.
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
        // Bindings match pathtag_scan1.wgsl:
        //   0 reduced (read-only first-level partials from pathtag_reduce)
        //   1 reduced2 (read-only second-level partials from pathtag_reduce2)
        //   2 tag_monoids (read-write; one exclusive prefix written per first-level partial)
        BindGroupLayoutEntry* entries = stackalloc BindGroupLayoutEntry[3];
        entries[0] = SceneShaderBindingLayoutHelper.CreateStorageEntry(0, BufferBindingType.ReadOnlyStorage);
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
            error = "Failed to create the pathtag-scan1 bind-group layout.";
            return false;
        }

        error = null;
        return true;
    }
}
