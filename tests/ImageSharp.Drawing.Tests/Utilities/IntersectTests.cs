// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using SixLabors.ImageSharp.Drawing.Utilities;
using Xunit;

namespace SixLabors.ImageSharp.Drawing.Tests.Drawing.Utils
{
    public class IntersectTests
    {
        public static TheoryData<PointF, PointF, PointF, PointF, PointF?> LineSegmentToLineSegment_Data =
            new TheoryData<PointF, PointF, PointF, PointF, PointF?>()
            {
                { new PointF(0, 0), new PointF(2, 3), new PointF(1, 3), new PointF(1, 0), new PointF(1, 1.5f) },
                { new PointF(0, 0), new PointF(2, 3), new PointF(1, 3), new PointF(1, 2), null },
            };

        [Theory]
        [MemberData(nameof(LineSegmentToLineSegment_Data))]
        public void LineSegmentToLineSegment(PointF a0, PointF a1, PointF b0, PointF b1, PointF? expected)
        {
            PointF ip = default;

            bool result = Intersect.LineSegmentToLineSegment(a0, a1, b0, b1, ref ip);
            Assert.Equal(result, expected.HasValue);
            if (expected.HasValue)
            {
                Assert.Equal(expected.Value, ip);
            }
        }
    }
}
