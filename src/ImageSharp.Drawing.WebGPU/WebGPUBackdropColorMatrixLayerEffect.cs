// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Provides a shader-accelerated counterpart to <see cref="BackdropColorMatrixLayerEffect"/>.
/// </summary>
/// <remarks>
/// This effect can be used with both WebGPU and CPU drawing backends. WebGPU transforms the backdrop with a shader;
/// a CPU backend applies the equivalent <see cref="BackdropColorMatrixLayerEffect"/>.
/// </remarks>
public sealed class WebGPUBackdropColorMatrixLayerEffect : WebGPUBackdropShaderLayerEffect
{
    /// <summary>
    /// Initializes a new instance of the <see cref="WebGPUBackdropColorMatrixLayerEffect"/> class.
    /// </summary>
    /// <param name="matrix">The colour matrix applied to the backdrop.</param>
    public WebGPUBackdropColorMatrixLayerEffect(ColorMatrix matrix)
        : base(
            new BackdropColorMatrixLayerEffect(matrix),
            WebGPUColorMatrixLayerEffect.ShaderSource,
            WebGPUColorMatrixLayerEffect.UniformLayout)
    {
        this.Matrix = matrix;

        // Backdrop capture changes where the source comes from, not how a color matrix transforms
        // it. Copy the content adapter's pass into this effect's base-owned sequence.
        WebGPUColorMatrixLayerEffect shaderEffect = new(matrix);
        this.AddShaderPasses(shaderEffect.GetShaderPasses());
    }

    /// <summary>
    /// Gets the colour matrix applied to the backdrop.
    /// </summary>
    public ColorMatrix Matrix { get; }
}
