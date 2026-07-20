// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Describes the packed location of one shader uniform member.
/// </summary>
internal readonly struct WebGPUShaderUniformMember
{
    /// <summary>
    /// Initializes a new instance of the <see cref="WebGPUShaderUniformMember"/> struct.
    /// </summary>
    /// <param name="uniform">The public declaration.</param>
    /// <param name="offset">The byte offset of the first element.</param>
    /// <param name="stride">The byte stride between elements.</param>
    /// <param name="wgslType">The WGSL element type.</param>
    public WebGPUShaderUniformMember(WebGPUShaderUniform uniform, int offset, int stride, string wgslType)
    {
        this.Uniform = uniform;
        this.Offset = offset;
        this.Stride = stride;
        this.WgslType = wgslType;
    }

    /// <summary>
    /// Gets the public declaration.
    /// </summary>
    public WebGPUShaderUniform Uniform { get; }

    /// <summary>
    /// Gets the byte offset of the first element.
    /// </summary>
    public int Offset { get; }

    /// <summary>
    /// Gets the byte stride between elements.
    /// </summary>
    public int Stride { get; }

    /// <summary>
    /// Gets the WGSL element type.
    /// </summary>
    public string WgslType { get; }
}
