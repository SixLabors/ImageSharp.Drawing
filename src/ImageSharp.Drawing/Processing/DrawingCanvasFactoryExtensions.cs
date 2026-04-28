// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Diagnostics.CodeAnalysis;
using SixLabors.ImageSharp.Advanced;
using SixLabors.ImageSharp.Memory;

namespace SixLabors.ImageSharp.Drawing.Processing;

/// <summary>
/// Convenience extension methods for creating drawing canvas instances from ImageSharp image types.
/// </summary>
public static class DrawingCanvasFactoryExtensions
{
    /// <summary>
    /// Creates a drawing canvas over a specific frame of an image whose pixel type is known only at runtime.
    /// </summary>
    /// <param name="image">The image containing the frame.</param>
    /// <param name="options">Initial drawing options for this canvas instance.</param>
    /// <param name="frameIndex">The zero-based frame index to target.</param>
    /// <param name="clipPaths">Initial clip paths for this canvas instance.</param>
    /// <returns>A drawing canvas targeting the selected frame.</returns>
    public static DrawingCanvas CreateCanvas(
        this Image image,
        DrawingOptions options,
        int frameIndex,
        params IPath[] clipPaths)
        => CreateCanvas(image, image.Configuration, options, frameIndex, clipPaths);

    /// <summary>
    /// Creates a drawing canvas over a specific frame of an image whose pixel type is known only at runtime.
    /// </summary>
    /// <param name="image">The image containing the frame.</param>
    /// <param name="configuration">The configuration to use for this canvas instance.</param>
    /// <param name="options">Initial drawing options for this canvas instance.</param>
    /// <param name="frameIndex">The zero-based frame index to target.</param>
    /// <param name="clipPaths">Initial clip paths for this canvas instance.</param>
    /// <returns>A drawing canvas targeting the selected frame.</returns>
    public static DrawingCanvas CreateCanvas(
        this Image image,
        Configuration configuration,
        DrawingOptions options,
        int frameIndex,
        params IPath[] clipPaths)
    {
        Guard.NotNull(image, nameof(image));
        Guard.NotNull(options, nameof(options));
        Guard.NotNull(clipPaths, nameof(clipPaths));
        Guard.MustBeBetweenOrEqualTo(frameIndex, 0, image.Frames.Count - 1, nameof(frameIndex));

        CreateCanvasVisitor visitor = new(configuration, options, frameIndex, clipPaths);
        image.AcceptVisitor(visitor);
        return visitor.Canvas!;
    }

    /// <summary>
    /// Creates a drawing canvas over the root frame of an image whose pixel type is known only at runtime.
    /// </summary>
    /// <param name="image">The image whose root frame should be targeted.</param>
    /// <param name="options">Initial drawing options for this canvas instance.</param>
    /// <param name="clipPaths">Initial clip paths for this canvas instance.</param>
    /// <returns>A drawing canvas targeting the root frame.</returns>
    public static DrawingCanvas CreateCanvas(
        this Image image,
        DrawingOptions options,
        params IPath[] clipPaths)
        => CreateCanvas(image, image.Configuration, options, clipPaths);

    /// <summary>
    /// Creates a drawing canvas over the root frame of an image whose pixel type is known only at runtime.
    /// </summary>
    /// <param name="image">The image whose root frame should be targeted.</param>
    /// <param name="configuration">The configuration to use for this canvas instance.</param>
    /// <param name="options">Initial drawing options for this canvas instance.</param>
    /// <param name="clipPaths">Initial clip paths for this canvas instance.</param>
    /// <returns>A drawing canvas targeting the root frame.</returns>
    public static DrawingCanvas CreateCanvas(
        this Image image,
        Configuration configuration,
        DrawingOptions options,
        params IPath[] clipPaths)
    {
        Guard.NotNull(image, nameof(image));
        Guard.NotNull(options, nameof(options));
        Guard.NotNull(clipPaths, nameof(clipPaths));

        CreateCanvasVisitor visitor = new(configuration, options, frameIndex: 0, clipPaths);
        image.AcceptVisitor(visitor);
        return visitor.Canvas!;
    }

    /// <summary>
    /// Creates a drawing canvas over an existing frame.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    /// <param name="frame">The frame backing the canvas.</param>
    /// <param name="options">Initial drawing options for this canvas instance.</param>
    /// <param name="clipPaths">Initial clip paths for this canvas instance.</param>
    /// <returns>A drawing canvas targeting <paramref name="frame"/>.</returns>
    public static DrawingCanvas CreateCanvas<TPixel>(
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
    public static DrawingCanvas CreateCanvas<TPixel>(
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

    /// <summary>
    /// Creates a drawing canvas over a specific frame of an image.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    /// <param name="image">The image containing the frame.</param>
    /// <param name="options">Initial drawing options for this canvas instance.</param>
    /// <param name="frameIndex">The zero-based frame index to target.</param>
    /// <param name="clipPaths">Initial clip paths for this canvas instance.</param>
    /// <returns>A drawing canvas targeting the selected frame.</returns>
    public static DrawingCanvas CreateCanvas<TPixel>(
        this Image<TPixel> image,
        DrawingOptions options,
        int frameIndex,
        params IPath[] clipPaths)
        where TPixel : unmanaged, IPixel<TPixel>
        => CreateCanvas(image, image.Configuration, options, frameIndex, clipPaths);

    /// <summary>
    /// Creates a drawing canvas over a specific frame of an image.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    /// <param name="image">The image containing the frame.</param>
    /// <param name="configuration">The configuration to use for this canvas instance.</param>
    /// <param name="options">Initial drawing options for this canvas instance.</param>
    /// <param name="frameIndex">The zero-based frame index to target.</param>
    /// <param name="clipPaths">Initial clip paths for this canvas instance.</param>
    /// <returns>A drawing canvas targeting the selected frame.</returns>
    public static DrawingCanvas CreateCanvas<TPixel>(
        this Image<TPixel> image,
        Configuration configuration,
        DrawingOptions options,
        int frameIndex,
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
    public static DrawingCanvas CreateCanvas<TPixel>(
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
    public static DrawingCanvas CreateCanvas<TPixel>(
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

    private sealed class CreateCanvasVisitor : IImageVisitor
    {
        private readonly Configuration configuration;
        private readonly DrawingOptions options;
        private readonly int frameIndex;
        private readonly IPath[] clipPaths;

        public CreateCanvasVisitor(
            Configuration configuration,
            DrawingOptions options,
            int frameIndex,
            IPath[] clipPaths)
        {
            this.configuration = configuration;
            this.options = options;
            this.frameIndex = frameIndex;
            this.clipPaths = clipPaths;
        }

        public DrawingCanvas? Canvas { get; private set; }

        [MemberNotNull(nameof(Canvas))]
        public void Visit<TPixel>(Image<TPixel> image)
            where TPixel : unmanaged, IPixel<TPixel>
            => this.Canvas = image.CreateCanvas(
                this.configuration,
                this.options,
                this.frameIndex,
                this.clipPaths);
    }
}
