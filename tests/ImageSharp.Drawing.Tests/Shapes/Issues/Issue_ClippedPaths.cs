// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using Xunit;

namespace SixLabors.ImageSharp.Drawing.Tests.Issues
{
    public class Issue_ClippedPaths
    {
        [Fact]
        public void ClippedTriangle()
        {
            var simplePath = new Polygon(new LinearLineSegment(
                           new PointF(10, 10),
                           new PointF(200, 150),
                           new PointF(50, 300)));

            var hole1 = new Polygon(new LinearLineSegment(
                            new PointF(37, 85),
                            new PointF(93, 85),
                            new PointF(65, 137)));

            IPath clippedPath = simplePath.Clip(hole1);
            IPath outline = clippedPath.GenerateOutline(5, new[] { 1f });

            Assert.False(outline.Contains(new PointF(74, 97)));
        }
    }
}
