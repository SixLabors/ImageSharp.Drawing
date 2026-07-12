// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;

namespace SixLabors.ImageSharp.Drawing.Processing;

/// <summary>
/// Describes an effect applied to the backdrop of a compositing layer: the pixels already on the
/// canvas beneath the layer's region are filtered when the layer is opened, and the layer's content
/// then renders above the filtered backdrop. This is the CSS <c>backdrop-filter</c> model; the
/// filtered result is clipped to the layer's region.
/// </summary>
public abstract class BackdropLayerEffect : LayerEffect
{
    /// <summary>
    /// Initializes a new instance of the <see cref="BackdropLayerEffect"/> class.
    /// </summary>
    private protected BackdropLayerEffect()
    {
    }

    /// <summary>
    /// Gets the reach of a backdrop effect, which is always zero: the filtered backdrop is clipped
    /// to the layer's region, so the region is never expanded.
    /// </summary>
    internal sealed override int Reach => 0;

    /// <inheritdoc/>
    internal override GraphicsOptions? WriteBackOptions => null;

    /// <inheritdoc/>
    internal override Point WriteBackOffset => default;
}

/// <summary>
/// Blurs the backdrop beneath a compositing layer. The CSS equivalent is
/// <c>backdrop-filter: blur()</c>.
/// </summary>
public sealed class BackdropBlurLayerEffect : BackdropLayerEffect
{
    /// <summary>
    /// Initializes a new instance of the <see cref="BackdropBlurLayerEffect"/> class.
    /// </summary>
    /// <param name="sigma">The Gaussian blur sigma, in pixels; zero leaves the backdrop unchanged.</param>
    public BackdropBlurLayerEffect(float sigma)
    {
        Guard.MustBeGreaterThanOrEqualTo(sigma, 0, nameof(sigma));
        this.Sigma = sigma;
    }

    /// <summary>
    /// Gets the Gaussian blur sigma, in pixels.
    /// </summary>
    public float Sigma { get; }

    /// <inheritdoc/>
    internal override bool IsPassThrough => this.Sigma == 0;

    /// <inheritdoc/>
    internal override Action<IImageProcessingContext> CreateOperation()
    {
        float sigma = this.Sigma;
        return context => context.GaussianBlur(sigma);
    }
}

/// <summary>
/// Blurs and tints the backdrop beneath a compositing layer, producing a frosted-glass acrylic
/// material. This is the tinted extension of <see cref="BackdropBlurLayerEffect"/> and has no CSS
/// equivalent; CSS composes the tint from the element's own translucent background instead.
/// </summary>
public sealed class BackdropAcrylicLayerEffect : BackdropLayerEffect
{
    /// <summary>
    /// Initializes a new instance of the <see cref="BackdropAcrylicLayerEffect"/> class.
    /// </summary>
    /// <param name="sigma">The Gaussian blur sigma, in pixels.</param>
    /// <param name="tint">
    /// The tint blended over the blurred backdrop. Its alpha controls the tint strength, so an
    /// opaque tint hides the backdrop entirely.
    /// </param>
    public BackdropAcrylicLayerEffect(float sigma, Color tint)
    {
        Guard.MustBeGreaterThanOrEqualTo(sigma, 0, nameof(sigma));
        this.Sigma = sigma;
        this.Tint = tint;
    }

    /// <summary>
    /// Gets the Gaussian blur sigma, in pixels.
    /// </summary>
    public float Sigma { get; }

    /// <summary>
    /// Gets the tint blended over the blurred backdrop.
    /// </summary>
    public Color Tint { get; }

    /// <inheritdoc/>
    internal override Action<IImageProcessingContext> CreateOperation()
    {
        // Blending a constant colour over the backdrop is linear, so it is expressed as a colour
        // matrix: the backdrop's channels scale by one minus the tint's alpha and the constant row
        // carries the pre-scaled tint. The backdrop's own alpha is preserved.
        Vector4 vector = this.Tint.ToPixel<Rgba32>().ToScaledVector4();
        float keep = 1F - vector.W;
        ColorMatrix tint = default;
        tint.M11 = keep;
        tint.M22 = keep;
        tint.M33 = keep;
        tint.M44 = 1F;
        tint.M51 = vector.X * vector.W;
        tint.M52 = vector.Y * vector.W;
        tint.M53 = vector.Z * vector.W;

        float sigma = this.Sigma;
        return context =>
        {
            if (sigma > 0)
            {
                context.GaussianBlur(sigma);
            }

            context.Filter(tint);
        };
    }
}

