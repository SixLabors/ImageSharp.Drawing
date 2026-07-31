// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Runtime.CompilerServices;
using SixLabors.ImageSharp.Drawing.Processing.Backends.Native;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// GPU stage that computes the indirect dispatch size for the per-line stages
/// (path-row-span and path-count) by dividing the flattened line count by the workgroup
/// size, or zero workgroups after an upstream allocation failure. Wraps
/// <c>path_count_setup.wgsl</c>.
/// </summary>
internal static unsafe class PathCountSetupComputeShader
{
    /// <summary>
    /// Gets the generated WGSL source bytes for the path-count-setup stage.
    /// </summary>
    public static ReadOnlySpan<byte> ShaderCode => GeneratedWgslShaderSources.PathCountSetupCode;

    /// <summary>
    /// Gets the WGSL entry point used by this shader.
    /// </summary>
    public static ReadOnlySpan<byte> EntryPoint => "main\0"u8;

    /// <summary>
    /// Gets the fixed X workgroup count required by the path-count-setup stage.
    /// The stage runs as a single workgroup of one thread, so the count is always 1.
    /// </summary>
    /// <returns>The X dispatch dimension in workgroups; always 1.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint GetDispatchX() => 1;

    /// <summary>
    /// Creates the bind-group layout required by the path-count-setup stage.
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
        // Bindings match path_count_setup.wgsl:
        //   0 bump allocators (read-write because the buffer is atomic; lines counter and failure mask read)
        //   1 indirect (read-write; workgroup counts written for path_row_span and path_count)
        WGPUBindGroupLayoutEntry* entries = stackalloc WGPUBindGroupLayoutEntry[2];
        entries[0] = SceneShaderBindingLayoutHelper.CreateStorageEntry(0, WGPUBufferBindingType.Storage, (nuint)sizeof(GpuSceneBumpAllocators));
        entries[1] = SceneShaderBindingLayoutHelper.CreateStorageEntry(1, WGPUBufferBindingType.Storage, (nuint)sizeof(GpuSceneIndirectCount));

        WGPUBindGroupLayoutDescriptor descriptor = new()
        {
            entryCount = 2,
            entries = entries
        };

        layout = api.DeviceCreateBindGroupLayout(device, in descriptor);
        if (layout is null)
        {
            error = "Failed to create the WebGPU path-count-setup bind-group layout.";
            return false;
        }

        error = null;
        return true;
    }
}
