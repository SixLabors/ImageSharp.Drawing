// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Processing;

/// <summary>
/// Renders a <see cref="Brush"/> against individual coverage scanlines.
/// </summary>
/// <typeparam name="TPixel">The pixel format.</typeparam>
public abstract class BrushRenderer<TPixel> : IDisposable
    where TPixel : unmanaged, IPixel<TPixel>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="BrushRenderer{TPixel}"/> class.
    /// </summary>
    /// <param name="configuration">The configuration instance to use when performing operations.</param>
    /// <param name="options">The graphics options.</param>
    /// <param name="canvasWidth">The canvas width for the current render pass.</param>
    protected BrushRenderer(
        Configuration configuration,
        GraphicsOptions options,
        int canvasWidth)
    {
        this.Configuration = configuration;
        this.Options = options;
        this.CanvasWidth = canvasWidth;
        this.Blender = PixelOperations<TPixel>.Instance.GetPixelBlender(options);
    }

    /// <summary>
    /// Gets the configuration instance to use when performing operations.
    /// </summary>
    protected Configuration Configuration { get; }

    /// <summary>
    /// Gets the pixel blender.
    /// </summary>
    internal PixelBlender<TPixel> Blender { get; }

    /// <summary>
    /// Gets the graphics options.
    /// </summary>
    protected GraphicsOptions Options { get; }

    /// <summary>
    /// Gets the canvas width for the current render pass.
    /// </summary>
    protected int CanvasWidth { get; }

    /// <inheritdoc />
    public void Dispose()
    {
        this.Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Applies the opacity weighting for each pixel in a scanline to the target based on the
    /// pattern contained in the brush.
    /// </summary>
    /// <param name="destinationRow">The destination row slice to shade.</param>
    /// <param name="scanline">The coverage values for the current destination scanline.</param>
    /// <param name="x">The x-position in the target pixel space that the start of the scanline data corresponds to.</param>
    /// <param name="y">The y-position in the target pixel space that the scanline corresponds to.</param>
    /// <param name="workspace">The worker-local scratch workspace for temporary blending buffers.</param>
    public abstract void Apply(
        Span<TPixel> destinationRow,
        ReadOnlySpan<float> scanline,
        int x,
        int y,
        BrushWorkspace<TPixel> workspace);

    /// <summary>
    /// Disposes the object and frees resources for the Garbage Collector.
    /// </summary>
    /// <param name="disposing">Whether to dispose managed and unmanaged objects.</param>
    protected virtual void Dispose(bool disposing)
    {
    }
}
