// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;

namespace SixLabors.ImageSharp.Drawing.Processing;

/// <summary>
/// Represents a logical configuration of a brush which can be used to source pixel colors.
/// </summary>
/// <remarks>
/// A brush creates a <see cref="BrushRenderer{TPixel}"/> that performs the logic for retrieving
/// pixel values for specific locations.
/// </remarks>
public abstract class Brush : IEquatable<Brush>
{
    /// <summary>
    /// Creates the prepared execution object for this brush.
    /// </summary>
    /// <typeparam name="TPixel">The pixel type.</typeparam>
    /// <param name="configuration">The configuration instance to use when performing operations.</param>
    /// <param name="options">The graphic options.</param>
    /// <param name="canvasWidth">The canvas width for the current render pass.</param>
    /// <param name="region">The region the brush will be applied to.</param>
    /// <returns>
    /// The <see cref="BrushRenderer{TPixel}"/> for this brush.
    /// </returns>
    /// <remarks>
    /// The <paramref name="region" /> when being applied to things like shapes would usually be the
    /// bounding box of the shape not necessarily the bounds of the whole image.
    /// </remarks>
    public abstract BrushRenderer<TPixel> CreateRenderer<TPixel>(
        Configuration configuration,
        GraphicsOptions options,
        int canvasWidth,
        RectangleF region)
        where TPixel : unmanaged, IPixel<TPixel>;

    /// <summary>
    /// Returns a new brush with its defining geometry transformed by the given matrix.
    /// </summary>
    /// <param name="matrix">The transformation matrix to apply.</param>
    /// <returns>A transformed brush, or <c>this</c> if the brush has no spatial parameters.</returns>
    public virtual Brush Transform(Matrix4x4 matrix) => this;

    /// <inheritdoc/>
    public abstract bool Equals(Brush? other);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => this.Equals(obj as Brush);

    /// <inheritdoc/>
    public abstract override int GetHashCode();
}
