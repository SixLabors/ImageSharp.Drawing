// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using Avalonia.Platform;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing.Processors.Transforms;

namespace SixLabors.ImageSharp.Drawing.Renderers.Avalonia;

/// <summary>
/// Bitmap implementation that can draw itself into an ImageSharp-backed drawing context.
/// </summary>
internal interface IDrawableBitmapImpl : IBitmapImpl
{
    /// <summary>
    /// Draws this bitmap into the supplied drawing context.
    /// </summary>
    /// <param name="context">The drawing context receiving the bitmap.</param>
    /// <param name="sourceRect">The source rectangle in bitmap pixels.</param>
    /// <param name="destinationRect">The destination rectangle in drawing coordinates.</param>
    /// <param name="sampler">The resampler used when scaling the bitmap.</param>
    /// <param name="opacity">The opacity applied to the bitmap draw.</param>
    /// <param name="alphaCompositionMode">The alpha composition mode used to blend the bitmap with the destination.</param>
    void Draw(
        DrawingContextImpl context,
        Rectangle sourceRect,
        RectangleF destinationRect,
        IResampler sampler,
        float opacity,
        PixelAlphaCompositionMode alphaCompositionMode);
}
