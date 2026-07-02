// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using Silk.NET.WebGPU;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// GPU stage that finishes the draw-tag prefix sum started by draw-reduce, producing an
/// exclusive DrawMonoid per draw object, then decodes each draw object into the per-draw info
/// stream and emits ClipInp records for the clip stack stages. Wraps <c>draw_leaf.wgsl</c>.
/// </summary>
internal static unsafe class DrawLeafComputeShader
{
    /// <summary>
    /// Gets the generated WGSL source bytes for the draw-leaf stage.
    /// </summary>
    public static ReadOnlySpan<byte> ShaderCode => GeneratedWgslShaderSources.DrawLeafCode;

    /// <summary>
    /// Gets the WGSL entry point used by this shader.
    /// </summary>
    public static ReadOnlySpan<byte> EntryPoint => "main\0"u8;

    /// <summary>
    /// Creates the bind-group layout required by the draw-leaf stage.
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
        // Bindings match draw_leaf.wgsl:
        //   0 config uniform
        //   1 scene (read-only drawtag and drawdata stream)
        //   2 reduced (read-only per-workgroup DrawMonoid aggregates from draw_reduce)
        //   3 path_bbox (read-only per-path bounds and draw flags)
        //   4 draw_monoid (read-write; exclusive prefix written per draw object)
        //   5 info (read-write; per-draw brush info words for coarse and fine)
        //   6 clip_inp (read-write; ClipInp records for clip_reduce and clip_leaf)
        BindGroupLayoutEntry* entries = stackalloc BindGroupLayoutEntry[7];
        entries[0] = SceneShaderBindingLayoutHelper.CreateUniformEntry(0, (nuint)sizeof(GpuSceneConfig));
        entries[1] = SceneShaderBindingLayoutHelper.CreateStorageEntry(1, BufferBindingType.ReadOnlyStorage);
        entries[2] = SceneShaderBindingLayoutHelper.CreateStorageEntry(2, BufferBindingType.ReadOnlyStorage);
        entries[3] = SceneShaderBindingLayoutHelper.CreateStorageEntry(3, BufferBindingType.ReadOnlyStorage);
        entries[4] = SceneShaderBindingLayoutHelper.CreateStorageEntry(4, BufferBindingType.Storage);
        entries[5] = SceneShaderBindingLayoutHelper.CreateStorageEntry(5, BufferBindingType.Storage);
        entries[6] = SceneShaderBindingLayoutHelper.CreateStorageEntry(6, BufferBindingType.Storage);

        BindGroupLayoutDescriptor descriptor = new()
        {
            EntryCount = 7,
            Entries = entries
        };

        layout = api.DeviceCreateBindGroupLayout(device, in descriptor);
        if (layout is null)
        {
            error = "Failed to create the draw-leaf bind-group layout.";
            return false;
        }

        error = null;
        return true;
    }
}
