// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using SixLabors.ImageSharp.Drawing.Shapes.Helpers;
using SixLabors.ImageSharp.Drawing.Shapes.Scan;
using Xunit;

namespace SixLabors.ImageSharp.Drawing.Tests.Shapes.Scan
{
    public class TopologyUtilitiesTests0
    {
        private static PointF[] CreateTestPoints()
        {
            return PolygonFactory.CreatePointArray((10, 0), (20, 0), (20, 30), (10, 30), (10, 20), (0, 20), (0, 10), (10, 10), (10,0));
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void EnsureOrientation_Positive(bool isPositive)
        {
            PointF[] expected = CreateTestPoints();
            PointF[] polygon = expected.CloneArray();

            if (!isPositive)
            {
                polygon.AsSpan().Reverse();
            }
            
            TopologyUtilities.EnsureOrientation(polygon, 1);
            
            Assert.Equal(expected, polygon);
        }


        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void EnsureOrientation_Negative(bool isNegative)
        {
            PointF[] expected = CreateTestPoints();
            expected.AsSpan().Reverse();
            
            PointF[] polygon = expected.CloneArray();

            if (!isNegative)
            {
                polygon.AsSpan().Reverse();
            }
            
            TopologyUtilities.EnsureOrientation(polygon, -1);
            
            Assert.Equal(expected, polygon);
        }
    }
}