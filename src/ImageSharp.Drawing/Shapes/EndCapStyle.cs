// Copyright (c) Six Labors and contributors.
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
    }
}
