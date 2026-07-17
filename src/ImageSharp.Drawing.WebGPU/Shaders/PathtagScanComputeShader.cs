// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// GPU stage that finishes the pathtag scan, writing an exclusive prefix TagMonoid per 4-byte
/// tag word for flatten to locate path data in the scene stream. Two generated variants share
/// this wrapper: the small variant scans the pathtag-reduce partials directly, while the large
/// variant reads per-workgroup prefixes precomputed by pathtag-scan1. Wraps
/// <c>pathtag_scan.wgsl</c>.
/// </summary>
internal static unsafe class PathtagScanComputeShader
{
    /// <summary>
    /// Gets the generated WGSL source bytes for the large pathtag-scan variant, which reads
    /// per-workgroup exclusive prefixes precomputed by pathtag-scan1.
    /// </summary>
    public static ReadOnlySpan<byte> ShaderCode => GeneratedWgslShaderSources.PathtagScanCode;

    /// <summary>
    /// Gets the generated WGSL source bytes for the small pathtag-scan variant, which scans the
    /// per-workgroup totals from pathtag-reduce in shared memory.
    /// </summary>
    public static ReadOnlySpan<byte> SmallShaderCode => GeneratedWgslShaderSources.PathtagScanSmallCode;

    /// <summary>
    /// Gets the WGSL entry point shared by both pathtag-scan variants.
    /// </summary>
    public static ReadOnlySpan<byte> EntryPoint => "main\0"u8;

    /// <summary>
    /// Creates the bind-group layout required by both pathtag-scan variants.
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
        // Bindings match pathtag_scan.wgsl (both variants):
        //   0 config uniform
        //   1 scene (read-only tag words)
        //   2 reduced (read-only; per-workgroup totals for the small variant, exclusive prefixes for the large one)
        //   3 tag_monoids (read-write; exclusive prefix written per tag word)
        BindGroupLayoutEntry* entries = stackalloc BindGroupLayoutEntry[4];
        entries[0] = SceneShaderBindingLayoutHelper.CreateUniformEntry(0, (nuint)sizeof(GpuSceneConfig));
        entries[1] = SceneShaderBindingLayoutHelper.CreateStorageEntry(1, BufferBindingType.ReadOnlyStorage);
        entries[2] = SceneShaderBindingLayoutHelper.CreateStorageEntry(2, BufferBindingType.ReadOnlyStorage);
        entries[3] = SceneShaderBindingLayoutHelper.CreateStorageEntry(3, BufferBindingType.Storage);

        BindGroupLayoutDescriptor descriptor = new()
        {
            entryCount = 4,
            entries = entries
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
