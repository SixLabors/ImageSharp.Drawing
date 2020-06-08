// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

namespace SixLabors.ImageSharp.Drawing
{
    /// <summary>
    /// Rule for calulating intersection points,
    /// </summary>
    public enum IntersectionRule
    {
        /// <summary>
        /// Use odd/even intersection rules, self intersection will cause holes.
        /// </summary>
        OddEven = 0,

        /// <summary>
        /// Nonzero rule treats intersecting holes as inside the path thus being ignored by intersections.
        /// </summary>
        Nonzero = 1
    }
}
