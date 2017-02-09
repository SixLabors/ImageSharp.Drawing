using ImageSharp;
using System;
using System.IO;

namespace SixLabors.Shapes.DrawShapesWithImageSharp
{
    using System.Numerics;

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

            OutputDrawnShape();
            OutputDrawnShapeHourGlass();

            DrawOval();
        }

        private static void DrawOval()
        {
            new Ellipse(0, 0, 10, 20).Scale(5).SaveImage("Curves", "Ellipse.png");
        }

        private static void OutputDrawnShape()
        {
            // center the shape outerRadii + 10 px away from edges
            var sb = new PathBuilder();

            // draw a 'V'
            sb.AddLines(new Vector2(10, 10), new Vector2(20, 20), new Vector2(30, 10));
            sb.StartFigure();

            // overlay rectangle
            sb.AddLine(new Vector2(15, 0), new Vector2(25, 0));
            sb.AddLine(new Vector2(25, 30), new Vector2(15, 30));
            sb.CloseFigure();

            sb.Build().Translate(0, 10).Scale(10).SaveImage("drawing", $"paths.png");
        }

        private static void OutputDrawnShapeHourGlass()
        {
            // center the shape outerRadii + 10 px away from edges
            var sb = new PathBuilder();

            // draw a 'V'
            sb.AddLines(new Vector2(10, 10), new Vector2(20, 20), new Vector2(30, 10));
            sb.StartFigure();

            // overlay rectangle
            sb.AddLine(new Vector2(15, 0), new Vector2(25, 0));
            sb.AddLine(new Vector2(15, 30), new Vector2(25, 30));
            sb.CloseFigure();

            sb.Build().Translate(0, 10).Scale(10).SaveImage("drawing", $"HourGlass.png");
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

        public static void SaveImage(this IPath shape, params string[] path)
        {
            shape = shape.Translate(shape.Bounds.Location * -1) // touch top left
                    .Translate(new Vector2(10)); // move in from top left

            var fullPath = System.IO.Path.GetFullPath(System.IO.Path.Combine("Output", System.IO.Path.Combine(path)));
            // pad even amount around shape
            int width = (int)(shape.Bounds.Left + shape.Bounds.Right);
            int height = (int)(shape.Bounds.Top + shape.Bounds.Bottom);

            using (var img = new Image(width, height))
            {
                img.Fill(Color.DarkBlue);

                // In ImageSharp.Drawing.Paths there is an extension method that takes in an IShape directly.
                img.Fill(Color.HotPink, new ShapeRegion(shape));
                img.Draw(Color.LawnGreen, 1, new ShapePath(shape));

                // Ensure directory exists
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(fullPath));

                using (var fs = File.Create(fullPath))
                {
                    img.SaveAsPng(fs);
                }
            }
        }
    }
}
