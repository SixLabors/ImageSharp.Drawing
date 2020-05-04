// Copyright (c) Six Labors and contributors.
// Licensed under the Apache License, Version 2.0.

using SixLabors.ImageSharp.Drawing.Processing.Processors.Drawing;
using SixLabors.ImageSharp.Processing;

namespace SixLabors.ImageSharp.Drawing.Processing
{
    /// <summary>
    /// Adds extensions that allow the filling of regions with various brushes to the <see cref="Image{TPixel}"/> type.
    /// </summary>
    public static class FillExtensions
    {
        /// <summary>
        /// Flood fills the image with the specified brush.
        /// </summary>
        /// <param name="source">The image this method extends.</param>
        /// <param name="options">The graphics options.</param>
        /// <param name="brush">The details how to fill the region of interest.</param>
        /// <returns>The <see cref="Image{TPixel}"/>.</returns>
        public static IImageProcessingContext Fill(
            this IImageProcessingContext source,
            GraphicsOptions options,
            IBrush brush) =>
            source.ApplyProcessor(new FillProcessor(options, brush));

        /// <summary>
        /// Flood fills the image with the specified brush.
        /// </summary>
        /// <param name="source">The image this method extends.</param>
        /// <param name="brush">The details how to fill the region of interest.</param>
        /// <returns>The <see cref="Image{TPixel}"/>.</returns>
        public static IImageProcessingContext Fill(this IImageProcessingContext source, IBrush brush) =>
            source.Fill(source.GetGraphicsOptions(), brush);

        /// <summary>
        /// Flood fills the image with the specified color.
        /// </summary>
        /// <param name="source">The image this method extends.</param>
        /// <param name="options">The graphics options.</param>
        /// <param name="color">The color.</param>
        /// <returns>The <see cref="Image{TPixel}"/>.</returns>
        public static IImageProcessingContext Fill(
            this IImageProcessingContext source,
            GraphicsOptions options,
            Color color) =>
            source.Fill(options, new SolidBrush(color));

        /// <summary>
        /// Flood fills the image with the specified color.
        /// </summary>
        /// <param name="source">The image this method extends.</param>
        /// <param name="color">The color.</param>
        /// <returns>The <see cref="Image{TPixel}"/>.</returns>
        public static IImageProcessingContext Fill(this IImageProcessingContext source, Color color) =>
            source.Fill(new SolidBrush(color));
    }
}
