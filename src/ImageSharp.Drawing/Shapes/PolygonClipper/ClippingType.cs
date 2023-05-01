// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

namespace SixLabors.ImageSharp.Drawing.Shapes.PolygonClipper
{
    /// <summary>
    /// Defines the polygon clipping type.
    /// </summary>
    public enum ClippingType
    {
        /// <summary>
        /// Represents a shape to act as a subject which will be clipped or merged.
        /// </summary>
        Subject,

        /// <summary>
        /// Represents a shape to act as a clipped path.
        /// </summary>
        Clip
    }
}
