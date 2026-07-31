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
    /// Creates a drawing canvas over an existing typed image frame.
    /// </summary>
    /// <remarks>
    /// The caller owns the returned canvas and must dispose it to replay recorded work into the frame.
    /// </remarks>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    /// <param name="frame">The frame backing the canvas.</param>
    /// <param name="configuration">The configuration to use for this canvas instance.</param>
    /// <param name="options">Initial drawing options for this canvas instance.</param>
    /// <param name="textCache">The text drawing cache used by this canvas instance.</param>
    /// <param name="clipPaths">Initial clip paths for this canvas instance.</param>
    /// <returns>A drawing canvas targeting <paramref name="frame"/>.</returns>
    public static DrawingCanvas CreateCanvas<TPixel>(
        this ImageFrame<TPixel> frame,
        Configuration configuration,
        DrawingOptions options,
        DrawingTextCache textCache,
        params IPath[] clipPaths)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        Guard.NotNull(frame, nameof(frame));
        Guard.NotNull(options, nameof(options));
        Guard.NotNull(textCache, nameof(textCache));
        Guard.NotNull(clipPaths, nameof(clipPaths));

        return new DrawingCanvas<TPixel>(
            configuration,
            options,
            textCache,
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

    /// <summary>
    /// Creates a drawing canvas over an existing image frame.
    /// </summary>
    /// <remarks>
    /// The caller owns the returned canvas and must dispose it to replay recorded work into the frame.
    /// </remarks>
    /// <param name="frame">The frame backing the canvas.</param>
    /// <param name="configuration">The configuration to use for this canvas instance.</param>
    /// <param name="options">Initial drawing options for this canvas instance.</param>
    /// <param name="textCache">The text drawing cache used by this canvas instance.</param>
    /// <param name="clipPaths">Initial clip paths for this canvas instance.</param>
    /// <returns>A drawing canvas targeting <paramref name="frame"/>.</returns>
    public static DrawingCanvas CreateCanvas(
        this ImageFrame frame,
        Configuration configuration,
        DrawingOptions options,
        DrawingTextCache textCache,
        params IPath[] clipPaths)
    {
        Guard.NotNull(frame, nameof(frame));
        Guard.NotNull(options, nameof(options));
        Guard.NotNull(textCache, nameof(textCache));
        Guard.NotNull(clipPaths, nameof(clipPaths));

        CanvasFactoryVisitor visitor = new(configuration, options, textCache, clipPaths);
        frame.AcceptVisitor(visitor);
        return visitor.Value!;
    }

    /// <summary>
    /// Visits a non-generic <see cref="ImageFrame"/> to create a canvas over its concrete pixel type.
    /// </summary>
    private struct CanvasFactoryVisitor : IImageFrameVisitor
    {
        /// <summary>
        /// The configuration to use for the created canvas.
        /// </summary>
        private readonly Configuration configuration;

        /// <summary>
        /// Initial drawing options for the created canvas.
        /// </summary>
        private readonly DrawingOptions options;

        /// <summary>
        /// Optional text drawing cache; when <see langword="null"/> the canvas creates and owns its own cache.
        /// </summary>
        private readonly DrawingTextCache? textCache;

        /// <summary>
        /// Initial clip paths for the created canvas.
        /// </summary>
        private readonly IPath[] clipPaths;

        /// <summary>
        /// Initializes a new instance of the <see cref="CanvasFactoryVisitor"/> struct
        /// creating a canvas that owns its own text drawing cache.
        /// </summary>
        /// <param name="configuration">The configuration to use for the created canvas.</param>
        /// <param name="options">Initial drawing options for the created canvas.</param>
        /// <param name="clipPaths">Initial clip paths for the created canvas.</param>
        public CanvasFactoryVisitor(Configuration configuration, DrawingOptions options, IPath[] clipPaths)
        {
            this.configuration = configuration;
            this.options = options;
            this.textCache = null;
            this.clipPaths = clipPaths;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="CanvasFactoryVisitor"/> struct
        /// creating a canvas that shares the supplied text drawing cache.
        /// </summary>
        /// <param name="configuration">The configuration to use for the created canvas.</param>
        /// <param name="options">Initial drawing options for the created canvas.</param>
        /// <param name="textCache">The text drawing cache used by the created canvas.</param>
        /// <param name="clipPaths">Initial clip paths for the created canvas.</param>
        public CanvasFactoryVisitor(
            Configuration configuration,
            DrawingOptions options,
            DrawingTextCache textCache,
            IPath[] clipPaths)
        {
            this.configuration = configuration;
            this.options = options;
            this.textCache = textCache;
            this.clipPaths = clipPaths;
        }

        /// <summary>
        /// Gets the canvas created during the visit, or <see langword="null"/> before the visit runs.
        /// </summary>
        public DrawingCanvas? Value { get; private set; }

        /// <inheritdoc />
        void IImageFrameVisitor.Visit<TPixel>(ImageFrame<TPixel> frame)
            => this.Value = this.textCache is null
                ? frame.CreateCanvas(this.configuration, this.options, this.clipPaths)
                : frame.CreateCanvas(this.configuration, this.options, this.textCache, this.clipPaths);
    }
}
