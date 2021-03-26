// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using SixLabors.ImageSharp.Processing;

namespace SixLabors.ImageSharp.Drawing.Processing
{
    /// <summary>
    /// Adds extensions that allow the processing of images to the <see cref="Image{TPixel}"/> type.
    /// </summary>
    public static class DrawingOptionsDefaultsExtensions
    {
        /// <summary>
        /// Gets the default shape processing options against the image processing context.
        /// </summary>
        /// <param name="context">The image processing context to retrieve defaults from.</param>
        /// <returns>The globaly configured default options.</returns>
        public static DrawingOptions GetDrawingOptions(this IImageProcessingContext context)
            => new DrawingOptions(context.GetGraphicsOptions(), context.GetShapeOptions(), context.GetTextOptions(), context.GetDrawingTransform());
    }
}
