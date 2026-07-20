// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Processing.Processors.Convolution;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Describes one immutable invocation of a WebGPU layer-effect program.
/// </summary>
internal readonly struct WebGPUShaderPass
{
    /// <summary>
    /// Initializes a new instance of the <see cref="WebGPUShaderPass"/> struct.
    /// </summary>
    /// <param name="program">The WGSL program to invoke.</param>
    /// <param name="uniforms">The immutable values supplied to the program.</param>
    public WebGPUShaderPass(WebGPUShaderProgram program, WebGPUShaderUniforms uniforms)
        : this(program, uniforms, null, null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="WebGPUShaderPass"/> struct.
    /// </summary>
    /// <param name="program">The WGSL program to invoke.</param>
    /// <param name="uniforms">The immutable values supplied to the program.</param>
    /// <param name="xBorderMode">The horizontal border mode used by <c>layer_sample</c>.</param>
    /// <param name="yBorderMode">The vertical border mode used by <c>layer_sample</c>.</param>
    public WebGPUShaderPass(
        WebGPUShaderProgram program,
        WebGPUShaderUniforms uniforms,
        BorderWrappingMode? xBorderMode,
        BorderWrappingMode? yBorderMode)
    {
        Guard.NotNull(program, nameof(program));
        Guard.NotNull(uniforms, nameof(uniforms));

        // Layout identity proves that the packed values use this program's exact offsets and types.
        if (!ReferenceEquals(program.UniformLayout, uniforms.Layout))
        {
            throw new ArgumentException("The supplied uniform values were built from a different layout.", nameof(uniforms));
        }

        this.Program = program;
        this.Uniforms = uniforms;
        this.XBorderMode = xBorderMode;
        this.YBorderMode = yBorderMode;
    }

    /// <summary>
    /// Gets the reusable WGSL program.
    /// </summary>
    public WebGPUShaderProgram Program { get; }

    /// <summary>
    /// Gets the immutable values supplied to the program.
    /// </summary>
    public WebGPUShaderUniforms Uniforms { get; }

    /// <summary>
    /// Gets the horizontal border mode used by <c>layer_sample</c>, or <see langword="null"/> for transparent samples.
    /// </summary>
    public BorderWrappingMode? XBorderMode { get; }

    /// <summary>
    /// Gets the vertical border mode used by <c>layer_sample</c>, or <see langword="null"/> for transparent samples.
    /// </summary>
    public BorderWrappingMode? YBorderMode { get; }
}
