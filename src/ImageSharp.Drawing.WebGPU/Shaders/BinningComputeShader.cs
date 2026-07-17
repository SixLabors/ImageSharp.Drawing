// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Runtime.CompilerServices;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// GPU stage that assigns each draw object to every 16x16-tile bin its clipped bounding box
/// touches, writing bin headers and element lists using Vello's bitmap-compaction structure.
/// Wraps <c>binning.wgsl</c>.
/// </summary>
internal static unsafe class BinningComputeShader
{
    /// <summary>
    /// Gets the generated WGSL source bytes for the binning stage.
    /// </summary>
    public static ReadOnlySpan<byte> ShaderCode => GeneratedWgslShaderSources.BinningCode;

    /// <summary>
    /// Gets the WGSL entry point used by this shader.
    /// </summary>
    public static ReadOnlySpan<byte> EntryPoint => "main\0"u8;

    /// <summary>
    /// Gets the X workgroup count required to bin every draw object in the scene.
    /// Each workgroup covers one 256-element draw partition (workgroup size 256, one thread
    /// per draw object), so this is ceil(<paramref name="drawObjectCount"/> / 256). The Y axis
    /// of the dispatch chunks the bin grid and is computed by the caller.
    /// </summary>
    /// <param name="drawObjectCount">The number of draw objects in the scene.</param>
    /// <returns>The X dispatch dimension in workgroups.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint GetDispatchX(uint drawObjectCount)
        => (drawObjectCount + 255U) / 256U;

    /// <summary>
    /// Creates the bind-group layout required by the binning stage.
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
        // Bindings match binning.wgsl:
        //   0 config uniform
        //   1 draw_monoids (read-only)
        //   2 path_bbox_buf (read-only path bboxes plus interest rects)
        //   3 clip_bbox_buf (read-only clip-stack bboxes from clip_leaf)
        //   4 intersected_bbox (read-write; clipped bbox written per draw object)
        //   5 bump allocators (read-write; binning counter and failure mask)
        //   6 info_bin_data (read-write; bin headers and element lists)
        BindGroupLayoutEntry* entries = stackalloc BindGroupLayoutEntry[7];
        entries[0] = SceneShaderBindingLayoutHelper.CreateUniformEntry(0, (nuint)sizeof(GpuSceneConfig));
        entries[1] = SceneShaderBindingLayoutHelper.CreateStorageEntry(1, BufferBindingType.ReadOnlyStorage);
        entries[2] = SceneShaderBindingLayoutHelper.CreateStorageEntry(2, BufferBindingType.ReadOnlyStorage);
        entries[3] = SceneShaderBindingLayoutHelper.CreateStorageEntry(3, BufferBindingType.ReadOnlyStorage);
        entries[4] = SceneShaderBindingLayoutHelper.CreateStorageEntry(4, BufferBindingType.Storage);
        entries[5] = SceneShaderBindingLayoutHelper.CreateStorageEntry(5, BufferBindingType.Storage, (nuint)sizeof(GpuSceneBumpAllocators));
        entries[6] = SceneShaderBindingLayoutHelper.CreateStorageEntry(6, BufferBindingType.Storage);

        BindGroupLayoutDescriptor descriptor = new()
        {
            entryCount = 7,
            entries = entries
        };

        layout = api.DeviceCreateBindGroupLayout(device, in descriptor);
        if (layout is null)
        {
            error = "Failed to create the WebGPU scene binning bind-group layout.";
            return false;
        }

        error = null;
        return true;
    }
}
