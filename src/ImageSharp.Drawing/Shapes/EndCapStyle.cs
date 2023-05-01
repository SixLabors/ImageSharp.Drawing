// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

namespace SixLabors.ImageSharp.Drawing
{
    /// <summary>
    /// The style to apply to the end cap when generating an outline.
    /// </summary>
    public enum EndCapStyle
    {
        /// <summary>
        /// The outline stops exactly at the end of the path.
        /// </summary>
        Butt = 0,

        /// <summary>
        /// The outline extends with a rounded style passed the end of the path.
        /// </summary>
        Round = 1,

        /// <summary>
        /// The outlines ends squared off passed the end of the path.
        /// </summary>
        Square = 2,

        /// <summary>
        /// The outline is treated as a polygon.
        /// </summary>
        Polygon = 3,

        /// <summary>
        /// The outlines ends are joined and the path treated as a polyline
        /// </summary>
        Joined = 4
    }
}
