// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Runtime.CompilerServices;
using SixLabors.ImageSharp.Drawing.Processing.Backends.Native;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// GPU stage that preserves chunk-invariant scheduling state (the shared-stage counters and
/// failure bits) while clearing the per-chunk allocators before the next oversized-scene
/// tile-row window. Wraps <c>chunk_reset.wgsl</c>.
/// </summary>
internal static unsafe class ChunkResetComputeShader
{
    /// <summary>
    /// Gets the generated WGSL source bytes for the chunk-reset stage.
    /// </summary>
    public static ReadOnlySpan<byte> ShaderCode => GeneratedWgslShaderSources.ChunkResetCode;

    /// <summary>
    /// Gets the WGSL entry point used by this shader.
    /// </summary>
    public static ReadOnlySpan<byte> EntryPoint => "main\0"u8;

    /// <summary>
    /// Gets the fixed X workgroup count required by the chunk-reset stage.
    /// The stage runs as a single workgroup of one thread, so the count is always 1.
    /// </summary>
    /// <returns>The X dispatch dimension in workgroups; always 1.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint GetDispatchX() => 1;

    /// <summary>
    /// Creates the bind-group layout required by the chunk-reset stage.
    /// </summary>
    /// <param name="api">The WebGPU API facade used to create the bind-group layout.</param>
    /// <param name="device">The native WebGPU device that owns the created layout.</param>
    /// <param name="layout">Receives the created bind-group layout on success.</param>
    /// <param name="error">Receives the creation failure reason when the layout cannot be created.</param>
    /// <returns><see langword="true"/> when the bind-group layout was created successfully; otherwise, <see langword="false"/>.</returns>
    public static bool TryCreateBindGroupLayout(
        WebGPU api,
        WGPUDeviceImpl* device,
        out WGPUBindGroupLayoutImpl* layout,
        out string? error)
    {
        // Bindings match chunk_reset.wgsl:
        //   0 bump allocators (read-write; chunk-local counters zeroed, shared-stage state retained)
        WGPUBindGroupLayoutEntry* entries = stackalloc WGPUBindGroupLayoutEntry[1];
        entries[0] = SceneShaderBindingLayoutHelper.CreateStorageEntry(0, WGPUBufferBindingType.Storage, (nuint)sizeof(GpuSceneBumpAllocators));

        WGPUBindGroupLayoutDescriptor descriptor = new()
        {
            entryCount = 1,
            entries = entries
        };

        layout = api.DeviceCreateBindGroupLayout(device, in descriptor);
        if (layout is null)
        {
            error = "Failed to create the WebGPU chunk-reset bind-group layout.";
            return false;
        }

        error = null;
        return true;
    }
}
