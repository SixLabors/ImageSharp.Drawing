// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;

namespace SixLabors.ImageSharp.Drawing
{
    /// <summary>
    /// Represents a region of an image.
    /// </summary>
    public abstract class Region
    {
        /// <summary>
        /// Gets the maximum number of intersections to could be returned.
        /// </summary>
        public abstract int MaxIntersections { get; }

        /// <summary>
        /// Gets the bounding box that entirely surrounds this region.
        /// </summary>
        /// <remarks>
        /// This should always contains all possible points returned from <see cref="Scan"/>.
        /// </remarks>
        public abstract Rectangle Bounds { get; }

        // We should consider removing Region, so keeping this internal for now.
        internal abstract IPath Shape { get; }
    }
}
