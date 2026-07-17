// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Runtime.CompilerServices;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// GPU stage that runs the first level of the multi-level prefix scan over the packed path-tag
/// stream, reducing each workgroup's slice of tag words to a single TagMonoid that the scan
/// stages later turn into exclusive prefixes. Wraps <c>pathtag_reduce.wgsl</c>.
/// </summary>
internal static unsafe class PathtagReduceComputeShader
{
    /// <summary>
    /// Gets the generated WGSL source bytes for the pathtag-reduce stage.
    /// </summary>
    public static ReadOnlySpan<byte> ShaderCode => GeneratedWgslShaderSources.PathtagReduceCode;

    /// <summary>
    /// Gets the WGSL entry point used by this shader.
    /// </summary>
    public static ReadOnlySpan<byte> EntryPoint => "main\0"u8;

    /// <summary>
    /// Gets the X workgroup count required to cover the packed path-tag words.
    /// The shader reduces one 4-byte tag word per thread at a workgroup size of 256, so this is
    /// ceil(<paramref name="pathTagWords"/> / 256).
    /// </summary>
    /// <param name="pathTagWords">The number of packed 4-byte path-tag words.</param>
    /// <returns>The X dispatch dimension in workgroups.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint GetDispatchX(uint pathTagWords)
        => (pathTagWords + 255U) / 256U;

    /// <summary>
    /// Creates the bind-group layout required by the pathtag-reduce stage.
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
        // Bindings match pathtag_reduce.wgsl:
        //   0 config uniform
        //   1 scene (read-only; tag words read at config.pathtag_base)
        //   2 reduced (read-write; one TagMonoid aggregate written per workgroup)
        BindGroupLayoutEntry* entries = stackalloc BindGroupLayoutEntry[3];
        entries[0] = SceneShaderBindingLayoutHelper.CreateUniformEntry(0, (nuint)sizeof(GpuSceneConfig));
        entries[1] = SceneShaderBindingLayoutHelper.CreateStorageEntry(1, BufferBindingType.ReadOnlyStorage);
        entries[2] = SceneShaderBindingLayoutHelper.CreateStorageEntry(2, BufferBindingType.Storage);

        BindGroupLayoutDescriptor descriptor = new()
        {
            entryCount = 3,
            entries = entries
        };

        layout = api.DeviceCreateBindGroupLayout(device, in descriptor);
        if (layout is null)
        {
            error = "Failed to create the pathtag-reduce bind-group layout.";
            return false;
        }

        error = null;
        return true;
    }
}
