// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Runtime.CompilerServices;
using SixLabors.ImageSharp.Drawing.Processing.Backends.Native;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// GPU stage that initializes every per-path bounding box to an empty (inverted) box so
/// that flatten can accumulate extents with atomic min/max. Wraps <c>bbox_clear.wgsl</c>.
/// </summary>
internal static unsafe class BboxClearComputeShader
{
    /// <summary>
    /// Gets the generated WGSL source bytes for the bbox-clear stage.
    /// </summary>
    public static ReadOnlySpan<byte> ShaderCode => GeneratedWgslShaderSources.BboxClearCode;

    /// <summary>
    /// Gets the WGSL entry point used by this shader.
    /// </summary>
    public static ReadOnlySpan<byte> EntryPoint => "main\0"u8;

    /// <summary>
    /// Gets the X workgroup count required to clear every path bounding box.
    /// The shader resets one bounding box per thread at a workgroup size of 256, so this is
    /// ceil(<paramref name="pathCount"/> / 256).
    /// </summary>
    /// <param name="pathCount">The number of paths in the scene.</param>
    /// <returns>The X dispatch dimension in workgroups.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint GetDispatchX(uint pathCount)
        => (pathCount + 255U) / 256U;

    /// <summary>
    /// Creates the bind-group layout required by the bbox-clear stage.
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
        // Bindings match bbox_clear.wgsl:
        //   0 config uniform
        //   1 path_bboxes (read-write; min/max fields reset to their inverted extremes)
        WGPUBindGroupLayoutEntry* entries = stackalloc WGPUBindGroupLayoutEntry[2];
        entries[0] = SceneShaderBindingLayoutHelper.CreateUniformEntry(0, (nuint)sizeof(GpuSceneConfig));
        entries[1] = SceneShaderBindingLayoutHelper.CreateStorageEntry(1, WGPUBufferBindingType.Storage);

        WGPUBindGroupLayoutDescriptor descriptor = new()
        {
            entryCount = 2,
            entries = entries
        };

        layout = api.DeviceCreateBindGroupLayout(device, in descriptor);
        if (layout is null)
        {
            error = "Failed to create the bbox-clear bind-group layout.";
            return false;
        }

        error = null;
        return true;
    }
}
