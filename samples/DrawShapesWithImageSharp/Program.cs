using ImageSharp;
using System;
using System.IO;

namespace SixLabors.Shapes.DrawShapesWithImageSharp
{
    public static class Program
    {
        public static void Main(string[] args)
        {
            GenerateClippedRectangle();
        }

        private static void GenerateClippedRectangle()
        {
            var rect1 = new Rectangle(10, 10, 40, 40);
            var rect2 = new Rectangle(20, 0, 20, 20);
            var paths = rect1.Clip(rect2);

            paths.SaveImage("Clipping", "RectangleWithTopClipped.png");
        }

        private static void SaveImage(this IShape shape, params string[] path)
        {
            var fullPath = System.IO.Path.GetFullPath(System.IO.Path.Combine("Output", System.IO.Path.Combine(path)));
            // pad even amount around shape
            var width = shape.Bounds.Left + shape.Bounds.Right;
            var height = shape.Bounds.Top + shape.Bounds.Bottom;

            using (var img = new Image((int)Math.Ceiling(width), (int)Math.Ceiling(height)))
            {
                img.Fill(Color.DarkBlue);

                // In ImageSharp.Drawing.Paths there is an extension method that takes in an IShape directly.
                img.Fill(Color.HotPink, new ShapeRegion(shape));
                img.Draw(Color.LawnGreen, 1, new ShapePath(shape));

                //ensure directory exists
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(fullPath));

                using (var fs = File.Create(fullPath))
                {
                    img.SaveAsPng(fs);
                }
            }
        }
    }
}
