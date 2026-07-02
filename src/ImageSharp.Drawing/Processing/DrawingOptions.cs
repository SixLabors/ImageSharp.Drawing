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
    private GraphicsOptions graphicsOptions;

    /// <summary>
    /// Initializes a new instance of the <see cref="DrawingOptions"/> class.
    /// </summary>
    public DrawingOptions()
    {
        this.graphicsOptions = new GraphicsOptions();
        this.Transform = Matrix4x4.Identity;
    }

    internal DrawingOptions(
        GraphicsOptions graphicsOptions,
        IntersectionRule intersectionRule,
        Matrix4x4 transform)
    {
        DebugGuard.NotNull(graphicsOptions, nameof(graphicsOptions));

        this.graphicsOptions = graphicsOptions;
        this.IntersectionRule = intersectionRule;
        this.Transform = transform;
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
}
