// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System.Numerics;
using SixLabors.ImageSharp.Drawing.Utilities;
using Xunit;

namespace SixLabors.ImageSharp.Drawing.Tests.Drawing.Utils
{
    public class IntersectTests
    {
        public static TheoryData<(float x, float y), (float x, float y), (float x, float y), (float x, float y), (float x, float y)?> LineSegmentToLineSegment_Data =
            new ()
            {
                { (0, 0), (2, 3), (1, 3), (1, 0), (1, 1.5f) },
                { (3, 1), (3, 3), (3, 2), (4, 2), (3, 2)},
                { (1, -3), (3, -1), (3, -4), (2, -2), (2, -2)},
                { (0, 0), (2, 1), (2, 1.0001f), (5, 2), (2, 1)}, // Robust to inaccuracies
                { (0, 0), (2, 3), (1, 3), (1, 2), null },
                { (-3, 3), (-1, 3), (-3, 2), (-1, 2), null},
                { (-4, 3), (-4, 1), (-5, 3), (-5, 1), null},
                { (0, 0), (4, 1), (4, 1), (8, 2), null}, // Collinear intersections are ignored
                { (0, 0), (4, 1), (4, 1.0001f), (8, 2), null}, // Collinear intersections are ignored
            };

        [Theory]
        [MemberData(nameof(LineSegmentToLineSegment_Data))]
        public void LineSegmentToLineSegmentNoCollinear(
            (float x, float y) a0,
            (float x, float y) a1,
            (float x, float y) b0,
            (float x, float y) b1,
            (float x, float y)? expected)
        {
            Vector2 ip = default;

            bool result = Intersect.LineSegmentToLineSegmentIgnoreCollinear(P(a0), P(a1), P(b0), P(b1), ref ip);
            Assert.Equal(result, expected.HasValue);
            if (expected.HasValue)
            {
                Assert.Equal(P(expected.Value), ip, new ApproximateFloatComparer(1e-3f));
            }

            static Vector2 P((float x, float y) p) => new Vector2(p.x, p.y);
        }
    }
}
