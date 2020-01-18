// Copyright (c) Six Labors and contributors.
// Licensed under the Apache License, Version 2.0.

namespace SixLabors.Shapes
{
    /// <summary>
    /// Rule for calulating intersection points,
    /// </summary>
    public enum IntersectionRule
    {
        /// <summary>
        /// Use odd/even intersection rules, self intersection will cause holes.
        /// </summary>
        OddEven,

        /// <summary>
        /// Nonzero rule treats intersecting holes as inside the path thus being ignored by intersections.
        /// </summary>
        Nonzero
    }
}
