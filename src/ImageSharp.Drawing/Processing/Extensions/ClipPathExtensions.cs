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
    /// Applies the processing operation within the region defined by an <see cref="IPath"/>.
    /// </summary>
    /// <param name="source">The source image processing context.</param>
    /// <param name="region">
    /// The <see cref="IPath"/> defining the clip region. Only pixels inside the clip are affected.
    /// </param>
    /// <param name="operation">
    /// The operation to perform. This executes in the clipped context so results are constrained to the
    /// clip bounds.
    /// </param>
    /// <returns>The <see cref="IImageProcessingContext"/> to allow chaining of operations.</returns>
    public static IImageProcessingContext Clip(
        this IImageProcessingContext source,
        IPath region,
        Action<IImageProcessingContext> operation)
        => source.ApplyProcessor(new ClipPathProcessor(source.GetDrawingOptions(), region, operation));
}
