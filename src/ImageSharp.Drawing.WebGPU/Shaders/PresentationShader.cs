// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// GPU stage that transfers an ImageSharp-owned frame texture into a presentable surface attachment.
/// Wraps <c>present.wgsl</c>.
/// </summary>
internal static unsafe class PresentationShader
{
    /// <summary>
    /// Gets the generated WGSL source bytes for the presentation stage.
    /// </summary>
    public static ReadOnlySpan<byte> ShaderCode => GeneratedWgslShaderSources.PresentCode;

    /// <summary>
    /// Creates the bind-group layout required by the presentation stage.
    /// </summary>
    /// <param name="api">The WebGPU API facade.</param>
    /// <param name="device">The device that owns the presentation pipeline.</param>
    /// <param name="layout">Receives the created bind-group layout on success.</param>
    /// <param name="error">Receives the creation failure reason when layout creation fails.</param>
    /// <returns><see langword="true"/> when the bind-group layout was created successfully; otherwise, <see langword="false"/>.</returns>
    public static bool TryCreateBindGroupLayout(WebGPU api, Device* device, out BindGroupLayout* layout, out string? error)
    {
        BindGroupLayoutEntry entry = new()
        {
            binding = 0,
            visibility = (ulong)ShaderStage.Fragment,
            texture = new TextureBindingLayout
            {
                sampleType = TextureSampleType.Float,
                viewDimension = TextureViewDimension._2D,
                multisampled = 0U
            }
        };

        BindGroupLayoutDescriptor descriptor = new()
        {
            entryCount = 1,
            entries = &entry
        };

        layout = api.DeviceCreateBindGroupLayout(device, in descriptor);
        if (layout is null)
        {
            error = "Failed to create the WebGPU presentation bind-group layout.";
            return false;
        }

        error = null;
        return true;
    }
}
