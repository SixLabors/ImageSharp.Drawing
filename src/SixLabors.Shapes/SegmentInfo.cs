// <copyright file="PointInfo.cs" company="Scott Williams">
// Copyright (c) Scott Williams and contributors.
// Licensed under the Apache License, Version 2.0.
// </copyright>

namespace SixLabors.Shapes
{
    using SixLabors.Primitives;
    using System.Numerics;

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
