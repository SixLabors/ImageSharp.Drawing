using ImageSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;

namespace SixLabors.Shapes.DrawShapesWithImageSharp
{
    public static class ImageSharpLogo
    {
        public static void SaveLogo(params string[] path)
        {
            var center = new Vector2(603);

            // segment whoes cetner of rotation should be 
            var segmentOffset = new Vector2(301.16968f, 301.16974f);
            var segment = new Polygon(new LinearLineSegment(new Vector2(230.54f, 361.0261f), new System.Numerics.Vector2(5.8641942f, 361.46031f)),
                new BezierLineSegment(new Vector2(5.8641942f, 361.46031f),
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

            List<Color> colors = new List<Color>() {
                Color.FromHex("35a849"),
                Color.FromHex("fcee21"),
                Color.FromHex("ed7124"),
                Color.FromHex("cb202d"),
                Color.FromHex("5f2c83"),
                Color.FromHex("085ba7"),
            };

            using (var img = new Image(1206, 1206))
            {
                img.Fill(Color.Black);
                img.Fill(Color.FromHex("e1e1e1ff"), new ShapeRegion(new SixLabors.Shapes.Ellipse(center, 600f)));
                img.Fill(Color.White, new ShapeRegion(new SixLabors.Shapes.Ellipse(center, 600f - 60)));

                for (var i = 0; i < 6; i++)
                {
                    img.Fill(colors[i], new ShapeRegion(segments[i]));
                }

                img.Fill(new Color(0, 0, 0, 170), new ShapeRegion(new ComplexPolygon(new SixLabors.Shapes.Ellipse(center, 161f), new SixLabors.Shapes.Ellipse(center, 61f))));

                var fullPath = System.IO.Path.GetFullPath(System.IO.Path.Combine("Output", System.IO.Path.Combine(path)));

                img.Save(fullPath);
            }

       
        }
    }
}
