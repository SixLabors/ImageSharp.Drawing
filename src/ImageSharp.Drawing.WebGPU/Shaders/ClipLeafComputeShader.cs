// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Processing.Backends.Native;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// GPU stage that runs the second pass of the clip-stack monoid scan: it resolves
/// BeginClip/EndClip nesting across the scene, producing a conservative clip bounding box per
/// clip record and rewriting each EndClip's draw monoid to reference its matching BeginClip.
/// Wraps <c>clip_leaf.wgsl</c>.
/// </summary>
internal static unsafe class ClipLeafComputeShader
{
    /// <summary>
    /// Gets the generated WGSL source bytes for the clip-leaf stage.
    /// </summary>
    public static ReadOnlySpan<byte> ShaderCode => GeneratedWgslShaderSources.ClipLeafCode;

    /// <summary>
    /// Gets the WGSL entry point used by this shader.
    /// </summary>
    public static ReadOnlySpan<byte> EntryPoint => "main\0"u8;

    /// <summary>
    /// Creates the bind-group layout required by the clip-leaf stage.
    /// </summary>
    /// <param name="api">The WebGPU API facade.</param>
    /// <param name="device">The device that owns the staged-scene pipelines.</param>
    /// <param name="layout">Receives the created bind-group layout on success.</param>
    /// <param name="error">Receives the creation failure reason when layout creation fails.</param>
    /// <returns><see langword="true"/> when the bind-group layout was created successfully; otherwise, <see langword="false"/>.</returns>
    public static bool TryCreateBindGroupLayout(
        WebGPU api,
        WGPUDeviceImpl* device,
        out WGPUBindGroupLayoutImpl* layout,
        out string? error)
    {
        // Bindings match clip_leaf.wgsl:
        //   0 config uniform
        //   1 clip_inp (read-only ClipInp records from draw_leaf)
        //   2 path_bboxes (read-only per-path bounds)
        //   3 reduced (read-only per-workgroup Bic aggregates from clip_reduce)
        //   4 clip_els (read-only open-clip stack elements from clip_reduce)
        //   5 draw_monoids (read-write; EndClip entries rewritten in place)
        //   6 clip_bboxes (read-write; conservative bbox per clip record, consumed by binning)
        WGPUBindGroupLayoutEntry* entries = stackalloc WGPUBindGroupLayoutEntry[7];
        entries[0] = SceneShaderBindingLayoutHelper.CreateUniformEntry(0, (nuint)sizeof(GpuSceneConfig));
        entries[1] = SceneShaderBindingLayoutHelper.CreateStorageEntry(1, WGPUBufferBindingType.ReadOnlyStorage);
        entries[2] = SceneShaderBindingLayoutHelper.CreateStorageEntry(2, WGPUBufferBindingType.ReadOnlyStorage);
        entries[3] = SceneShaderBindingLayoutHelper.CreateStorageEntry(3, WGPUBufferBindingType.ReadOnlyStorage);
        entries[4] = SceneShaderBindingLayoutHelper.CreateStorageEntry(4, WGPUBufferBindingType.ReadOnlyStorage);
        entries[5] = SceneShaderBindingLayoutHelper.CreateStorageEntry(5, WGPUBufferBindingType.Storage);
        entries[6] = SceneShaderBindingLayoutHelper.CreateStorageEntry(6, WGPUBufferBindingType.Storage);

        WGPUBindGroupLayoutDescriptor descriptor = new()
        {
            entryCount = 7,
            entries = entries
        };

        layout = api.DeviceCreateBindGroupLayout(device, in descriptor);
        if (layout is null)
        {
            error = "Failed to create the clip-leaf bind-group layout.";
            return false;
        }

        error = null;
        return true;
    }
}
