// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Runtime.CompilerServices;
using SixLabors.ImageSharp.Drawing.Processing.Backends.Native;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// GPU stage that bump-allocates sparse per-path tile-row metadata: one thread per draw object
/// converts the draw bounds to a chunk-clamped tile bbox, writes the Path record, and resets
/// each covered row to an empty span before line-driven span discovery. Wraps
/// <c>path_row_alloc.wgsl</c>.
/// </summary>
internal static unsafe class PathRowAllocComputeShader
{
    /// <summary>
    /// Gets the generated WGSL source bytes for the path-row allocation stage.
    /// </summary>
    public static ReadOnlySpan<byte> ShaderCode => GeneratedWgslShaderSources.PathRowAllocCode;

    /// <summary>
    /// Gets the WGSL entry point used by this shader.
    /// </summary>
    public static ReadOnlySpan<byte> EntryPoint => "main\0"u8;

    /// <summary>
    /// Gets the X workgroup count required to process every path.
    /// The shader runs one thread per draw object at a workgroup size of 256, so this is
    /// ceil(<paramref name="pathCount"/> / 256).
    /// </summary>
    /// <param name="pathCount">The number of paths (draw objects) in the scene.</param>
    /// <returns>The X dispatch dimension in workgroups.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint GetDispatchX(uint pathCount)
        => (pathCount + 255U) / 256U;

    /// <summary>
    /// Creates the bind-group layout required by the path-row allocation stage.
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
        // Bindings match path_row_alloc.wgsl:
        //   0 config uniform
        //   1 scene (read-only draw tags)
        //   2 draw_bboxes (read-only draw bounds from draw_leaf)
        //   3 bump allocators (read-write; path_rows bump-allocated, failure bit set on overflow)
        //   4 paths (read-write; Path record written per draw object)
        //   5 rows (read-write; PathRow records initialized to empty spans)
        WGPUBindGroupLayoutEntry* entries = stackalloc WGPUBindGroupLayoutEntry[6];
        entries[0] = SceneShaderBindingLayoutHelper.CreateUniformEntry(0, (nuint)sizeof(GpuSceneConfig));
        entries[1] = SceneShaderBindingLayoutHelper.CreateStorageEntry(1, WGPUBufferBindingType.ReadOnlyStorage);
        entries[2] = SceneShaderBindingLayoutHelper.CreateStorageEntry(2, WGPUBufferBindingType.ReadOnlyStorage);
        entries[3] = SceneShaderBindingLayoutHelper.CreateStorageEntry(3, WGPUBufferBindingType.Storage, (nuint)sizeof(GpuSceneBumpAllocators));
        entries[4] = SceneShaderBindingLayoutHelper.CreateStorageEntry(4, WGPUBufferBindingType.Storage);
        entries[5] = SceneShaderBindingLayoutHelper.CreateStorageEntry(5, WGPUBufferBindingType.Storage);

        WGPUBindGroupLayoutDescriptor descriptor = new()
        {
            entryCount = 6,
            entries = entries
        };

        layout = api.DeviceCreateBindGroupLayout(device, in descriptor);
        if (layout is null)
        {
            error = "Failed to create the WebGPU path-row-allocation bind-group layout.";
            return false;
        }

        error = null;
        return true;
    }
}
