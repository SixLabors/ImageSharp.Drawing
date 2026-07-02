// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Runtime.CompilerServices;
using Silk.NET.WebGPU;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// GPU stage that runs coarse rasterization: one workgroup per 16x16-tile bin merges the bin
/// element lists from binning back into draw order and serializes each tile's per-tile command
/// list (PTCL) for the fine stage, matching Vello's coarse stage shape. Wraps
/// <c>coarse.wgsl</c>.
/// </summary>
internal static unsafe class CoarseComputeShader
{
    /// <summary>
    /// Gets the generated WGSL source bytes for the coarse stage.
    /// </summary>
    public static ReadOnlySpan<byte> ShaderCode => GeneratedWgslShaderSources.CoarseCode;

    /// <summary>
    /// Gets the WGSL entry point used by this shader.
    /// </summary>
    public static ReadOnlySpan<byte> EntryPoint => "main\0"u8;

    /// <summary>
    /// Gets the X workgroup count required to cover the bin grid width.
    /// One workgroup covers one 16x16-tile bin (256 threads, one per tile), so the X count is
    /// the bin grid width itself.
    /// </summary>
    /// <param name="widthInBins">The bin grid width in 16x16-tile bins.</param>
    /// <returns>The X dispatch dimension in workgroups.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint GetDispatchX(uint widthInBins) => widthInBins;

    /// <summary>
    /// Gets the Y workgroup count required to cover the bin grid height.
    /// One workgroup covers one 16x16-tile bin, so the Y count is the bin grid height itself.
    /// </summary>
    /// <param name="heightInBins">The bin grid height in 16x16-tile bins.</param>
    /// <returns>The Y dispatch dimension in workgroups.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint GetDispatchY(uint heightInBins) => heightInBins;

    /// <summary>
    /// Creates the bind-group layout required by the coarse stage.
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
        // Bindings match coarse.wgsl:
        //   0 config uniform
        //   1 scene (read-only drawtags and drawdata)
        //   2 draw_monoids (read-only)
        //   3 info_bin_data (read-only draw info, bin headers, and bin element lists)
        //   4 paths (read-only sparse tile-grid lookup)
        //   5 rows (read-only sparse PathRow records)
        //   6 tiles (read-write; segment_count_or_ix rewritten to the allocated segment index)
        //   7 bump allocators (read-write; ptcl, segments and blend_spill counters, failure mask)
        //   8 ptcl (read-write; per-tile command lists written)
        BindGroupLayoutEntry* entries = stackalloc BindGroupLayoutEntry[9];
        entries[0] = CreateUniformEntry(0, (nuint)sizeof(GpuSceneConfig));
        entries[1] = CreateStorageEntry(1, BufferBindingType.ReadOnlyStorage, 0);
        entries[2] = CreateStorageEntry(2, BufferBindingType.ReadOnlyStorage, 0);
        entries[3] = CreateStorageEntry(3, BufferBindingType.ReadOnlyStorage, 0);
        entries[4] = CreateStorageEntry(4, BufferBindingType.ReadOnlyStorage, 0);
        entries[5] = CreateStorageEntry(5, BufferBindingType.ReadOnlyStorage, 0);
        entries[6] = CreateStorageEntry(6, BufferBindingType.Storage, 0);
        entries[7] = CreateStorageEntry(7, BufferBindingType.Storage, (nuint)sizeof(GpuSceneBumpAllocators));
        entries[8] = CreateStorageEntry(8, BufferBindingType.Storage, 0);

        BindGroupLayoutDescriptor descriptor = new()
        {
            EntryCount = 9,
            Entries = entries
        };

        layout = api.DeviceCreateBindGroupLayout(device, in descriptor);
        if (layout is null)
        {
            error = "Failed to create the WebGPU coarse bind-group layout.";
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
            Binding = binding,
            Visibility = ShaderStage.Compute,
            Buffer = new BufferBindingLayout
            {
                Type = type,
                HasDynamicOffset = false,
                MinBindingSize = minBindingSize
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
            Binding = binding,
            Visibility = ShaderStage.Compute,
            Buffer = new BufferBindingLayout
            {
                Type = BufferBindingType.Uniform,
                HasDynamicOffset = false,
                MinBindingSize = minBindingSize
            }
        };
}
