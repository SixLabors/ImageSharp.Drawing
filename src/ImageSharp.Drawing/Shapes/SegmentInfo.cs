// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

namespace SixLabors.ImageSharp.Drawing
{
    /// <summary>
    /// Returns metadata about the point along a path.
    /// </summary>
    public struct SegmentInfo
    {
        /// <summary>
        /// The point on the path
        /// </summary>
        public PointF Point;

        /// <summary>
        /// The angle of the segment. Measured in radians.
        /// </summary>
        public float Angle;
    }
}
