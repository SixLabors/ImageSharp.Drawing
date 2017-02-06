using ImageSharp;
using System;
using System.IO;

namespace SixLabors.Shapes.DrawShapesWithImageSharp
{
    public static class Program
    {
        public static void Main(string[] args)
        {
            OutputClippedRectangle();
            OutputStars();
        }

        private static void OutputStars()
        {
            OutputStar(3, 5);
            OutputStar(4);
            OutputStar(5);
            OutputStar(6);
            OutputStar(20, 100, 200);
        }

        private static void OutputStar(int points, float inner = 10, float outer = 20)
        {
            // center the shape outerRadii + 10 px away from edges
            var offset = outer + 10;

            var star = new Star(offset, offset, points, inner, outer);
            star.SaveImage("Stars", $"Star_{points}.png");
        }

        private static void OutputClippedRectangle()
        {
            var rect1 = new Rectangle(10, 10, 40, 40);
            var rect2 = new Rectangle(20, 0, 20, 20);
            var paths = rect1.Clip(rect2);

            paths.SaveImage("Clipping", "RectangleWithTopClipped.png");
        }

        public static void SaveImage(this IShape shape, params string[] path)
        {
            var fullPath = System.IO.Path.GetFullPath(System.IO.Path.Combine("Output", System.IO.Path.Combine(path)));
            // pad even amount around shape
            int width =(int)(shape.Bounds.Left + shape.Bounds.Right);
            int height = (int)(shape.Bounds.Top + shape.Bounds.Bottom);

            using (var img = new Image(width, height))
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
