// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace SixLabors.Shapes.DrawShapesWithImageSharp;

public static class ImageSharpLogo
{
    public static void SaveLogo(float size, string path)
    {
        // the point are based on a 1206x1206 shape so size requires scaling from there
        float scalingFactor = size / 1206;

        Vector2 center = new(603);

        // segment whose center of rotation should be
        Vector2 segmentOffset = new(301.16968f, 301.16974f);
        IPath segment = new Polygon(
            new LinearLineSegment(new Vector2(230.54f, 361.0261f), new Vector2(5.8641942f, 361.46031f)),
            new CubicBezierLineSegment(
                new Vector2(5.8641942f, 361.46031f),
                new Vector2(-11.715693f, 259.54052f),
                new Vector2(24.441609f, 158.17478f),
                new Vector2(78.26f, 97.0461f))).Translate(center - segmentOffset);

        // we need to create 6 of theses all rotated about the center point
        List<IPath> segments = [];
        for (int i = 0; i < 6; i++)
        {
            float angle = i * ((float)Math.PI / 3);
            IPath s = segment.Transform(Matrix3x2.CreateRotation(angle, center));
            segments.Add(s);
        }

        List<Color> colors =
        [
            Color.ParseHex("35a849"),
            Color.ParseHex("fcee21"),
            Color.ParseHex("ed7124"),
            Color.ParseHex("cb202d"),
            Color.ParseHex("5f2c83"),
            Color.ParseHex("085ba7")
        ];

        Matrix3x2 scaler = Matrix3x2.CreateScale(scalingFactor, Vector2.Zero);

        int dimensions = (int)Math.Ceiling(size);
        using (Image<Rgba32> img = new(dimensions, dimensions))
        {
            img.Mutate(i => i.Fill(Color.Black));
            img.Mutate(i => i.Fill(Color.ParseHex("e1e1e1ff"), new EllipsePolygon(center, 600f).Transform(scaler)));
            img.Mutate(i => i.Fill(Color.White, new EllipsePolygon(center, 600f - 60).Transform(scaler)));

            for (int s = 0; s < 6; s++)
            {
                img.Mutate(i => i.Fill(colors[s], segments[s].Transform(scaler)));
            }

            img.Mutate(i => i.Fill(Color.FromPixel(new Rgba32(0, 0, 0, 170)), new ComplexPolygon(new EllipsePolygon(center, 161f), new EllipsePolygon(center, 61f)).Transform(scaler)));

            string fullPath = System.IO.Path.GetFullPath(System.IO.Path.Combine("Output", path));

            img.Save(fullPath);
        }
    }
}
