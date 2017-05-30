// <copyright file="PointInfo.cs" company="Scott Williams">
// Copyright (c) Scott Williams and contributors.
// Licensed under the Apache License, Version 2.0.
// </copyright>

namespace SixLabors.Shapes
{
    using System.Numerics;

    /// <summary>
    /// Returns meta data about the nearest point on a path from a vector
    /// </summary>
    public struct SegmentInfo
    {
        /// <summary>
        /// The point on the path
        /// </summary>
        public Vector2 Point;

        /// <summary>
        /// The angle of the segment.
        /// </summary>
        public float Angle;
    }
}
