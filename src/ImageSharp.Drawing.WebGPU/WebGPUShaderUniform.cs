// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Describes one named value or fixed-size array supplied to a WebGPU layer-effect shader.
/// </summary>
public readonly struct WebGPUShaderUniform
{
    /// <summary>
    /// Initializes a new instance of the <see cref="WebGPUShaderUniform"/> struct.
    /// </summary>
    /// <param name="name">The WGSL member name used to access the value.</param>
    /// <param name="type">The value type.</param>
    /// <param name="elementCount">The fixed number of elements. Use one for a scalar, vector, or matrix value.</param>
    public WebGPUShaderUniform(string name, WebGPUShaderUniformType type, int elementCount)
    {
        Guard.NotNull(name, nameof(name));
        Guard.MustBeGreaterThan(elementCount, 0, nameof(elementCount));

        this.Name = name;
        this.Type = type;
        this.ElementCount = elementCount;
    }

    /// <summary>
    /// Gets the WGSL member name used to access the value.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the value type.
    /// </summary>
    public WebGPUShaderUniformType Type { get; }

    /// <summary>
    /// Gets the fixed number of elements.
    /// </summary>
    public int ElementCount { get; }
}
