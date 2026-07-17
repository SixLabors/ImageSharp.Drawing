// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// GPU stage that runs the first pass of the two-pass prefix sum over the draw-tag stream:
/// each workgroup reduces its assigned draw tags to a single DrawMonoid aggregate that
/// draw-leaf later combines into a full exclusive prefix sum. Wraps <c>draw_reduce.wgsl</c>.
/// </summary>
internal static unsafe class DrawReduceComputeShader
{
    /// <summary>
    /// Gets the generated WGSL source bytes for the draw-reduce stage.
    /// </summary>
    public static ReadOnlySpan<byte> ShaderCode => GeneratedWgslShaderSources.DrawReduceCode;

    /// <summary>
    /// Gets the WGSL entry point used by this shader.
    /// </summary>
    public static ReadOnlySpan<byte> EntryPoint => "main\0"u8;

    /// <summary>
    /// Creates the bind-group layout required by the draw-reduce stage.
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
        // Bindings match draw_reduce.wgsl:
        //   0 config uniform
        //   1 scene (read-only; draw tags read at config.drawtag_base)
        //   2 reduced (read-write; one DrawMonoid aggregate written per workgroup)
        BindGroupLayoutEntry* entries = stackalloc BindGroupLayoutEntry[3];
        entries[0] = SceneShaderBindingLayoutHelper.CreateUniformEntry(0, (nuint)sizeof(GpuSceneConfig));
        entries[1] = SceneShaderBindingLayoutHelper.CreateStorageEntry(1, BufferBindingType.ReadOnlyStorage);
        entries[2] = SceneShaderBindingLayoutHelper.CreateStorageEntry(2, BufferBindingType.Storage);

        BindGroupLayoutDescriptor descriptor = new()
        {
            entryCount = 3,
            entries = entries
        };

        layout = api.DeviceCreateBindGroupLayout(device, in descriptor);
        if (layout is null)
        {
            error = "Failed to create the draw-reduce bind-group layout.";
            return false;
        }

        error = null;
        return true;
    }
}
