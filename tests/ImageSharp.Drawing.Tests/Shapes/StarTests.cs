// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Xunit;

namespace SixLabors.ImageSharp.Drawing.Tests
{
    public class StarTests
    {
        [Theory]
        [InlineData(0, true)]
        [InlineData(1, true)]
        [InlineData(2, true)]
        [InlineData(3, false)]
        [InlineData(4, false)]
        public void RequiresAtleast3Verticies(int points, bool throws)
        {
            if (throws)
            {
                ArgumentOutOfRangeException ex = Assert.Throws<ArgumentOutOfRangeException>(() => new Star(Vector2.Zero, points, 10f, 20f, 0));

                Assert.Equal("prongs", ex.ParamName);
            }
            else
            {
                var p = new Star(Vector2.Zero, points, 10f, 20f, 0);
                Assert.NotNull(p);
            }
        }

        [Theory]
        [InlineData(-1, true)]
        [InlineData(0, true)]
        [InlineData(0.00001, false)]
        [InlineData(1, false)]
        public void RadiusMustBeGreateThan0(float radius, bool throws)
        {
            if (throws)
            {
                ArgumentOutOfRangeException ex = Assert.Throws<ArgumentOutOfRangeException>(() => new Star(Vector2.Zero, 3, radius, 20f, 0));
                ArgumentOutOfRangeException ex2 = Assert.Throws<ArgumentOutOfRangeException>(() => new Star(Vector2.Zero, 3, 20f, radius, 0));

                Assert.Equal("innerRadii", ex.ParamName);
                Assert.Equal("outerRadii", ex2.ParamName);
            }
            else
            {
                var p = new Star(Vector2.Zero, 3, radius, radius, 0);
                Assert.NotNull(p);
            }
        }

        [Fact]
        public void GeneratesCorrectPath()
        {
            const float Radius = 5;
            const float Radius2 = 30;
            int pointsCount = new Random().Next(3, 20);

            var poly = new Star(Vector2.Zero, pointsCount, Radius, Radius2, 0);

            PointF[] points = poly.Flatten().ToArray()[0].Points.ToArray();

            // calcualte baselineDistance
            float baseline = Vector2.Distance(points[0], points[1]);

            // all points are extact the same distance away from the center
            Assert.Equal(pointsCount * 2, points.Length);
            for (int i = 0; i < points.Length; i++)
            {
                int j = i - 1;
                if (j < 0)
                {
                    j += points.Length;
                }

                float actual = Vector2.Distance(points[i], points[j]);
                Assert.Equal(baseline, actual, 3);
                if (i % 2 == 1)
                {
                    Assert.Equal(Radius, Vector2.Distance(Vector2.Zero, points[i]), 3);
                }
                else
                {
                    Assert.Equal(Radius2, Vector2.Distance(Vector2.Zero, points[i]), 3);
                }
            }
        }

        [Fact]
        public void AngleChangesOnePointToStartAtThatPosition()
        {
            const double TwoPI = 2 * Math.PI;
            const float Radius = 10;
            const float Radius2 = 20;
            double anAngle = new Random().NextDouble() * TwoPI;

            var poly = new Star(Vector2.Zero, 3, Radius, Radius2, (float)anAngle);
            ISimplePath[] points = poly.Flatten().ToArray();

            IEnumerable<double> allAngles = points[0].Points.ToArray().Select(b => Math.Atan2(b.Y, b.X))
                .Select(x => x < 0 ? x + TwoPI : x); // normalise it from +/- PI to 0 to TwoPI

            Assert.Contains(allAngles, a => Math.Abs(a - anAngle) > 0.000001);
        }
    }
}
