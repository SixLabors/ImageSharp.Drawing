// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using SixLabors.ImageSharp.Drawing.Processing;

namespace SixLabors.ImageSharp.Drawing
{
    /// <summary>
    /// A mapping between a <see cref="IPath"/> and a region.
    /// </summary>
    internal class ShapePath : ShapeRegion
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ShapePath"/> class.
        /// </summary>
        /// <param name="shape">The shape.</param>
        /// <param name="pen">The pen to apply to the shape.</param>
        public ShapePath(IPath shape, IPen pen)
            : base(shape.GenerateOutline(pen.StrokeWidth, pen.StrokePattern))
        {
        }
    }
}
