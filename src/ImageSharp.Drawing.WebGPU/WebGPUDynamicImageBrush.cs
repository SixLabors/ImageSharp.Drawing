// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Placeholder image brush used by retained WebGPU scene data whose texture is supplied during render.
/// </summary>
internal sealed class WebGPUDynamicImageBrush : Brush
{
    /// <summary>
    /// Initializes a new instance of the <see cref="WebGPUDynamicImageBrush"/> class.
    /// </summary>
    /// <param name="size">The render-time texture size.</param>
    /// <param name="offset">The image-brush offset encoded into the retained scene.</param>
    public WebGPUDynamicImageBrush(Size size, Point offset)
    {
        this.Size = size;
        this.Offset = offset;
        this.WrapX = WrapMode.Repeat;
        this.WrapY = WrapMode.Repeat;
    }

    /// <summary>
    /// Gets the render-time texture size.
    /// </summary>
    public Size Size { get; }

    /// <summary>
    /// Gets the image-brush offset encoded into the retained scene.
    /// </summary>
    public Point Offset { get; }

    /// <summary>
    /// Gets the horizontal wrap mode matching the default <see cref="ImageBrush{TPixel}"/> constructor used by CPU Apply.
    /// </summary>
    public WrapMode WrapX { get; }

    /// <summary>
    /// Gets the vertical wrap mode matching the default <see cref="ImageBrush{TPixel}"/> constructor used by CPU Apply.
    /// </summary>
    public WrapMode WrapY { get; }

    /// <inheritdoc />
    public override BrushRenderer<TPixel> CreateRenderer<TPixel>(
        Configuration configuration,
        GraphicsOptions options,
        int canvasWidth,
        RectangleF region)
        => throw new InvalidOperationException("WebGPU dynamic image brushes are lowered directly into GPU scene data.");

    /// <inheritdoc />
    public override bool Equals(Brush? other)
        => other is WebGPUDynamicImageBrush brush
            && brush.Size == this.Size
            && brush.Offset == this.Offset;

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(this.Size, this.Offset);
}