/// <summary>
/// Composites a drop shadow of the backdrop's silhouette beneath it. The CSS equivalent is
/// <c>backdrop-filter: drop-shadow()</c>.
/// </summary>
public sealed class BackdropDropShadowLayerEffect : BackdropLayerEffect
{
    /// <summary>
    /// Initializes a new instance of the <see cref="BackdropDropShadowLayerEffect"/> class.
    /// </summary>
    /// <param name="offset">The shadow offset, in pixels.</param>
    /// <param name="sigma">The Gaussian blur sigma, in pixels; zero draws a hard shadow.</param>
    /// <param name="color">The shadow colour. Its alpha scales the backdrop's alpha.</param>
    public BackdropDropShadowLayerEffect(Point offset, float sigma, Color color)
    {
        Guard.MustBeGreaterThanOrEqualTo(sigma, 0, nameof(sigma));
        this.Offset = offset;
        this.Sigma = sigma;
        this.Color = color;
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

    /// <inheritdoc/>
    internal override GraphicsOptions? WriteBackOptions
        => new() { AlphaCompositionMode = PixelAlphaCompositionMode.DestOver };

    /// <inheritdoc/>
    internal override Point WriteBackOffset => this.Offset;

    /// <inheritdoc/>
    internal override Action<IImageProcessingContext> CreateOperation()
    {
        // The tint replaces the backdrop's colour with the constant shadow colour and scales its
        // alpha; the DestOver write-back slots the blurred silhouette beneath the untouched
        // backdrop at the offset.
        Vector4 vector = this.Color.ToPixel<Rgba32>().ToScaledVector4();
        ColorMatrix tint = default;
        tint.M44 = vector.W;
        tint.M51 = vector.X;
        tint.M52 = vector.Y;
        tint.M53 = vector.Z;

        float sigma = this.Sigma;
        return context =>
        {
            context.Filter(tint);
            if (sigma > 0)
            {
                context.GaussianBlur(sigma);
            }
        };
    }
}

/// <summary>
/// Transforms the colours of the backdrop beneath a compositing layer through a colour matrix. The
/// named backdrop colour effects all specialize this; use it directly for matrices they do not
/// cover.
/// </summary>
public class BackdropColorMatrixLayerEffect : BackdropLayerEffect
{
    /// <summary>
    /// Initializes a new instance of the <see cref="BackdropColorMatrixLayerEffect"/> class.
    /// </summary>
    /// <param name="matrix">The colour matrix applied to the backdrop.</param>
    public BackdropColorMatrixLayerEffect(ColorMatrix matrix)
        => this.Matrix = matrix;

    /// <summary>
    /// Gets the colour matrix applied to the backdrop.
    /// </summary>
    public ColorMatrix Matrix { get; }

    /// <inheritdoc/>
    internal override bool IsPassThrough => this.Matrix == ColorMatrix.Identity;

    /// <inheritdoc/>
    internal override Action<IImageProcessingContext> CreateOperation()
    {
        ColorMatrix matrix = this.Matrix;
        return context => context.Filter(matrix);
    }
}

/// <summary>
/// Adjusts the brightness of the backdrop beneath a compositing layer. The CSS equivalent is
/// <c>backdrop-filter: brightness()</c>.
/// </summary>
public sealed class BackdropBrightnessLayerEffect : BackdropColorMatrixLayerEffect
{
    /// <summary>
    /// Initializes a new instance of the <see cref="BackdropBrightnessLayerEffect"/> class.
    /// </summary>
    /// <param name="amount">The brightness amount: 0 is black, 1 leaves the backdrop unchanged, and values above 1 brighten.</param>
    public BackdropBrightnessLayerEffect(float amount)
        : base(KnownFilterMatrices.CreateBrightnessFilter(amount))
        => this.Amount = amount;

    /// <summary>
    /// Gets the brightness amount.
    /// </summary>
    public float Amount { get; }
}

/// <summary>
/// Adjusts the contrast of the backdrop beneath a compositing layer. The CSS equivalent is
/// <c>backdrop-filter: contrast()</c>.
/// </summary>
public sealed class BackdropContrastLayerEffect : BackdropColorMatrixLayerEffect
{
    /// <summary>
    /// Initializes a new instance of the <see cref="BackdropContrastLayerEffect"/> class.
    /// </summary>
    /// <param name="amount">The contrast amount: 0 is fully grey, 1 leaves the backdrop unchanged, and values above 1 increase contrast.</param>
    public BackdropContrastLayerEffect(float amount)
        : base(KnownFilterMatrices.CreateContrastFilter(amount))
        => this.Amount = amount;

