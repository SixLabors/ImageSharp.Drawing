// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;

namespace SixLabors.ImageSharp.Drawing.Processing;

/// <summary>
/// Describes an image effect applied to the contents of a compositing layer when it is restored.
/// The layer isolates the content the effect operates on, so the effect sees only what was drawn
/// into the layer, against transparency, before the result is composited onto the canvas.
/// </summary>
public abstract class LayerEffect
{
    private readonly bool isPassThrough;
    private readonly GraphicsOptions? writeBackOptions;
    private readonly Point writeBackOffset;
    private readonly Action<IImageProcessingContext> operation;
    private readonly int reach;

    /// <summary>
    /// Initializes a new instance of the <see cref="LayerEffect"/> class.
    /// </summary>
    /// <param name="operation">The operation that implements the effect.</param>
    protected LayerEffect(Action<IImageProcessingContext> operation)
    {
        Guard.NotNull(operation, nameof(operation));
        this.operation = operation;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="LayerEffect"/> class with the observable
    /// behavior of another effect when this effect is not executed natively.
    /// </summary>
    /// <param name="fallbackEffect">The effect whose behavior is used as the fallback.</param>
    protected LayerEffect(LayerEffect fallbackEffect)
    {
        Guard.NotNull(fallbackEffect, nameof(fallbackEffect));
        this.isPassThrough = fallbackEffect.IsPassThrough;
        this.writeBackOptions = fallbackEffect.WriteBackOptions;
        this.writeBackOffset = fallbackEffect.WriteBackOffset;
        this.operation = fallbackEffect.CreateOperation();
        this.reach = fallbackEffect.Reach;
    }

    /// <summary>
    /// Gets the distance, in pixels, the effect can push content beyond its source region. Layer
    /// and processing bounds are expanded by this reach so the effect output is not cut off.
    /// Effects constructed over a fallback inherit the fallback's reach.
    /// </summary>
    public virtual int Reach => this.reach;

    /// <summary>
    /// Gets a value indicating whether the effect leaves the layer content unchanged, in which
    /// case its application is skipped entirely.
    /// </summary>
    public virtual bool IsPassThrough => this.isPassThrough;

    /// <summary>
    /// Gets the graphics options used to composite the processed pixels back onto the layer, or
    /// <see langword="null"/> to replace the processed region outright.
    /// </summary>
    public virtual GraphicsOptions? WriteBackOptions => this.writeBackOptions;

    /// <summary>
    /// Gets the offset, in pixels, at which the processed pixels are written back relative to the
    /// region they were read from.
    /// </summary>
    public virtual Point WriteBackOffset => this.writeBackOffset;

    /// <summary>
    /// Creates the image-processing operation that transforms the layer snapshot into the effect
    /// output.
    /// </summary>
    /// <returns>The operation to run against the layer snapshot.</returns>
    internal Action<IImageProcessingContext> CreateOperation() => this.operation;
}

/// <summary>
/// Blurs the contents of a compositing layer when it is restored.
/// </summary>
public sealed class BlurLayerEffect : LayerEffect
{
    /// <summary>
    /// Initializes a new instance of the <see cref="BlurLayerEffect"/> class.
    /// </summary>
    /// <param name="sigma">The Gaussian blur sigma, in pixels; zero leaves the content unchanged.</param>
    public BlurLayerEffect(float sigma)
        : base(context => context.GaussianBlur(sigma))
    {
        Guard.MustBeGreaterThanOrEqualTo(sigma, 0, nameof(sigma));
        this.Sigma = sigma;
    }

    /// <summary>
    /// Gets the Gaussian blur sigma, in pixels.
    /// </summary>
    public float Sigma { get; }

    /// <inheritdoc/>
    public override int Reach => (int)MathF.Ceiling(this.Sigma * 3F) + 1;

    /// <inheritdoc/>
    public override bool IsPassThrough => this.Sigma == 0;

    /// <inheritdoc/>
    public override GraphicsOptions? WriteBackOptions => null;

