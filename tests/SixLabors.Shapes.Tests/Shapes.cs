using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;

namespace SixLabors.Shapes.Tests
{
    public static class Shapes
    {
        public static IPath IrisSegment(int rotationPos)
        {
            var center = new Vector2(603);
            var segmentRotationCenter = new Vector2(301.16968f, 301.16974f);
            var segment = new Polygon(new LinearLineSegment(new Vector2(230.54f, 361.0261f), new System.Numerics.Vector2(5.8641942f, 361.46031f)),
                new BezierLineSegment(new Vector2(5.8641942f, 361.46031f),
                new Vector2(-11.715693f, 259.54052f),
                new Vector2(24.441609f, 158.17478f),
                new Vector2(78.26f, 97.0461f))).Translate(center - segmentRotationCenter);

            float angle = rotationPos * ((float)Math.PI / 3);
            return segment.Transform(Matrix3x2.CreateRotation(angle, center));
        } 
    }
}
