// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;

namespace SixLabors.ImageSharp.Drawing.Processing;

/// <summary>
/// Adds extensions that help working with <see cref="DrawingOptions" />.
/// </summary>
public static class DrawingOptionsDefaultsExtensions
{
    private const string DrawingTransformMatrixKey = "DrawingTransformMatrix3x2";

    /// <summary>
    /// Gets the default shape processing options against The source image processing context.
    /// </summary>
    /// <param name="context">The image processing context to retrieve defaults from.</param>
    /// <returns>The globally configured default options.</returns>
    public static DrawingOptions GetDrawingOptions(this IImageProcessingContext context)
        => new(context.GetGraphicsOptions(), context.GetShapeOptions(), context.GetDrawingTransform());

    /// <summary>
    /// Sets the 2D transformation matrix to be used during rasterization when drawing shapes or text.
    /// </summary>
    /// <param name="context">The image processing context to store default against.</param>
    /// <param name="matrix">The matrix to use.</param>
    /// <returns>The passed in <paramref name="context"/> to allow chaining.</returns>
    public static IImageProcessingContext SetDrawingTransform(this IImageProcessingContext context, Matrix3x2 matrix)
    {
        context.Properties[DrawingTransformMatrixKey] = matrix;
        return context;
    }

    /// <summary>
    /// Sets the default 2D transformation matrix to be used during rasterization when drawing shapes or text.
    /// </summary>
    /// <param name="configuration">The configuration to store default against.</param>
    /// <param name="matrix">The default matrix to use.</param>
    public static void SetDrawingTransform(this Configuration configuration, Matrix3x2 matrix)
        => configuration.Properties[DrawingTransformMatrixKey] = matrix;

    /// <summary>
    /// Gets the default 2D transformation matrix to be used during rasterization when drawing shapes or text.
    /// </summary>
    /// <param name="context">The image processing context to retrieve defaults from.</param>
    /// <returns>The matrix.</returns>
    public static Matrix3x2 GetDrawingTransform(this IImageProcessingContext context)
    {
        if (context.Properties.TryGetValue(DrawingTransformMatrixKey, out object? options) && options is Matrix3x2 go)
        {
            return go;
        }

        // do not cache the fall back to config into the processing context
        // in case someone want to change the value on the config and expects it re-flow thru.
        return context.Configuration.GetDrawingTransform();
    }

    /// <summary>
    /// Gets the default 2D transformation matrix to be used during rasterization when drawing shapes or text.
    /// </summary>
    /// <param name="configuration">The configuration to retrieve defaults from.</param>
    /// <returns>The globally configured default matrix.</returns>
    public static Matrix3x2 GetDrawingTransform(this Configuration configuration)
    {
        if (configuration.Properties.TryGetValue(DrawingTransformMatrixKey, out object? options) && options is Matrix3x2 go)
        {
            return go;
        }

        return Matrix3x2.Identity;
    }
}
