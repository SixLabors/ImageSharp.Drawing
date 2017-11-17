using System;
using System.Collections.Generic;
using System.Numerics;
using SixLabors.ImageSharp;

namespace SixLabors.Shapes.DrawShapesWithImageSharp
{
    public static class ImageSharpLogo
    {
        public static void SaveLogo(float size, string path)
        {
            // the point are based on a 1206x1206 shape so size requires scaling from there

            float scalingFactor = size / 1206;

            var center = new Vector2(603);

            // segment whoes cetner of rotation should be 
            var segmentOffset = new Vector2(301.16968f, 301.16974f);
            var segment = new Polygon(new LinearLineSegment(new Vector2(230.54f, 361.0261f), new System.Numerics.Vector2(5.8641942f, 361.46031f)),
                new CubicBezierLineSegment(new Vector2(5.8641942f, 361.46031f),
                new Vector2(-11.715693f, 259.54052f),
                new Vector2(24.441609f, 158.17478f),
                new Vector2(78.26f, 97.0461f))).Translate(center - segmentOffset);


            //we need to create 6 of theses all rotated about the center point
            List<IPath> segments = new List<IPath>();
            for (var i = 0; i < 6; i++)
            {
                float angle = i * ((float)Math.PI / 3);
                var s = segment.Transform(Matrix3x2.CreateRotation(angle, center));
                segments.Add(s);
            }

            List<Rgba32> colors = new List<Rgba32>() {
                Rgba32.FromHex("35a849"),
                Rgba32.FromHex("fcee21"),
                Rgba32.FromHex("ed7124"),
                Rgba32.FromHex("cb202d"),
                Rgba32.FromHex("5f2c83"),
                Rgba32.FromHex("085ba7"),
            };

            Matrix3x2 scaler = Matrix3x2.CreateScale(scalingFactor, Vector2.Zero);

            var dimensions = (int)Math.Ceiling(size);
            using (var img = new Image<Rgba32>(dimensions, dimensions))
            {
                img.Mutate(i => i.Fill(Rgba32.Black));
                img.Mutate(i => i.Fill(Rgba32.FromHex("e1e1e1ff"), new EllipsePolygon(center, 600f).Transform(scaler)));
                img.Mutate(i => i.Fill(Rgba32.White, new EllipsePolygon(center, 600f - 60).Transform(scaler)));

                for (var s = 0; s < 6; s++)
                {
                    img.Mutate(i => i.Fill(colors[s], segments[s].Transform(scaler)));
                }

                img.Mutate(i => i.Fill(new Rgba32(0, 0, 0, 170), new ComplexPolygon(new EllipsePolygon(center, 161f), new EllipsePolygon(center, 61f)).Transform(scaler)));

                var fullPath = System.IO.Path.GetFullPath(System.IO.Path.Combine("Output", path));

                img.Save(fullPath);
            }
        }
    }
}
