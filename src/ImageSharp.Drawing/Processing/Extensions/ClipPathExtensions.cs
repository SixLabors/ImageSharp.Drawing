// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Processing.Processors.Drawing;

namespace SixLabors.ImageSharp.Drawing.Processing;

/// <summary>
/// Adds extensions that allow the application of processors within a clipped path.
/// </summary>
public static class ClipPathExtensions
{
    /// <summary>
    /// Applies the processing operation within the provided region defined by an <see cref="IPath"/>.
    /// </summary>
    /// <param name="source">The source image processing context.</param>
    /// <param name="region">The <see cref="IPath"/> defining the region to operation within.</param>
    /// <param name="operation">The operation to perform.</param>
    /// <returns>The <see cref="IImageProcessingContext"/> to allow chaining of operations.</returns>
    public static IImageProcessingContext Clip(
        this IImageProcessingContext source,
        IPath region,
        Action<IImageProcessingContext> operation)
        => source.ApplyProcessor(new ClipPathProcessor(source.GetDrawingOptions(), region, operation));
}
