// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Runtime.CompilerServices;
using SixLabors.ImageSharp.Drawing.Processing.Backends.Native;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// GPU stage that finalizes the horizontal extent of every sparse path row (widening for
/// backdrop seeds and right-boundary touches), bump-allocates the backing tile storage, and
/// zeroes the allocated tiles. Wraps <c>tile_alloc.wgsl</c>.
/// </summary>
internal static unsafe class TileAllocComputeShader
{
    /// <summary>
    /// Gets the generated WGSL source bytes for the tile-allocation stage.
    /// </summary>
    public static ReadOnlySpan<byte> ShaderCode => GeneratedWgslShaderSources.TileAllocCode;

    /// <summary>
    /// Gets the WGSL entry point used by this shader.
    /// </summary>
    public static ReadOnlySpan<byte> EntryPoint => "main\0"u8;

    /// <summary>
    /// Gets the X workgroup count required to process every path.
    /// The shader runs one thread per path at a workgroup size of 256, so this is
    /// ceil(<paramref name="pathCount"/> / 256).
    /// </summary>
    /// <param name="pathCount">The number of paths (draw objects) in the scene.</param>
    /// <returns>The X dispatch dimension in workgroups.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint GetDispatchX(uint pathCount)
        => (pathCount + 255U) / 256U;

    /// <summary>
    /// Creates the bind-group layout required by the tile-allocation stage.
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
        // Bindings match tile_alloc.wgsl:
        //   0 config uniform
        //   1 bump allocators (read-write; tile counter bump-allocated, failure bit set on overflow)
        //   2 paths (read-only Path records from path_row_alloc)
        //   3 rows (read-write; final x0/x1 and base tile index written)
        //   4 tiles (read-write; allocated tiles zeroed)
        WGPUBindGroupLayoutEntry* entries = stackalloc WGPUBindGroupLayoutEntry[5];
        entries[0] = SceneShaderBindingLayoutHelper.CreateUniformEntry(0, (nuint)sizeof(GpuSceneConfig));
        entries[1] = SceneShaderBindingLayoutHelper.CreateStorageEntry(1, WGPUBufferBindingType.Storage, (nuint)sizeof(GpuSceneBumpAllocators));
        entries[2] = SceneShaderBindingLayoutHelper.CreateStorageEntry(2, WGPUBufferBindingType.ReadOnlyStorage);
        entries[3] = SceneShaderBindingLayoutHelper.CreateStorageEntry(3, WGPUBufferBindingType.Storage);
        entries[4] = SceneShaderBindingLayoutHelper.CreateStorageEntry(4, WGPUBufferBindingType.Storage);

        WGPUBindGroupLayoutDescriptor descriptor = new()
        {
            entryCount = 5,
            entries = entries
        };

        layout = api.DeviceCreateBindGroupLayout(device, in descriptor);
        if (layout is null)
        {
            error = "Failed to create the WebGPU tile-allocation bind-group layout.";
            return false;
        }

        error = null;
        return true;
    }
}
