// Copyright (c) Six Labors and contributors.
// Licensed under the GNU Affero General Public License, Version 3.

using SixLabors.ImageSharp.Drawing.Processing.Processors.Drawing;
using SixLabors.ImageSharp.Processing;

namespace SixLabors.ImageSharp.Drawing.Processing
{
    /// <summary>
    /// Adds extensions that allow the filling of regions with various brushes to the <see cref="Image{TPixel}"/> type.
    /// </summary>
    public static class ClearRegionExtensions
    {
        /// <summary>
        /// Flood fills the image with in the region with the specified brush without any blending.
        /// </summary>
        /// <param name="source">The image this method extends.</param>
        /// <param name="brush">The brush.</param>
        /// <param name="region">The region.</param>
        /// <returns>The <see cref="IImageProcessingContext"/> to allow chaining of operations.</returns>
        public static IImageProcessingContext Clear(this IImageProcessingContext source, IBrush brush, Region region) =>
            source.Clear(source.GetShapeGraphicsOptions(), brush, region);

        /// <summary>
        /// Flood fills the image with in the region with the specified brush without any blending.
        /// </summary>
        /// <param name="source">The image this method extends.</param>
        /// <param name="options">The graphics options.</param>
        /// <param name="brush">The brush.</param>
        /// <param name="region">The region.</param>
        /// <returns>The <see cref="IImageProcessingContext"/> to allow chaining of operations.</returns>
        public static IImageProcessingContext Clear(
            this IImageProcessingContext source,
            ShapeGraphicsOptions options,
            IBrush brush,
            Region region) =>
            source.Fill(options.CloneForClearOperation(), brush, region);

        /// <summary>
        /// Flood fills the image with in the region with the specified color without any blending.
        /// </summary>
        /// <param name="source">The image this method extends.</param>
        /// <param name="options">The options.</param>
        /// <param name="color">The color.</param>
        /// <param name="region">The region.</param>
        /// <returns>The <see cref="IImageProcessingContext"/> to allow chaining of operations.</returns>
        public static IImageProcessingContext Clear(
            this IImageProcessingContext source,
            ShapeGraphicsOptions options,
            Color color,
            Region region) =>
            source.Clear(options, new SolidBrush(color), region);

        /// <summary>
        /// Flood fills the image with in the region with the specified color without any blending.
        /// </summary>
        /// <param name="source">The image this method extends.</param>
        /// <param name="color">The color.</param>
        /// <param name="region">The region.</param>
        /// <returns>The <see cref="IImageProcessingContext"/> to allow chaining of operations.</returns>
        public static IImageProcessingContext Clear(this IImageProcessingContext source, Color color, Region region) =>
            source.Clear(new SolidBrush(color), region);
    }
}