    /// <summary>
    /// Gets the contrast amount.
    /// </summary>
    public float Amount { get; }
}

/// <summary>
/// Desaturates the backdrop beneath a compositing layer towards grayscale using ITU-R BT.709
/// luminance coefficients, matching the CSS definition. The CSS equivalent is
/// <c>backdrop-filter: grayscale()</c>.
/// </summary>
public sealed class BackdropGrayscaleLayerEffect : BackdropColorMatrixLayerEffect
{
    /// <summary>
    /// Initializes a new instance of the <see cref="BackdropGrayscaleLayerEffect"/> class.
    /// </summary>
    /// <param name="amount">The grayscale amount between 0 and 1: 0 leaves the backdrop unchanged and 1 is fully grayscale.</param>
    public BackdropGrayscaleLayerEffect(float amount)
        : base(KnownFilterMatrices.CreateGrayscaleBt709Filter(amount))
        => this.Amount = amount;

    /// <summary>
    /// Gets the grayscale amount.
    /// </summary>
    public float Amount { get; }
}

/// <summary>
/// Rotates the hue of the backdrop beneath a compositing layer. The CSS equivalent is
/// <c>backdrop-filter: hue-rotate()</c>.
/// </summary>
public sealed class BackdropHueRotateLayerEffect : BackdropColorMatrixLayerEffect
{
    /// <summary>
    /// Initializes a new instance of the <see cref="BackdropHueRotateLayerEffect"/> class.
    /// </summary>
    /// <param name="degrees">The hue rotation in degrees; 0 leaves the backdrop unchanged.</param>
    public BackdropHueRotateLayerEffect(float degrees)
        : base(KnownFilterMatrices.CreateHueFilter(degrees))
        => this.Degrees = degrees;

    /// <summary>
    /// Gets the hue rotation in degrees.
    /// </summary>
    public float Degrees { get; }
}

/// <summary>
/// Inverts the colours of the backdrop beneath a compositing layer. The CSS equivalent is
/// <c>backdrop-filter: invert()</c>.
/// </summary>
public sealed class BackdropInvertLayerEffect : BackdropColorMatrixLayerEffect
{
    /// <summary>
    /// Initializes a new instance of the <see cref="BackdropInvertLayerEffect"/> class.
    /// </summary>
    /// <param name="amount">The inversion amount between 0 and 1: 0 leaves the backdrop unchanged and 1 fully inverts.</param>
    public BackdropInvertLayerEffect(float amount)
        : base(KnownFilterMatrices.CreateInvertFilter(amount))
        => this.Amount = amount;

    /// <summary>
    /// Gets the inversion amount.
    /// </summary>
    public float Amount { get; }
}

/// <summary>
/// Scales the opacity of the backdrop beneath a compositing layer. The CSS equivalent is
/// <c>backdrop-filter: opacity()</c>.
/// </summary>
public sealed class BackdropOpacityLayerEffect : BackdropColorMatrixLayerEffect
{
    /// <summary>
    /// Initializes a new instance of the <see cref="BackdropOpacityLayerEffect"/> class.
    /// </summary>
    /// <param name="amount">The opacity amount between 0 and 1: 0 is fully transparent and 1 leaves the backdrop unchanged.</param>
    public BackdropOpacityLayerEffect(float amount)
        : base(KnownFilterMatrices.CreateOpacityFilter(amount))
        => this.Amount = amount;

    /// <summary>
    /// Gets the opacity amount.
    /// </summary>
    public float Amount { get; }
}

/// <summary>
/// Shifts the colours of the backdrop beneath a compositing layer towards sepia. The CSS equivalent
/// is <c>backdrop-filter: sepia()</c>.
/// </summary>
public sealed class BackdropSepiaLayerEffect : BackdropColorMatrixLayerEffect
{
    /// <summary>
    /// Initializes a new instance of the <see cref="BackdropSepiaLayerEffect"/> class.
    /// </summary>
    /// <param name="amount">The sepia amount between 0 and 1: 0 leaves the backdrop unchanged and 1 is fully sepia.</param>
    public BackdropSepiaLayerEffect(float amount)
        : base(KnownFilterMatrices.CreateSepiaFilter(amount))
        => this.Amount = amount;

    /// <summary>
    /// Gets the sepia amount.
    /// </summary>
    public float Amount { get; }
}

/// <summary>
/// Adjusts the colour saturation of the backdrop beneath a compositing layer. The CSS equivalent is
/// <c>backdrop-filter: saturate()</c>.
/// </summary>
public sealed class BackdropSaturateLayerEffect : BackdropColorMatrixLayerEffect
{
    /// <summary>
    /// Initializes a new instance of the <see cref="BackdropSaturateLayerEffect"/> class.
    /// </summary>
    /// <param name="amount">The saturation amount: 0 is fully desaturated, 1 leaves the backdrop unchanged, and values above 1 over-saturate.</param>
    public BackdropSaturateLayerEffect(float amount)
        : base(KnownFilterMatrices.CreateSaturateFilter(amount))
        => this.Amount = amount;

    /// <summary>
    /// Gets the saturation amount.
    /// </summary>
    public float Amount { get; }
}
