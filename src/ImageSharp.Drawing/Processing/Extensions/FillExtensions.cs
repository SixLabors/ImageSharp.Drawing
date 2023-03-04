// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using SixLabors.ImageSharp.Drawing.Processing.Processors.Drawing;
using SixLabors.ImageSharp.Processing;

namespace SixLabors.ImageSharp.Drawing.Processing
{
    /// <summary>
    /// Adds extensions that allow the flood filling of images.
    /// </summary>
    public static class FillExtensions
    {
        /// <summary>
        /// Flood fills the image with the specified color.
        /// </summary>
        /// <param name="source">The image processing context.</param>
        /// <param name="color">The color.</param>
        /// <returns>The <see cref="IImageProcessingContext"/> to allow chaining of operations.</returns>
        public static IImageProcessingContext Fill(this IImageProcessingContext source, Color color)
            => source.Fill(new SolidBrush(color));

        /// <summary>
        /// Flood fills the image with the specified color.
        /// </summary>
        /// <param name="source">The image processing context.</param>
        /// <param name="options">The drawing options.</param>
        /// <param name="color">The color.</param>
        /// <returns>The <see cref="IImageProcessingContext"/> to allow chaining of operations.</returns>
        public static IImageProcessingContext Fill(this IImageProcessingContext source, DrawingOptions options, Color color)
            => source.Fill(options, new SolidBrush(color));

        /// <summary>
        /// Flood fills the image with the specified brush.
        /// </summary>
        /// <param name="source">The image processing context.</param>
        /// <param name="brush">The brush.</param>
        /// <returns>The <see cref="IImageProcessingContext"/> to allow chaining of operations.</returns>
        public static IImageProcessingContext Fill(this IImageProcessingContext source, Brush brush)
            => source.Fill(source.GetDrawingOptions(), brush);

        /// <summary>
        /// Flood fills the image with the specified brush.
        /// </summary>
        /// <param name="source">The image processing context.</param>
        /// <param name="options">The drawing options.</param>
        /// <param name="brush">The brush.</param>
        /// <returns>The <see cref="IImageProcessingContext"/> to allow chaining of operations.</returns>
        public static IImageProcessingContext Fill(this IImageProcessingContext source, DrawingOptions options, Brush brush)
            => source.ApplyProcessor(new FillProcessor(options, brush));
    }
}
