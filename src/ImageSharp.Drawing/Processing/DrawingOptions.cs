// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;

namespace SixLabors.ImageSharp.Drawing.Processing;

/// <summary>
/// Provides options for influencing drawing operations, combining graphics rendering settings,
/// the fill-rule intersection mode, and an optional coordinate transform.
/// </summary>
public class DrawingOptions
{
    /// <summary>
    /// The default perceptual contrast boost applied to antialiased text rendering.
    /// </summary>
    public const float DefaultTextContrast = 0.5F;

    private GraphicsOptions graphicsOptions;

    /// <summary>
    /// Initializes a new instance of the <see cref="DrawingOptions"/> class.
    /// </summary>
    public DrawingOptions()
    {
        this.graphicsOptions = new GraphicsOptions();
        this.Transform = Matrix4x4.Identity;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DrawingOptions"/> class with explicit values.
    /// </summary>
    /// <param name="graphicsOptions">The graphics rendering options.</param>
    /// <param name="intersectionRule">The fill rule used to determine the interior of paths.</param>
    /// <param name="transform">The transform matrix applied to vector output before rasterization.</param>
    /// <param name="textContrast">The perceptual contrast boost applied to antialiased text rendering.</param>
    internal DrawingOptions(
        GraphicsOptions graphicsOptions,
        IntersectionRule intersectionRule,
        Matrix4x4 transform,
        float textContrast)
    {
        DebugGuard.NotNull(graphicsOptions, nameof(graphicsOptions));

        this.graphicsOptions = graphicsOptions;
        this.IntersectionRule = intersectionRule;
        this.Transform = transform;
        this.TextContrast = textContrast;
    }

    /// <summary>
    /// Gets or sets the graphics rendering options that control antialiasing, blending, alpha composition,
    /// and coverage thresholding for the drawing operation.
    /// </summary>
    public GraphicsOptions GraphicsOptions
    {
        get => this.graphicsOptions;
        set
        {
            Guard.NotNull(value, nameof(this.GraphicsOptions));
            this.graphicsOptions = value;
        }
    }

    /// <summary>
    /// Gets or sets the fill rule used to determine which regions of a self-intersecting or
    /// multi-contour path are inside the filled area. Defaults to <see cref="IntersectionRule.NonZero"/>.
    /// </summary>
    public IntersectionRule IntersectionRule { get; set; } = IntersectionRule.NonZero;

    /// <summary>
    /// Gets or sets the transform matrix applied to vector output before rasterization.
    /// For strokes, the pen is expanded in local geometry space and the resulting outline is transformed before rasterization.
    /// Defaults to <see cref="Matrix4x4.Identity"/>.
    /// </summary>
    public Matrix4x4 Transform { get; set; }

    /// <summary>
    /// Gets or sets the perceptual contrast boost applied to antialiased text rendering.
    /// Coverage is remapped through an S-curve that darkens mostly covered pixels and
    /// lightens mostly empty ones, so glyph stems solidify while counters and gaps stay
    /// bright; empty, half-covered, and fully covered pixels are unchanged. <c>0</c>
    /// disables the boost and renders text identically to plain vector fills; <c>1</c>
    /// applies the full smoothstep remap. Because the curve lightens sub-half coverage,
    /// high values gradually thin hairline stems in very light faces at very small sizes;
    /// lower the value if hairline preservation matters more than contrast. The boost
    /// applies only to text drawn through the text APIs; general vector fills and strokes
    /// are never affected.
    /// Defaults to <see cref="DefaultTextContrast"/>.
    /// </summary>
    public float TextContrast { get; set; } = DefaultTextContrast;
}
