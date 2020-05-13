// Copyright (c) Six Labors and contributors.
// Licensed under the GNU Affero General Public License, Version 3.

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
        /// The angle of the segment.
        /// </summary>
        public float Angle;
    }
}
