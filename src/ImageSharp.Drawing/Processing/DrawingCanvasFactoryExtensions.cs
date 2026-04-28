// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Memory;

namespace SixLabors.ImageSharp.Drawing.Processing;

/// <summary>
/// Convenience extension methods for creating drawing canvas instances from ImageSharp image types.
/// </summary>
internal static class DrawingCanvasFactoryExtensions
{
    /// <summary>
    /// Creates a drawing canvas over an existing frame.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    /// <param name="frame">The frame backing the canvas.</param>
    /// <param name="configuration">The configuration to use for this canvas instance.</param>
    /// <param name="options">Initial drawing options for this canvas instance.</param>
    /// <param name="clipPaths">Initial clip paths for this canvas instance.</param>
    /// <returns>A drawing canvas targeting <paramref name="frame"/>.</returns>
    internal static DrawingCanvas CreateCanvas<TPixel>(
        this ImageFrame<TPixel> frame,
        Configuration configuration,
        DrawingOptions options,
        params IPath[] clipPaths)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        Guard.NotNull(frame, nameof(frame));
        Guard.NotNull(options, nameof(options));
        Guard.NotNull(clipPaths, nameof(clipPaths));

        return new DrawingCanvas<TPixel>(
            configuration,
            options,
            new Buffer2DRegion<TPixel>(frame.PixelBuffer),
            clipPaths);
    }
}
