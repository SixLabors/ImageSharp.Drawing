// Copyright (c) Six Labors and contributors.
// Licensed under the Apache License, Version 2.0.

namespace SixLabors.Shapes.PolygonClipper
{
    /// <summary>
    /// Poly Type
    /// </summary>
    public enum ClippingType
    {
        /// <summary>
        /// Represent a main shape to act as a main subject whoes path will be clipped or merged.
        /// </summary>
        Subject,

        /// <summary>
        /// Represents a shape to act as a clipped path.
        /// </summary>
        Clip
    }
}