    /// <inheritdoc/>
    public override Point WriteBackOffset => default;
}

/// <summary>
/// Composites a drop shadow beneath the contents of a compositing layer when it is restored. The
/// shadow is the content's own silhouette tinted with the shadow colour, blurred, and offset.
/// </summary>
public sealed class DropShadowLayerEffect : LayerEffect
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DropShadowLayerEffect"/> class.
    /// </summary>
    /// <param name="offset">The shadow offset, in pixels.</param>
    /// <param name="sigma">The Gaussian blur sigma, in pixels; zero draws a hard shadow.</param>
    /// <param name="color">
    /// The shadow colour. Its alpha scales the content's alpha, so a semi-transparent colour draws
    /// a semi-transparent shadow.
    /// </param>
    public DropShadowLayerEffect(Point offset, float sigma, Color color)
        : base(CreateOperation(sigma, color))
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
    public override int Reach
        => (int)MathF.Ceiling(this.Sigma * 3F) + Math.Max(Math.Abs(this.Offset.X), Math.Abs(this.Offset.Y)) + 1;

    /// <inheritdoc/>
    public override GraphicsOptions? WriteBackOptions
        => new() { AlphaCompositionMode = PixelAlphaCompositionMode.DestOver };

    /// <inheritdoc/>
    public override Point WriteBackOffset => this.Offset;

    /// <summary>
    /// Creates the CPU drop-shadow operation for the supplied effect values.
    /// </summary>
    /// <param name="sigma">The Gaussian blur sigma.</param>
    /// <param name="color">The shadow colour.</param>
    /// <returns>The configured image-processing operation.</returns>
    private static Action<IImageProcessingContext> CreateOperation(float sigma, Color color)
    {
        // The tint replaces the snapshot's colour with the constant shadow colour and scales its
        // alpha. The colour filter operates on straight alpha, so with the RGB rows zeroed the
        // constant row sets the colour outright; because the colour is then constant across the
        // snapshot, the blur only spreads alpha and cannot bleed foreign colours into the shadow.
        Vector4 vector = color.ToPixel<Rgba32>().ToScaledVector4();
        ColorMatrix tint = default;
        tint.M44 = vector.W;
        tint.M51 = vector.X;
        tint.M52 = vector.Y;
        tint.M53 = vector.Z;

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
/// Composites a glow beneath the contents of a compositing layer when it is restored: the content's
/// own silhouette tinted with the glow colour and blurred, spreading evenly in all directions.
/// </summary>
public sealed class GlowLayerEffect : LayerEffect
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GlowLayerEffect"/> class.
    /// </summary>
    /// <param name="sigma">The Gaussian blur sigma, in pixels, controlling the glow's spread.</param>
    /// <param name="color">
    /// The glow colour. Its alpha scales the content's alpha, so a semi-transparent colour draws a
    /// fainter glow.
    /// </param>
    public GlowLayerEffect(float sigma, Color color)
        : base(CreateOperation(sigma, color))
    {
        Guard.MustBeGreaterThan(sigma, 0, nameof(sigma));
        this.Sigma = sigma;
        this.Color = color;
    }

    /// <summary>
    /// Gets the Gaussian blur sigma, in pixels, controlling the glow's spread.
    /// </summary>
    public float Sigma { get; }

    /// <summary>
    /// Gets the glow colour.
    /// </summary>
    public Color Color { get; }

    /// <inheritdoc/>
    public override int Reach => (int)MathF.Ceiling(this.Sigma * 3F) + 1;

    /// <inheritdoc/>
    public override GraphicsOptions? WriteBackOptions
        => new() { AlphaCompositionMode = PixelAlphaCompositionMode.DestOver };

    /// <inheritdoc/>
    public override Point WriteBackOffset => default;

    /// <summary>
    /// Creates the CPU glow operation for the supplied effect values.
    /// </summary>
    /// <param name="sigma">The Gaussian blur sigma.</param>
    /// <param name="color">The glow colour.</param>
    /// <returns>The configured image-processing operation.</returns>
    private static Action<IImageProcessingContext> CreateOperation(float sigma, Color color)
    {
        // The tint replaces the snapshot's colour with the constant glow colour and scales its
        // alpha. The colour filter operates on straight alpha, so with the RGB rows zeroed the
        // constant row sets the colour outright; because the colour is then constant across the
        // snapshot, the blur only spreads alpha and cannot bleed foreign colours into the glow.
        Vector4 vector = color.ToPixel<Rgba32>().ToScaledVector4();
        ColorMatrix tint = default;
        tint.M44 = vector.W;
        tint.M51 = vector.X;
        tint.M52 = vector.Y;
        tint.M53 = vector.Z;

        return context =>
        {
            context.Filter(tint);
            context.GaussianBlur(sigma);
        };
    }
}

/// <summary>
/// Composites a shadow inside the contents of a compositing layer when it is restored: the darkness
/// outside the content's silhouette is tinted with the shadow colour, blurred, offset, and clipped
/// onto the content itself, so the shadow hugs the content's inside edges.
/// </summary>
public sealed class InnerShadowLayerEffect : LayerEffect
{
    /// <summary>
    /// Initializes a new instance of the <see cref="InnerShadowLayerEffect"/> class.
    /// </summary>
    /// <param name="offset">
    /// The shadow offset, in pixels. Positive values shade the content's top and left inside edges,
    /// matching a CSS inset shadow.
    /// </param>
    /// <param name="sigma">The Gaussian blur sigma, in pixels; zero draws a hard shadow.</param>
    /// <param name="color">
    /// The shadow colour. Its alpha scales the shadow's alpha, so a semi-transparent colour draws a
    /// semi-transparent shadow.
    /// </param>
    public InnerShadowLayerEffect(Point offset, float sigma, Color color)
        : base(CreateOperation(sigma, color))
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
    public override int Reach
        => (int)MathF.Ceiling(this.Sigma * 3F) + Math.Max(Math.Abs(this.Offset.X), Math.Abs(this.Offset.Y)) + 1;

