// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Runtime.CompilerServices;
using Silk.NET.WebGPU;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// GPU stage that flattens encoded path segments into a device-space line soup, expands
/// strokes via the CPU PolygonStroker port, and accumulates per-path bounding boxes for the
/// downstream stages. Wraps <c>flatten.wgsl</c>.
/// </summary>
internal static unsafe class FlattenComputeShader
{
    /// <summary>
    /// Gets the generated WGSL source bytes for the flatten stage.
    /// </summary>
    public static ReadOnlySpan<byte> ShaderCode => GeneratedWgslShaderSources.FlattenCode;

    /// <summary>
    /// Gets the WGSL entry point used by this shader.
    /// </summary>
    public static ReadOnlySpan<byte> EntryPoint => "main\0"u8;

    /// <summary>
    /// Gets the X workgroup count required to cover the packed path-tag stream.
    /// The shader runs one invocation per path-tag byte at a workgroup size of 256, so this is
    /// ceil(<paramref name="pathTagCount"/> / 256).
    /// </summary>
    /// <param name="pathTagCount">The number of path-tag bytes in the scene stream.</param>
    /// <returns>The X dispatch dimension in workgroups.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint GetDispatchX(uint pathTagCount)
        => (pathTagCount + 255U) / 256U;

    /// <summary>
    /// Creates the bind-group layout required by the flatten stage.
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
        // Bindings match flatten.wgsl:
        //   0 config uniform
        //   1 scene (read-only path tags, points, styles and transforms)
        //   2 tag_monoids (read-only pathtag prefix sums from the scan stages)
        //   3 path_bboxes (read-write; extents merged with atomic min/max)
        //   4 bump allocators (read-write; lines counter)
        //   5 lines (read-write; LineSoup records appended)
        BindGroupLayoutEntry* entries = stackalloc BindGroupLayoutEntry[6];
        entries[0] = SceneShaderBindingLayoutHelper.CreateUniformEntry(0, (nuint)sizeof(GpuSceneConfig));
        entries[1] = SceneShaderBindingLayoutHelper.CreateStorageEntry(1, BufferBindingType.ReadOnlyStorage);
        entries[2] = SceneShaderBindingLayoutHelper.CreateStorageEntry(2, BufferBindingType.ReadOnlyStorage);
        entries[3] = SceneShaderBindingLayoutHelper.CreateStorageEntry(3, BufferBindingType.Storage);
        entries[4] = SceneShaderBindingLayoutHelper.CreateStorageEntry(4, BufferBindingType.Storage, (nuint)sizeof(GpuSceneBumpAllocators));
        entries[5] = SceneShaderBindingLayoutHelper.CreateStorageEntry(5, BufferBindingType.Storage);

        BindGroupLayoutDescriptor descriptor = new()
        {
            EntryCount = 6,
            Entries = entries
        };

        layout = api.DeviceCreateBindGroupLayout(device, in descriptor);
        if (layout is null)
        {
            error = "Failed to create the flatten bind-group layout.";
            return false;
        }

        error = null;
        return true;
    }
}
