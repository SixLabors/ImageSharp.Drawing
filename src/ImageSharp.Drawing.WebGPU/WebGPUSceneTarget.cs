// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using Silk.NET.WebGPU;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Render-time WebGPU target selected by the scene replay path.
/// </summary>
internal readonly unsafe struct WebGPUSceneTarget
{
    /// <summary>
    /// Initializes a new instance of the <see cref="WebGPUSceneTarget"/> struct.
    /// </summary>
    /// <param name="texture">The target texture.</param>
    /// <param name="textureView">The target texture view.</param>
    /// <param name="bounds">The absolute logical bounds represented by the target.</param>
    /// <param name="textureOffset">The offset from absolute logical coordinates to texture coordinates.</param>
    public WebGPUSceneTarget(
        Texture* texture,
        TextureView* textureView,
        Rectangle bounds,
        Point textureOffset)
    {
        this.Texture = texture;
        this.TextureView = textureView;
        this.Bounds = bounds;
        this.TextureOffset = textureOffset;
    }

    /// <summary>
    /// Gets the target texture.
    /// </summary>
    public Texture* Texture { get; }

    /// <summary>
    /// Gets the target texture view.
    /// </summary>
    public TextureView* TextureView { get; }

    /// <summary>
    /// Gets the absolute logical bounds represented by the target.
    /// </summary>
    public Rectangle Bounds { get; }

    /// <summary>
    /// Gets the offset from absolute logical coordinates to texture coordinates.
    /// </summary>
    public Point TextureOffset { get; }
}
