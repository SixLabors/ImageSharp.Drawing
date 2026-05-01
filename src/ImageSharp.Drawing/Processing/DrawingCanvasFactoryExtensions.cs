// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Advanced;
using SixLabors.ImageSharp.Memory;

namespace SixLabors.ImageSharp.Drawing.Processing;

/// <summary>
/// Extension methods for creating drawing canvas instances over ImageSharp image frames.
/// </summary>
public static class DrawingCanvasFactoryExtensions
{
    /// <summary>
    /// Creates a drawing canvas over an existing typed image frame.
    /// </summary>
    /// <remarks>
    /// The caller owns the returned canvas and must dispose it to replay recorded work into the frame.
    /// </remarks>
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
            frame.PixelBuffer.GetRegion(),
            clipPaths);
    }

    /// <summary>
    /// Creates a drawing canvas over an existing image frame.
    /// </summary>
    /// <remarks>
    /// The caller owns the returned canvas and must dispose it to replay recorded work into the frame.
    /// </remarks>
    /// <param name="frame">The frame backing the canvas.</param>
    /// <param name="configuration">The configuration to use for this canvas instance.</param>
    /// <param name="options">Initial drawing options for this canvas instance.</param>
    /// <param name="clipPaths">Initial clip paths for this canvas instance.</param>
    /// <returns>A drawing canvas targeting <paramref name="frame"/>.</returns>
    public static DrawingCanvas CreateCanvas(
        this ImageFrame frame,
        Configuration configuration,
        DrawingOptions options,
        params IPath[] clipPaths)
    {
        Guard.NotNull(frame, nameof(frame));
        Guard.NotNull(options, nameof(options));
        Guard.NotNull(clipPaths, nameof(clipPaths));

        CanvasFactoryVisitor visitor = new(configuration, options, clipPaths);
        frame.AcceptVisitor(visitor);
        return visitor.Value!;
    }

    private struct CanvasFactoryVisitor : IImageFrameVisitor
    {
        private readonly Configuration configuration;
        private readonly DrawingOptions options;
        private readonly IPath[] clipPaths;

        public CanvasFactoryVisitor(Configuration configuration, DrawingOptions options, IPath[] clipPaths)
        {
            this.configuration = configuration;
            this.options = options;
            this.clipPaths = clipPaths;
        }

        public DrawingCanvas? Value { get; private set; }

        void IImageFrameVisitor.Visit<TPixel>(ImageFrame<TPixel> frame)
            => this.Value = frame.CreateCanvas(this.configuration, this.options, this.clipPaths);
    }
}
