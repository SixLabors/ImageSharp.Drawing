// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Processing.Backends.Native;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// GPU stage that derives sparse per-row tile column spans from the flattened line stream:
/// one thread per line replays the tile-crossing traversal, atomically growing each touched
/// row's span and accumulating per-row winding backdrops for tile-alloc. Wraps
/// <c>path_row_span.wgsl</c>.
/// </summary>
internal static unsafe class PathRowSpanComputeShader
{
    /// <summary>
    /// Gets the generated WGSL source bytes for the path-row span stage.
    /// </summary>
    public static ReadOnlySpan<byte> ShaderCode => GeneratedWgslShaderSources.PathRowSpanCode;

    /// <summary>
    /// Gets the WGSL entry point used by this shader.
    /// </summary>
    public static ReadOnlySpan<byte> EntryPoint => "main\0"u8;

    /// <summary>
    /// Creates the bind-group layout required by the path-row span stage.
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
        // Bindings match path_row_span.wgsl:
        //   0 config uniform
        //   1 bump allocators (read-write because the buffer is atomic; this stage only reads the lines counter)
        //   2 lines (read-only LineSoup from flatten)
        //   3 paths (read-only Path records from path_row_alloc)
        //   4 rows (read-write; spans, backdrops and flags accumulated atomically)
        WGPUBindGroupLayoutEntry* entries = stackalloc WGPUBindGroupLayoutEntry[5];
        entries[0] = SceneShaderBindingLayoutHelper.CreateUniformEntry(0, (nuint)sizeof(GpuSceneConfig));
        entries[1] = SceneShaderBindingLayoutHelper.CreateStorageEntry(1, WGPUBufferBindingType.Storage, (nuint)sizeof(GpuSceneBumpAllocators));
        entries[2] = SceneShaderBindingLayoutHelper.CreateStorageEntry(2, WGPUBufferBindingType.ReadOnlyStorage);
        entries[3] = SceneShaderBindingLayoutHelper.CreateStorageEntry(3, WGPUBufferBindingType.ReadOnlyStorage);
        entries[4] = SceneShaderBindingLayoutHelper.CreateStorageEntry(4, WGPUBufferBindingType.Storage);

        WGPUBindGroupLayoutDescriptor descriptor = new()
        {
            entryCount = 5,
            entries = entries
        };

        layout = api.DeviceCreateBindGroupLayout(device, in descriptor);
        if (layout is null)
        {
            error = "Failed to create the WebGPU path-row-span bind-group layout.";
            return false;
        }

        error = null;
        return true;
    }
}
