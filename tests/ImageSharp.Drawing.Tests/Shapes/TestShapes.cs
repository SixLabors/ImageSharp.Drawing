// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Numerics;

namespace SixLabors.ImageSharp.Drawing.Tests.Shapes
{
    public static class TestShapes
    {
        public static IPath IrisSegment(int rotationPos)
        {
            var center = new Vector2(603);
            var segmentRotationCenter = new Vector2(301.16968f, 301.16974f);
            IPath segment = new Polygon(
                new LinearLineSegment(new Vector2(230.54f, 361.0261f), new Vector2(5.8641942f, 361.46031f)),
                new CubicBezierLineSegment(
                    new Vector2(5.8641942f, 361.46031f),
                    new Vector2(-11.715693f, 259.54052f),
                    new Vector2(24.441609f, 158.17478f),
                    new Vector2(78.26f, 97.0461f))).Translate(center - segmentRotationCenter);

            float angle = rotationPos * ((float)Math.PI / 3);
            return segment.Transform(Matrix3x2.CreateRotation(angle, center));
        }

        public static IPath IrisSegment(float size, int rotationPos)
        {
            float scalingFactor = size / 1206;

            var center = new Vector2(603);
            var segmentRotationCenter = new Vector2(301.16968f, 301.16974f);
            IPath segment = new Polygon(
                new LinearLineSegment(new Vector2(230.54f, 361.0261f), new Vector2(5.8641942f, 361.46031f)),
                new CubicBezierLineSegment(
                    new Vector2(5.8641942f, 361.46031f),
                    new Vector2(-11.715693f, 259.54052f),
                    new Vector2(24.441609f, 158.17478f),
                    new Vector2(78.26f, 97.0461f))).Translate(center - segmentRotationCenter);

            float angle = rotationPos * ((float)Math.PI / 3);

            IPath rotated = segment.Transform(Matrix3x2.CreateRotation(angle, center));

            var scaler = Matrix3x2.CreateScale(scalingFactor, Vector2.Zero);
            IPath scaled = rotated.Transform(scaler);

            return scaled;
        }

        public static IPath HourGlass()
        {
            // center the shape outerRadii + 10 px away from edges
            var sb = new PathBuilder();

            // overlay rectangle
            sb.AddLine(new Vector2(15, 0), new Vector2(25, 0));
            sb.AddLine(new Vector2(15, 30), new Vector2(25, 30));
            sb.CloseFigure();

            return sb.Build().Translate(0, 10).Scale(10);
        }
    }
}
