// Copyright (c) Six Labors and contributors.
// Licensed under the Apache License, Version 2.0.

using SixLabors.Primitives;

namespace SixLabors.Shapes
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
        /// The angle of the segment.
        /// </summary>
        public float Angle;
    }
}