    /// <inheritdoc/>
    public override GraphicsOptions? WriteBackOptions
        => new() { AlphaCompositionMode = PixelAlphaCompositionMode.SrcAtop };

    /// <inheritdoc/>
    public override Point WriteBackOffset => this.Offset;

    /// <summary>
    /// Creates the CPU inner-shadow operation for the supplied effect values.
    /// </summary>
    /// <param name="sigma">The Gaussian blur sigma.</param>
    /// <param name="color">The shadow colour.</param>
    /// <returns>The configured image-processing operation.</returns>
    private static Action<IImageProcessingContext> CreateOperation(float sigma, Color color)
    {
        // The shadow is the blurred INVERSE of the content's silhouette: everywhere the content is
        // not, tinted with the shadow colour. The matrix maps alpha to colourAlpha * (1 - alpha)
        // with the constant RGB rows carrying the shadow colour, the blur feathers the boundary,
        // and the SrcAtop write-back clips the result onto the content so only the parts that
        // reach inside its edges remain.
        Vector4 vector = color.ToPixel<Rgba32>().ToScaledVector4();
        ColorMatrix invertedTint = default;
        invertedTint.M44 = -vector.W;
        invertedTint.M54 = vector.W;
        invertedTint.M51 = vector.X;
        invertedTint.M52 = vector.Y;
        invertedTint.M53 = vector.Z;

        return context =>
        {
            context.Filter(invertedTint);
            if (sigma > 0)
            {
                context.GaussianBlur(sigma);
            }
        };
    }
}

/// <summary>
/// Transforms the colours of a compositing layer's contents through a colour matrix when the layer
/// is restored. Covers the CSS filter family: grayscale, sepia, saturation, hue rotation,
/// brightness, contrast, and invert are all colour matrices.
/// </summary>
public sealed class ColorMatrixLayerEffect : LayerEffect
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ColorMatrixLayerEffect"/> class.
    /// </summary>
    /// <param name="matrix">The colour matrix applied to the layer content.</param>
    public ColorMatrixLayerEffect(ColorMatrix matrix)
        : base(context => context.Filter(matrix))
        => this.Matrix = matrix;

    /// <summary>
    /// Gets the colour matrix applied to the layer content.
    /// </summary>
    public ColorMatrix Matrix { get; }

    /// <inheritdoc/>
    public override int Reach => 0;

    /// <inheritdoc/>
    public override GraphicsOptions? WriteBackOptions => null;

    /// <inheritdoc/>
    public override Point WriteBackOffset => default;

    /// <inheritdoc/>
    public override bool IsPassThrough => this.Matrix == ColorMatrix.Identity;
}
