// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Processing.Backends.Native;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// GPU stage that writes the final per-tile path segments: one thread per SegmentCount record
/// from path-count replays the line's tile-crossing traversal, clips the line to its tile, and
/// stores the tile-relative segment in the slot range reserved by coarse. Wraps
/// <c>path_tiling.wgsl</c>.
/// </summary>
internal static unsafe class PathTilingComputeShader
{
    /// <summary>
    /// Gets the generated WGSL source bytes for the path-tiling stage.
    /// </summary>
    public static ReadOnlySpan<byte> ShaderCode => GeneratedWgslShaderSources.PathTilingCode;

    /// <summary>
    /// Gets the WGSL entry point used by this shader.
    /// </summary>
    public static ReadOnlySpan<byte> EntryPoint => "main\0"u8;

    /// <summary>
    /// Creates the bind-group layout required by the path-tiling stage.
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
        // Bindings match path_tiling.wgsl:
        //   0 bump allocators (read-write because the buffer is atomic; this stage only reads the seg_counts total)
        //   1 seg_counts (read-only SegmentCount records from path_count)
        //   2 lines (read-only LineSoup from flatten)
        //   3 paths (read-only Path records)
        //   4 rows (read-only sparse PathRow records)
        //   5 tiles (read-only; segment_count_or_ix holds the inverted segment base index from coarse)
        //   6 segments (read-write; tile-relative Segment records written for fine)
        WGPUBindGroupLayoutEntry* entries = stackalloc WGPUBindGroupLayoutEntry[7];
        entries[0] = SceneShaderBindingLayoutHelper.CreateStorageEntry(0, WGPUBufferBindingType.Storage, (nuint)sizeof(GpuSceneBumpAllocators));
        entries[1] = SceneShaderBindingLayoutHelper.CreateStorageEntry(1, WGPUBufferBindingType.ReadOnlyStorage);
        entries[2] = SceneShaderBindingLayoutHelper.CreateStorageEntry(2, WGPUBufferBindingType.ReadOnlyStorage);
        entries[3] = SceneShaderBindingLayoutHelper.CreateStorageEntry(3, WGPUBufferBindingType.ReadOnlyStorage);
        entries[4] = SceneShaderBindingLayoutHelper.CreateStorageEntry(4, WGPUBufferBindingType.ReadOnlyStorage);
        entries[5] = SceneShaderBindingLayoutHelper.CreateStorageEntry(5, WGPUBufferBindingType.ReadOnlyStorage);
        entries[6] = SceneShaderBindingLayoutHelper.CreateStorageEntry(6, WGPUBufferBindingType.Storage);

        WGPUBindGroupLayoutDescriptor descriptor = new()
        {
            entryCount = 7,
            entries = entries
        };

        layout = api.DeviceCreateBindGroupLayout(device, in descriptor);
        if (layout is null)
        {
            error = "Failed to create the WebGPU path-tiling bind-group layout.";
            return false;
        }

        error = null;
        return true;
    }
}
