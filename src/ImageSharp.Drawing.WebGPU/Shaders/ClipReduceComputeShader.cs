// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// GPU stage that runs the first pass of the clip-stack monoid scan: each workgroup reduces
/// its span of clip records to a single Bic (bicyclic semigroup) aggregate and records the
/// still-open BeginClip stack elements so clip-leaf can resolve pushes and pops across
/// workgroup boundaries. Wraps <c>clip_reduce.wgsl</c>.
/// </summary>
internal static unsafe class ClipReduceComputeShader
{
    /// <summary>
    /// Gets the generated WGSL source bytes for the clip-reduce stage.
    /// </summary>
    public static ReadOnlySpan<byte> ShaderCode => GeneratedWgslShaderSources.ClipReduceCode;

    /// <summary>
    /// Gets the WGSL entry point used by this shader.
    /// </summary>
    public static ReadOnlySpan<byte> EntryPoint => "main\0"u8;

    /// <summary>
    /// Creates the bind-group layout required by the clip-reduce stage.
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
        // Bindings match clip_reduce.wgsl:
        //   0 clip_inp (read-only ClipInp records emitted by draw_leaf)
        //   1 path_bboxes (read-only per-path bounds)
        //   2 reduced (read-write; one Bic aggregate written per workgroup)
        //   3 clip_out (read-write; ClipEl written per open BeginClip)
        BindGroupLayoutEntry* entries = stackalloc BindGroupLayoutEntry[4];
        entries[0] = SceneShaderBindingLayoutHelper.CreateStorageEntry(0, BufferBindingType.ReadOnlyStorage);
        entries[1] = SceneShaderBindingLayoutHelper.CreateStorageEntry(1, BufferBindingType.ReadOnlyStorage);
        entries[2] = SceneShaderBindingLayoutHelper.CreateStorageEntry(2, BufferBindingType.Storage);
        entries[3] = SceneShaderBindingLayoutHelper.CreateStorageEntry(3, BufferBindingType.Storage);

        BindGroupLayoutDescriptor descriptor = new()
        {
            entryCount = 4,
            entries = entries
        };

        layout = api.DeviceCreateBindGroupLayout(device, in descriptor);
        if (layout is null)
        {
            error = "Failed to create the clip-reduce bind-group layout.";
            return false;
        }

        error = null;
        return true;
    }
}
