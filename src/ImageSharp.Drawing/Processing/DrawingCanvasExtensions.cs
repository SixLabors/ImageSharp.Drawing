// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Memory;

namespace SixLabors.ImageSharp.Drawing.Processing;

/// <summary>
/// Convenience extension methods for creating <see cref="DrawingCanvas{TPixel}"/> instances from ImageSharp image types.
/// </summary>
public static class DrawingCanvasExtensions
{
    /// <summary>
    /// Creates a drawing canvas over an existing frame.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    /// <param name="frame">The frame backing the canvas.</param>
    /// <param name="options">Initial drawing options for this canvas instance.</param>
    /// <param name="clipPaths">Initial clip paths for this canvas instance.</param>
    /// <returns>A drawing canvas targeting <paramref name="frame"/>.</returns>
    public static DrawingCanvas<TPixel> CreateCanvas<TPixel>(
        this ImageFrame<TPixel> frame,
        DrawingOptions options,
        params IPath[] clipPaths)
        where TPixel : unmanaged, IPixel<TPixel>
        => CreateCanvas(frame, frame.Configuration, options, clipPaths);

    /// <summary>
    /// Creates a drawing canvas over an existing frame.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    /// <param name="frame">The frame backing the canvas.</param>
    /// <param name="configuration">The configuration to use for this canvas instance.</param>
    /// <param name="options">Initial drawing options for this canvas instance.</param>
    /// <param name="clipPaths">Initial clip paths for this canvas instance.</param>
    /// <returns>A drawing canvas targeting <paramref name="frame"/>.</returns>
    public static DrawingCanvas<TPixel> CreateCanvas<TPixel>(
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
            new Buffer2DRegion<TPixel>(frame.PixelBuffer, frame.Bounds),
            options,
            clipPaths);
    }

    /// <summary>
    /// Creates a drawing canvas over a specific frame of an image.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    /// <param name="image">The image containing the frame.</param>
    /// <param name="frameIndex">The zero-based frame index to target.</param>
    /// <param name="options">Initial drawing options for this canvas instance.</param>
    /// <param name="clipPaths">Initial clip paths for this canvas instance.</param>
    /// <returns>A drawing canvas targeting the selected frame.</returns>
    public static DrawingCanvas<TPixel> CreateCanvas<TPixel>(
        this Image<TPixel> image,
        int frameIndex,
        DrawingOptions options,
        params IPath[] clipPaths)
        where TPixel : unmanaged, IPixel<TPixel>
        => CreateCanvas(image, frameIndex, image.Configuration, options, clipPaths);

    /// <summary>
    /// Creates a drawing canvas over a specific frame of an image.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    /// <param name="image">The image containing the frame.</param>
    /// <param name="frameIndex">The zero-based frame index to target.</param>
    /// <param name="configuration">The configuration to use for this canvas instance.</param>
    /// <param name="options">Initial drawing options for this canvas instance.</param>
    /// <param name="clipPaths">Initial clip paths for this canvas instance.</param>
    /// <returns>A drawing canvas targeting the selected frame.</returns>
    public static DrawingCanvas<TPixel> CreateCanvas<TPixel>(
        this Image<TPixel> image,
        int frameIndex,
        Configuration configuration,
        DrawingOptions options,
        params IPath[] clipPaths)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        Guard.NotNull(image, nameof(image));
        Guard.NotNull(options, nameof(options));
        Guard.NotNull(clipPaths, nameof(clipPaths));
        Guard.MustBeBetweenOrEqualTo(frameIndex, 0, image.Frames.Count - 1, nameof(frameIndex));

        return image.Frames[frameIndex].CreateCanvas(configuration, options, clipPaths);
    }

    /// <summary>
    /// Creates a drawing canvas over the root frame of an image.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    /// <param name="image">The image whose root frame should be targeted.</param>
    /// <param name="options">Initial drawing options for this canvas instance.</param>
    /// <param name="clipPaths">Initial clip paths for this canvas instance.</param>
    /// <returns>A drawing canvas targeting the root frame.</returns>
    public static DrawingCanvas<TPixel> CreateCanvas<TPixel>(
        this Image<TPixel> image,
        DrawingOptions options,
        params IPath[] clipPaths)
        where TPixel : unmanaged, IPixel<TPixel>
        => CreateCanvas(image, image.Configuration, options, clipPaths);

    /// <summary>
    /// Creates a drawing canvas over the root frame of an image.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    /// <param name="image">The image whose root frame should be targeted.</param>
    /// <param name="configuration">The configuration to use for this canvas instance.</param>
    /// <param name="options">Initial drawing options for this canvas instance.</param>
    /// <param name="clipPaths">Initial clip paths for this canvas instance.</param>
    /// <returns>A drawing canvas targeting the root frame.</returns>
    public static DrawingCanvas<TPixel> CreateCanvas<TPixel>(
        this Image<TPixel> image,
        Configuration configuration,
        DrawingOptions options,
        params IPath[] clipPaths)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        Guard.NotNull(image, nameof(image));
        Guard.NotNull(options, nameof(options));
        Guard.NotNull(clipPaths, nameof(clipPaths));

        return image.Frames.RootFrame.CreateCanvas(configuration, options, clipPaths);
    }
}
