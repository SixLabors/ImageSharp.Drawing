// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Contains an immutable snapshot of values supplied to a WebGPU layer-effect shader.
/// </summary>
public sealed class WebGPUShaderUniforms
{
    private readonly byte[] data;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebGPUShaderUniforms"/> class.
    /// </summary>
    /// <param name="layout">The layout that describes the values.</param>
    /// <param name="data">The packed immutable value snapshot.</param>
    internal WebGPUShaderUniforms(WebGPUShaderUniformLayout layout, byte[] data)
    {
        this.Layout = layout;
        this.data = data;
    }

    /// <summary>
    /// Gets the layout that describes the values.
    /// </summary>
    public WebGPUShaderUniformLayout Layout { get; }

    /// <summary>
    /// Gets the packed immutable value bytes.
    /// </summary>
    internal ReadOnlySpan<byte> Data => this.data;
}
