// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using SixLabors.ImageSharp.Processing;

namespace SixLabors.ImageSharp.Drawing.Processing
{
    /// <summary>
    /// Adds extensions that allow the flood filling of polygon outlines without blending.
    /// </summary>
    public static class ClearPathExtensions
    {
        /// <summary>
        /// Flood fills the image in the path of the provided polygon with the specified brush without any blending.
        /// </summary>
        /// <param name="source">The image processing context.</param>
        /// <param name="color">The color.</param>
        /// <param name="path">The logic path.</param>
        /// <returns>The <see cref="IImageProcessingContext"/> to allow chaining of operations.</returns>
        public static IImageProcessingContext Clear(
            this IImageProcessingContext source,
            Color color,
            IPath path) =>
            source.Clear(new SolidBrush(color), path);

        /// <summary>
        /// Flood fills the image in the path of the provided polygon with the specified brush without any blending.
        /// </summary>
        /// <param name="source">The image processing context.</param>
        /// <param name="options">The drawing options.</param>
        /// <param name="color">The color.</param>
        /// <param name="path">The logic path.</param>
        /// <returns>The <see cref="IImageProcessingContext"/> to allow chaining of operations.</returns>
        public static IImageProcessingContext Clear(
            this IImageProcessingContext source,
            DrawingOptions options,
            Color color,
            IPath path) =>
            source.Clear(options, new SolidBrush(color), path);

        /// <summary>
        /// Flood fills the image in the path of the provided polygon with the specified brush without any blending.
        /// </summary>
        /// <param name="source">The image processing context.</param>
        /// <param name="brush">The brush.</param>
        /// <param name="path">The logic path.</param>
        /// <returns>The <see cref="IImageProcessingContext"/> to allow chaining of operations.</returns>
        public static IImageProcessingContext Clear(
            this IImageProcessingContext source,
            IBrush brush,
            IPath path) =>
            source.Clear(source.GetDrawingOptions(), brush, path);

        /// <summary>
        /// Flood fills the image in the path of the provided polygon with the specified brush without any blending.
        /// </summary>
        /// <param name="source">The image processing context.</param>
        /// <param name="options">The drawing options.</param>
        /// <param name="brush">The brush.</param>
        /// <param name="path">The logic path.</param>
        /// <returns>The <see cref="IImageProcessingContext"/> to allow chaining of operations.</returns>
        public static IImageProcessingContext Clear(
            this IImageProcessingContext source,
            DrawingOptions options,
            IBrush brush,
            IPath path) =>
            source.Fill(options.CloneForClearOperation(), brush, path);

        /// <summary>
        /// Clones the path graphic options and applies changes required to force clearing.
        /// </summary>
        /// <param name="shapeOptions">The drawing options to clone</param>
        /// <returns>A clone of shapeOptions with ColorBlendingMode, AlphaCompositionMode, and BlendPercentage set</returns>
        internal static DrawingOptions CloneForClearOperation(this DrawingOptions shapeOptions)
        {
            GraphicsOptions options = shapeOptions.GraphicsOptions.DeepClone();
            options.ColorBlendingMode = PixelFormats.PixelColorBlendingMode.Normal;
            options.AlphaCompositionMode = PixelFormats.PixelAlphaCompositionMode.Src;
            options.BlendPercentage = 1F;

            return new DrawingOptions(options, shapeOptions.ShapeOptions, shapeOptions.TextOptions, shapeOptions.Transform);
        }
    }
}
