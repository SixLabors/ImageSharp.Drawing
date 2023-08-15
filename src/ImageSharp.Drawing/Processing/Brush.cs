// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

#nullable disable

namespace SixLabors.ImageSharp.Drawing.Processing;

/// <summary>
/// Represents a logical configuration of a brush which can be used to source pixel colors.
/// </summary>
/// <remarks>
/// A brush is a simple class that will return an <see cref="BrushApplicator{TPixel}" /> that will perform the
/// logic for retrieving pixel values for specific locations.
/// </remarks>
public abstract class Brush : IEquatable<Brush>
{
    /// <summary>
    /// Creates the applicator for this brush.
    /// </summary>
    /// <typeparam name="TPixel">The pixel type.</typeparam>
    /// <param name="configuration">The configuration instance to use when performing operations.</param>
    /// <param name="options">The graphic options.</param>
    /// <param name="source">The source image.</param>
    /// <param name="region">The region the brush will be applied to.</param>
    /// <returns>
    /// The <see cref="BrushApplicator{TPixel}"/> for this brush.
    /// </returns>
    /// <remarks>
    /// The <paramref name="region" /> when being applied to things like shapes would usually be the
    /// bounding box of the shape not necessarily the bounds of the whole image.
    /// </remarks>
    public abstract BrushApplicator<TPixel> CreateApplicator<TPixel>(
        Configuration configuration,
        GraphicsOptions options,
        ImageFrame<TPixel> source,
        RectangleF region)
        where TPixel : unmanaged, IPixel<TPixel>;

    /// <inheritdoc/>
    public abstract bool Equals(Brush other);

    /// <inheritdoc/>
    public override bool Equals(object obj) => this.Equals(obj as Brush);

    /// <inheritdoc/>
    public abstract override int GetHashCode();
}
