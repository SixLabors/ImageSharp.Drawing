// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;

namespace SixLabors.ImageSharp.Drawing.Processing;

/// <summary>
/// Adds extensions that help working with <see cref="DrawingOptions" />.
/// </summary>
public static class DrawingOptionsDefaultsExtensions
{
    /// <summary>
    /// Gets the default drawing options against the source image processing context.
    /// </summary>
    /// <param name="context">The image processing context to retrieve defaults from.</param>
    /// <returns>The globally configured default options.</returns>
    public static DrawingOptions GetDrawingOptions(this IImageProcessingContext context)
        => new(context.GetGraphicsOptions(), new ShapeOptions(), Matrix4x4.Identity);

    /// <summary>
    /// Clones the path graphic options and applies changes required to force clearing.
    /// </summary>
    /// <param name="drawingOptions">The drawing options to clone</param>
    /// <returns>A clone of shapeOptions with ColorBlendingMode, AlphaCompositionMode, and BlendPercentage set</returns>
    internal static DrawingOptions CloneForClearOperation(this DrawingOptions drawingOptions)
    {
        GraphicsOptions options = drawingOptions.GraphicsOptions.DeepClone();
        options.ColorBlendingMode = PixelColorBlendingMode.Normal;
        options.AlphaCompositionMode = PixelAlphaCompositionMode.Src;
        options.BlendPercentage = 1F;

        return new DrawingOptions(options, drawingOptions.ShapeOptions, drawingOptions.Transform);
    }
}
