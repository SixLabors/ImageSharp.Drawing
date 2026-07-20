// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Provides a shader-accelerated counterpart to <see cref="GlowLayerEffect"/>.
/// </summary>
/// <remarks>
/// This effect can be used with both WebGPU and CPU drawing backends. WebGPU creates the glow with shader passes;
/// a CPU backend applies the equivalent <see cref="GlowLayerEffect"/>.
/// </remarks>
public sealed class WebGPUGlowLayerEffect : WebGPUShaderLayerEffect
{
    /// <summary>
    /// Initializes a new instance of the <see cref="WebGPUGlowLayerEffect"/> class.
    /// </summary>
    /// <param name="sigma">The Gaussian blur sigma, in pixels.</param>
    /// <param name="color">The glow colour.</param>
    public WebGPUGlowLayerEffect(float sigma, Color color)
        : base(
            new GlowLayerEffect(sigma, color),
            WebGPUColorMatrixLayerEffect.ShaderSource,
            WebGPUColorMatrixLayerEffect.UniformLayout)
    {
        this.Sigma = sigma;
        this.Color = color;
        this.AddPasses(sigma, color);
    }

    /// <summary>
    /// Gets the Gaussian blur sigma, in pixels.
    /// </summary>
    public float Sigma { get; }

    /// <summary>
    /// Gets the glow colour.
    /// </summary>
    public Color Color { get; }

    /// <summary>
    /// Adds the tint and blur passes which form the glow image.
    /// </summary>
    /// <param name="sigma">The Gaussian blur sigma, in pixels.</param>
    /// <param name="color">The glow colour.</param>
    private void AddPasses(float sigma, Color color)
    {
        Vector4 vector = color.ToScaledVector4(PixelAlphaRepresentation.Unassociated);

        // Zero RGB rows replace source colour with the constant glow colour. Scaling alpha by the
        // colour alpha retains the source silhouette at the requested glow opacity.
        ColorMatrix tint = default;
        tint.M44 = vector.W;
        tint.M51 = vector.X;
        tint.M52 = vector.Y;
        tint.M53 = vector.Z;

        WebGPUColorMatrixLayerEffect tintEffect = new(tint);
        WebGPUGaussianBlurLayerEffect blurEffect = new(sigma);
        ReadOnlySpan<WebGPUShaderPass> tintPasses = tintEffect.GetShaderPasses();
        ReadOnlySpan<WebGPUShaderPass> blurPasses = blurEffect.GetShaderPasses();

        // Tint before blur so the expanded pixels contain only the requested glow colour. Glow
        // requires sigma greater than zero, so every valid instance has both pass sequences.
        this.AddShaderPasses(tintPasses);
        this.AddShaderPasses(blurPasses);
    }
}
