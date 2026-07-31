// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Provides a shader-accelerated counterpart to <see cref="BackdropDropShadowLayerEffect"/>.
/// </summary>
/// <remarks>
/// This effect can be used with both WebGPU and CPU drawing backends. WebGPU creates the backdrop shadow with shader
/// passes; a CPU backend applies the equivalent <see cref="BackdropDropShadowLayerEffect"/>.
/// </remarks>
public sealed class WebGPUBackdropDropShadowLayerEffect : WebGPUBackdropShaderLayerEffect
{
    /// <summary>
    /// Initializes a new instance of the <see cref="WebGPUBackdropDropShadowLayerEffect"/> class.
    /// </summary>
    /// <param name="offset">The shadow offset, in pixels.</param>
    /// <param name="sigma">The Gaussian blur sigma, in pixels; zero draws a hard shadow.</param>
    /// <param name="color">The shadow colour.</param>
    public WebGPUBackdropDropShadowLayerEffect(Point offset, float sigma, Color color)
        : base(
            new BackdropDropShadowLayerEffect(offset, sigma, color),
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
    /// Adds the tint and blur passes which form the backdrop shadow image.
    /// </summary>
    /// <param name="sigma">The Gaussian blur sigma, in pixels.</param>
    /// <param name="color">The shadow colour.</param>
    private void AddPasses(float sigma, Color color)
    {
        Vector4 vector = color.ToScaledVector4(PixelAlphaRepresentation.Unassociated);

        // Zero RGB rows replace the backdrop colour with the constant shadow colour. Scaling alpha
        // by the colour alpha preserves the backdrop silhouette at the requested shadow opacity.
        ColorMatrix tint = default;
        tint.M44 = vector.W;
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

        // Tinting before blur spreads only the requested constant shadow colour. The inherited
        // write-back metadata applies Offset after these passes, so it is not a shader uniform.
        this.AddShaderPasses(tintPasses);
        this.AddShaderPasses(blurPasses);
    }
}
