// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Runtime.CompilerServices;
using Silk.NET.WebGPU;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// GPU stage that computes the indirect dispatch size for path-tiling from the number of
/// SegmentCount records produced by path-count, performing the late overflow check for that
/// counter and zeroing the dispatch after a failure. Wraps <c>path_tiling_setup.wgsl</c>.
/// </summary>
internal static unsafe class PathTilingSetupComputeShader
{
    /// <summary>
    /// Gets the generated WGSL source bytes for the path-tiling-setup stage.
    /// </summary>
    public static ReadOnlySpan<byte> ShaderCode => GeneratedWgslShaderSources.PathTilingSetupCode;

    /// <summary>
    /// Gets the WGSL entry point used by this shader.
    /// </summary>
    public static ReadOnlySpan<byte> EntryPoint => "main\0"u8;

    /// <summary>
    /// Gets the fixed X workgroup count required by the path-tiling-setup stage.
    /// The stage runs as a single workgroup of one thread, so the count is always 1.
    /// </summary>
    /// <returns>The X dispatch dimension in workgroups; always 1.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint GetDispatchX() => 1;

    /// <summary>
    /// Creates the bind-group layout required by the path-tiling-setup stage.
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
        // Bindings match path_tiling_setup.wgsl:
        //   0 config uniform
        //   1 bump allocators (read-write; seg_counts read, overflow flagged in failure mask)
        //   2 indirect (read-write; workgroup counts written for path_tiling)
        //   3 ptcl (read-write; slot 0 set to the abort marker on failure)
        BindGroupLayoutEntry* entries = stackalloc BindGroupLayoutEntry[4];
        entries[0] = SceneShaderBindingLayoutHelper.CreateUniformEntry(0, (nuint)sizeof(GpuSceneConfig));
        entries[1] = SceneShaderBindingLayoutHelper.CreateStorageEntry(1, BufferBindingType.Storage, (nuint)sizeof(GpuSceneBumpAllocators));
        entries[2] = SceneShaderBindingLayoutHelper.CreateStorageEntry(2, BufferBindingType.Storage, (nuint)sizeof(GpuSceneIndirectCount));
        entries[3] = SceneShaderBindingLayoutHelper.CreateStorageEntry(3, BufferBindingType.Storage);

        BindGroupLayoutDescriptor descriptor = new()
        {
            EntryCount = 4,
            Entries = entries
        };

        layout = api.DeviceCreateBindGroupLayout(device, in descriptor);
        if (layout is null)
        {
            error = "Failed to create the WebGPU path-tiling-setup bind-group layout.";
            return false;
        }

        error = null;
        return true;
    }
}
