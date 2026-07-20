// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Provides a shader-accelerated counterpart to <see cref="InnerShadowLayerEffect"/>.
/// </summary>
/// <remarks>
/// This effect can be used with both WebGPU and CPU drawing backends. WebGPU creates the inner shadow with shader
/// passes; a CPU backend applies the equivalent <see cref="InnerShadowLayerEffect"/>.
/// </remarks>
public sealed class WebGPUInnerShadowLayerEffect : WebGPUShaderLayerEffect
{
    /// <summary>
    /// Initializes a new instance of the <see cref="WebGPUInnerShadowLayerEffect"/> class.
    /// </summary>
    /// <param name="offset">The shadow offset, in pixels.</param>
    /// <param name="sigma">The Gaussian blur sigma, in pixels; zero draws a hard shadow.</param>
    /// <param name="color">The shadow colour.</param>
    public WebGPUInnerShadowLayerEffect(Point offset, float sigma, Color color)
        : base(
            new InnerShadowLayerEffect(offset, sigma, color),
            WebGPUColorMatrixLayerEffect.ShaderSource,
            WebGPUColorMatrixLayerEffect.UniformLayout)
    {
        this.Offset = offset;
        this.Sigma = sigma;
        this.Color = color;
        this.AddPasses(sigma, color);
    }

    /// <summary>
    /// Gets the shadow offset, in pixels.
    /// </summary>
    public Point Offset { get; }

    /// <summary>
    /// Gets the Gaussian blur sigma, in pixels.
    /// </summary>
    public float Sigma { get; }

    /// <summary>
    /// Gets the shadow colour.
    /// </summary>
    public Color Color { get; }

    /// <summary>
    /// Adds the inverse-mask tint and blur passes which form the inner shadow image.
    /// </summary>
    /// <param name="sigma">The Gaussian blur sigma, in pixels.</param>
    /// <param name="color">The shadow colour.</param>
    private void AddPasses(float sigma, Color color)
    {
        Vector4 vector = color.ToScaledVector4(PixelAlphaRepresentation.Unassociated);

        // M44 = -a and M54 = a produce a * (1 - source alpha), creating the coloured inverse
        // silhouette which will feather into the content when blurred.
        ColorMatrix tint = default;
        tint.M44 = -vector.W;
        tint.M54 = vector.W;
        tint.M51 = vector.X;
        tint.M52 = vector.Y;
        tint.M53 = vector.Z;

        WebGPUColorMatrixLayerEffect tintEffect = new(tint);
        ReadOnlySpan<WebGPUShaderPass> tintPasses = tintEffect.GetShaderPasses();

        if (sigma == 0)
        {
            this.AddShaderPasses(tintPasses);
            return;
        }

        WebGPUGaussianBlurLayerEffect blurEffect = new(sigma);
        ReadOnlySpan<WebGPUShaderPass> blurPasses = blurEffect.GetShaderPasses();

        // Blur the inverse mask after tinting. The inherited write-back metadata applies Offset
        // and SrcAtop clipping after these passes, so neither concern belongs in shader uniforms.
        this.AddShaderPasses(tintPasses);
        this.AddShaderPasses(blurPasses);
    }
}
