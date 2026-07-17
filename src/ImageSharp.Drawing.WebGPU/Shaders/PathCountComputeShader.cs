// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Runtime.CompilerServices;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// GPU stage that counts, per tile, how many flattened line segments cross it: one thread per
/// line walks the line's tile crossings, bumps tile segment counts and backdrops, and emits one
/// SegmentCount record per crossing for path-tiling. Wraps <c>path_count.wgsl</c>.
/// </summary>
internal static unsafe class PathCountComputeShader
{
    /// <summary>
    /// Gets the generated WGSL source bytes for the path-count stage.
    /// </summary>
    public static ReadOnlySpan<byte> ShaderCode => GeneratedWgslShaderSources.PathCountCode;

    /// <summary>
    /// Gets the WGSL entry point used by this shader.
    /// </summary>
    public static ReadOnlySpan<byte> EntryPoint => "main\0"u8;

    /// <summary>
    /// Gets the X workgroup count required to process every emitted line.
    /// The shader runs one thread per line at a workgroup size of 256, so this is
    /// ceil(<paramref name="lineCount"/> / 256). At runtime the stage is normally driven by
    /// the indirect count written by path-count-setup using the same divisor.
    /// </summary>
    /// <param name="lineCount">The number of flattened lines.</param>
    /// <returns>The X dispatch dimension in workgroups.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint GetDispatchX(uint lineCount)
        => (lineCount + 255U) / 256U;

    /// <summary>
    /// Creates the bind-group layout required by the path-count stage.
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
        // Bindings match path_count.wgsl:
        //   0 config uniform
        //   1 bump allocators (read-write; lines read, seg_counts bump-allocated)
        //   2 lines (read-only LineSoup from flatten)
        //   3 paths (read-only Path records from path_row_alloc)
        //   4 rows (read-only PathRow spans finalized by tile_alloc)
        //   5 tile (read-write; backdrop and segment counts updated atomically)
        //   6 seg_counts (read-write; one SegmentCount record written per crossing)
        BindGroupLayoutEntry* entries = stackalloc BindGroupLayoutEntry[7];
        entries[0] = CreateUniformEntry(0, (nuint)sizeof(GpuSceneConfig));
        entries[1] = CreateStorageEntry(1, BufferBindingType.Storage, (nuint)sizeof(GpuSceneBumpAllocators));
        entries[2] = CreateStorageEntry(2, BufferBindingType.ReadOnlyStorage, 0);
        entries[3] = CreateStorageEntry(3, BufferBindingType.ReadOnlyStorage, 0);
        entries[4] = CreateStorageEntry(4, BufferBindingType.ReadOnlyStorage, 0);
        entries[5] = CreateStorageEntry(5, BufferBindingType.Storage, 0);
        entries[6] = CreateStorageEntry(6, BufferBindingType.Storage, 0);

        BindGroupLayoutDescriptor descriptor = new()
        {
            entryCount = 7,
            entries = entries
        };

        layout = api.DeviceCreateBindGroupLayout(device, in descriptor);
        if (layout is null)
        {
            error = "Failed to create the WebGPU path-count bind-group layout.";
            return false;
        }

        error = null;
        return true;
    }

    /// <summary>
    /// Creates one compute-stage storage-buffer binding entry.
    /// </summary>
    /// <param name="binding">The WGSL binding index.</param>
    /// <param name="type">The storage-buffer access mode.</param>
    /// <param name="minBindingSize">The minimum buffer binding size in bytes, or 0 to skip validation.</param>
    /// <returns>The populated binding entry.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static BindGroupLayoutEntry CreateStorageEntry(uint binding, BufferBindingType type, nuint minBindingSize)
        => new()
        {
            binding = binding,
            visibility = (ulong)ShaderStage.Compute,
            buffer = new BufferBindingLayout
            {
                type = type,
                hasDynamicOffset = 0U,
                minBindingSize = minBindingSize
            }
        };

    /// <summary>
    /// Creates one compute-stage uniform-buffer binding entry.
    /// </summary>
    /// <param name="binding">The WGSL binding index.</param>
    /// <param name="minBindingSize">The minimum buffer binding size in bytes.</param>
    /// <returns>The populated binding entry.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static BindGroupLayoutEntry CreateUniformEntry(uint binding, nuint minBindingSize)
        => new()
        {
            binding = binding,
            visibility = (ulong)ShaderStage.Compute,
            buffer = new BufferBindingLayout
            {
                type = BufferBindingType.Uniform,
                hasDynamicOffset = 0U,
                minBindingSize = minBindingSize
            }
        };
}
