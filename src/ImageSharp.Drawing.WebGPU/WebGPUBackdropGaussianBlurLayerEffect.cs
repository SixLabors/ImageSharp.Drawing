// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Provides a shader-accelerated counterpart to <see cref="BackdropBlurLayerEffect"/>.
/// </summary>
/// <remarks>
/// This effect can be used with both WebGPU and CPU drawing backends. WebGPU blurs the backdrop with separable
/// shader passes; a CPU backend applies the equivalent <see cref="BackdropBlurLayerEffect"/>.
/// </remarks>
public sealed class WebGPUBackdropGaussianBlurLayerEffect : WebGPUBackdropShaderLayerEffect
{
    /// <summary>
    /// Initializes a new instance of the <see cref="WebGPUBackdropGaussianBlurLayerEffect"/> class.
    /// </summary>
    /// <param name="sigma">The Gaussian blur sigma, in pixels; zero leaves the backdrop unchanged.</param>
    public WebGPUBackdropGaussianBlurLayerEffect(float sigma)
        : base(
            new BackdropBlurLayerEffect(sigma),
            WebGPUGaussianBlurLayerEffect.ShaderSource,
            WebGPUGaussianBlurLayerEffect.UniformLayout)
    {
        this.Sigma = sigma;

        // Backdrop capture changes the source snapshot, but Gaussian sampling is otherwise
        // identical. Copy the content adapter's passes into this effect's base-owned sequence.
        WebGPUGaussianBlurLayerEffect shaderEffect = new(sigma);
        this.AddShaderPasses(shaderEffect.GetShaderPasses());
    }

    /// <summary>
    /// Gets the Gaussian blur sigma, in pixels.
    /// </summary>
    public float Sigma { get; }
}
