// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Provides a shader-accelerated counterpart to <see cref="BackdropAcrylicLayerEffect"/>.
/// </summary>
/// <remarks>
/// This effect can be used with both WebGPU and CPU drawing backends. WebGPU blurs and tints the backdrop with shader
/// passes; a CPU backend applies the equivalent <see cref="BackdropAcrylicLayerEffect"/>.
/// </remarks>
public sealed class WebGPUBackdropAcrylicLayerEffect : WebGPUBackdropShaderLayerEffect
{
    /// <summary>
    /// Initializes a new instance of the <see cref="WebGPUBackdropAcrylicLayerEffect"/> class.
    /// </summary>
    /// <param name="sigma">The Gaussian blur sigma, in pixels.</param>
    /// <param name="tint">The tint blended over the blurred backdrop.</param>
    public WebGPUBackdropAcrylicLayerEffect(float sigma, Color tint)
        : base(
            new BackdropAcrylicLayerEffect(sigma, tint),
            WebGPUColorMatrixLayerEffect.ShaderSource,
            WebGPUColorMatrixLayerEffect.UniformLayout)
    {
        this.Sigma = sigma;
        this.Tint = tint;
        this.AddPasses(sigma, tint);
    }

    /// <summary>
    /// Gets the Gaussian blur sigma, in pixels.
    /// </summary>
    public float Sigma { get; }

    /// <summary>
    /// Gets the tint blended over the blurred backdrop.
    /// </summary>
    public Color Tint { get; }

    /// <summary>
    /// Adds the blur and tint passes which implement the acrylic effect.
    /// </summary>
    /// <param name="sigma">The Gaussian blur sigma, in pixels.</param>
    /// <param name="color">The acrylic tint.</param>
    private void AddPasses(float sigma, Color color)
    {
        Vector4 vector = color.ToScaledVector4(PixelAlphaRepresentation.Unassociated);

        // Source RGB is retained by (1 - tint alpha), while the constant row contributes the
        // premultiplied tint. Alpha is preserved so the backdrop keeps its existing coverage.
        float keep = 1F - vector.W;
        ColorMatrix tint = default;
        tint.M11 = keep;
        tint.M22 = keep;
        tint.M33 = keep;
        tint.M44 = 1F;
        tint.M51 = vector.X * vector.W;
        tint.M52 = vector.Y * vector.W;
        tint.M53 = vector.Z * vector.W;

        WebGPUColorMatrixLayerEffect tintEffect = new(tint);
        ReadOnlySpan<WebGPUShaderPass> tintPasses = tintEffect.GetShaderPasses();

        if (sigma == 0)
        {
            this.AddShaderPasses(tintPasses);
            return;
        }

        WebGPUGaussianBlurLayerEffect blurEffect = new(sigma);
        ReadOnlySpan<WebGPUShaderPass> blurPasses = blurEffect.GetShaderPasses();

        // Acrylic first blurs the captured backdrop, then blends the tint over that result.
        this.AddShaderPasses(blurPasses);
        this.AddShaderPasses(tintPasses);
    }
}
