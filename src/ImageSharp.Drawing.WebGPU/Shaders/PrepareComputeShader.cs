// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Runtime.CompilerServices;
using Silk.NET.WebGPU;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// GPU stage that starts a scheduling run by zeroing every bump allocator counter and the
/// failure mask on the GPU, avoiding a CPU buffer write. It never cancels later stages; all
/// stages run so the allocators report true demand in a single pass. Wraps <c>prepare.wgsl</c>.
/// </summary>
internal static unsafe class PrepareComputeShader
{
    /// <summary>
    /// Gets the generated WGSL source bytes for the prepare stage.
    /// </summary>
    public static ReadOnlySpan<byte> ShaderCode => GeneratedWgslShaderSources.PrepareCode;

    /// <summary>
    /// Gets the WGSL entry point used by this shader.
    /// </summary>
    public static ReadOnlySpan<byte> EntryPoint => "main\0"u8;

    /// <summary>
    /// Gets the fixed X workgroup count required by the prepare stage.
    /// The stage runs as a single workgroup of one thread, so the count is always 1.
    /// </summary>
    /// <returns>The X dispatch dimension in workgroups; always 1.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint GetDispatchX() => 1;

    /// <summary>
    /// Creates the bind-group layout required by the prepare stage.
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
        // Bindings match prepare.wgsl:
        //   0 bump allocators (read-write; all counters and the failure mask zeroed)
        BindGroupLayoutEntry* entries = stackalloc BindGroupLayoutEntry[1];
        entries[0] = SceneShaderBindingLayoutHelper.CreateStorageEntry(0, BufferBindingType.Storage, (nuint)sizeof(GpuSceneBumpAllocators));

        BindGroupLayoutDescriptor descriptor = new()
        {
            EntryCount = 1,
            Entries = entries
        };

        layout = api.DeviceCreateBindGroupLayout(device, in descriptor);
        if (layout is null)
        {
            error = "Failed to create the WebGPU prepare bind-group layout.";
            return false;
        }

        error = null;
        return true;
    }
}
