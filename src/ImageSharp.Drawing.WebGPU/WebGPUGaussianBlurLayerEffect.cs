// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using SixLabors.ImageSharp.Processing.Processors.Convolution;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Provides a shader-accelerated counterpart to <see cref="BlurLayerEffect"/>.
/// </summary>
/// <remarks>
/// This effect can be used with both WebGPU and CPU drawing backends. WebGPU performs the Gaussian blur with
/// separable shader passes; a CPU backend applies the equivalent <see cref="BlurLayerEffect"/>.
/// </remarks>
public sealed class WebGPUGaussianBlurLayerEffect : WebGPUShaderLayerEffect
{
    internal const string ShaderSource = """
        fn layer_effect(position: vec2<f32>) -> vec4<f32> {

            // The unpaired center tap is sampled exactly at the output position.
            var result = layer_sample(position) * imagesharp_uniforms.center_weight;
            var first_weight = imagesharp_uniforms.center_weight * imagesharp_uniforms.weight_step;
            var second_ratio = imagesharp_uniforms.weight_step * imagesharp_uniforms.weight_step * imagesharp_uniforms.weight_step;
            let ratio_squared = imagesharp_uniforms.weight_step * imagesharp_uniforms.weight_step;
            let ratio_growth = ratio_squared * ratio_squared;

            for (var distance = 1u; distance <= imagesharp_uniforms.radius; distance += 2u) {

                // G(d + 1) / G(d) is q^(2d + 1), where q = exp(-1 / (2 sigma^2)).
                // The recurrence keeps the program independent of sigma and avoids evaluating
                // exp() for every output pixel while retaining bilinear paired-tap sampling.
                let second_weight = select(0.0, first_weight * second_ratio, distance < imagesharp_uniforms.radius);
                let combined_weight = first_weight + second_weight;
                let sample_distance =
                    ((f32(distance) * first_weight) + (f32(distance + 1u) * second_weight)) / combined_weight;
                let offset = imagesharp_uniforms.direction * sample_distance;
                result += (layer_sample(position - offset) + layer_sample(position + offset)) * combined_weight;

                // Advancing by two taps multiplies by q^(2d + 1) and q^(2d + 3).
                // The ratio for the following pair grows by q^4.
                first_weight = second_weight * second_ratio * ratio_squared;
                second_ratio *= ratio_growth;
            }

            return result;
        }
        """;

    internal static readonly WebGPUShaderUniformLayout UniformLayout = new(
    [
        new WebGPUShaderUniform("direction", WebGPUShaderUniformType.Vector2, 1),
        new WebGPUShaderUniform("center_weight", WebGPUShaderUniformType.Float32, 1),
        new WebGPUShaderUniform("radius", WebGPUShaderUniformType.UInt32, 1),
        new WebGPUShaderUniform("weight_step", WebGPUShaderUniformType.Float32, 1)
    ]);

    /// <summary>
    /// Initializes a new instance of the <see cref="WebGPUGaussianBlurLayerEffect"/> class.
    /// </summary>
    /// <param name="sigma">The Gaussian blur sigma, in pixels; zero leaves the content unchanged.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="sigma"/> is negative.
    /// </exception>
    public WebGPUGaussianBlurLayerEffect(float sigma)
        : base(new BlurLayerEffect(sigma), ShaderSource, UniformLayout)
    {
        this.Sigma = sigma;

        // ImageSharp truncates its Gaussian at three standard deviations. The CPU-computed center
        // weight preserves the same normalized kernel, while q lets the shader reconstruct every
        // remaining weight through the exact Gaussian recurrence without sigma-specific WGSL.
        int radius = (int)MathF.Ceiling(sigma * 3F);
        DenseMatrix<float> kernel = CreateKernel(radius, sigma);
        float weightStep = radius == 0 ? 1F : MathF.Exp(-1F / (2F * sigma * sigma));

        // Gaussian convolution is separable: the horizontal result convolved vertically with the
        // same one-dimensional kernel is exactly the corresponding two-dimensional Gaussian.
        // GaussianBlurProcessor uses Repeat on both axes: samples beyond the requested processing
        // rectangle repeat its nearest edge pixel instead of introducing transparent texels.
        this.AddShaderPass(BorderWrappingMode.Repeat, BorderWrappingMode.Repeat, uniforms =>
        {
            uniforms.SetVector2("direction", Vector2.UnitX);
            uniforms.SetFloat32("center_weight", kernel[0, radius]);
            uniforms.SetUInt32("radius", (uint)radius);
            uniforms.SetFloat32("weight_step", weightStep);
        });

        this.AddShaderPass(BorderWrappingMode.Repeat, BorderWrappingMode.Repeat, uniforms =>
        {
            uniforms.SetVector2("direction", Vector2.UnitY);
            uniforms.SetFloat32("center_weight", kernel[0, radius]);
            uniforms.SetUInt32("radius", (uint)radius);
            uniforms.SetFloat32("weight_step", weightStep);
        });
    }

    /// <summary>
    /// Gets the Gaussian blur sigma, in pixels.
    /// </summary>
    public float Sigma { get; }

    /// <summary>
    /// Creates the normalized one-dimensional Gaussian convolution kernel.
    /// </summary>
    /// <param name="radius">The number of sampled pixels on either side of the center.</param>
    /// <param name="sigma">The Gaussian standard deviation, in pixels.</param>
    /// <returns>A one-row matrix containing the kernel from negative radius through positive radius.</returns>
    private static DenseMatrix<float> CreateKernel(int radius, float sigma)
    {
        if (radius == 0)
        {
            // A zero-radius convolution is the identity. This also avoids evaluating G(0) with a
            // zero sigma, whose formula contains divisions by zero.
            DenseMatrix<float> identity = new(1, 1);
            identity[0, 0] = 1F;
            return identity;
        }

        int size = checked((radius * 2) + 1);

        // A single row is the complete kernel because Gaussian convolution is separable. Both
        // shader passes consume these same normalized weights along their respective axes.
        DenseMatrix<float> kernel = new(size, 1);
        Span<float> weights = kernel.Span;
        float sum = 0F;
        float midpoint = (size - 1) / 2F;

        // This is the same discrete Gaussian G(x) used by ImageSharp's Gaussian blur processor.
        // Keeping the unnormalized weights first permits one normalization pass after their sum is known.
        for (int i = 0; i < size; i++)
        {
            float x = i - midpoint;
            const float numerator = 1F;
            float denominator = MathF.Sqrt(2 * MathF.PI) * sigma;
            float exponentNumerator = -x * x;
            float exponentDenominator = 2 * (sigma * sigma);
            float left = numerator / denominator;
            float right = MathF.Exp(exponentNumerator / exponentDenominator);
            float weight = left * right;
            sum += weight;
            weights[i] = weight;
        }

        // Unit-sum weights preserve a constant image and prevent either pass changing brightness.
        for (int i = 0; i < size; i++)
        {
            weights[i] /= sum;
        }

        return kernel;
    }
}
