// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;

namespace SixLabors.ImageSharp.Drawing.Processing;

/// <summary>
/// Provides options for influencing drawing operations, combining graphics rendering settings,
/// shape fill-rule behavior, and an optional coordinate transform.
/// </summary>
public class DrawingOptions
{
    private GraphicsOptions graphicsOptions;
    private ShapeOptions shapeOptions;

    /// <summary>
    /// Initializes a new instance of the <see cref="DrawingOptions"/> class.
    /// </summary>
    public DrawingOptions()
    {
        this.graphicsOptions = new GraphicsOptions();
        this.shapeOptions = new ShapeOptions();
        this.Transform = Matrix4x4.Identity;
    }

    internal DrawingOptions(
        GraphicsOptions graphicsOptions,
        ShapeOptions shapeOptions,
        Matrix4x4 transform)
    {
        DebugGuard.NotNull(graphicsOptions, nameof(graphicsOptions));
        DebugGuard.NotNull(shapeOptions, nameof(shapeOptions));

        this.graphicsOptions = graphicsOptions;
        this.shapeOptions = shapeOptions;
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
    /// Gets or sets the shape options that control fill-rule intersection mode and boolean clipping behavior.
    /// </summary>
    public ShapeOptions ShapeOptions
    {
        get => this.shapeOptions;
        set
        {
            Guard.NotNull(value, nameof(this.ShapeOptions));
            this.shapeOptions = value;
        }
    }

    /// <summary>
    /// Gets or sets the affine transform matrix applied to vector geometry before rasterization.
    /// Can be used to translate, rotate, scale, or skew shapes.
    /// Defaults to <see cref="Matrix4x4.Identity"/>.
    /// </summary>
    public Matrix4x4 Transform { get; set; }
}
