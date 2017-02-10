using ImageSharp;
using System;
using System.IO;

namespace SixLabors.Shapes.DrawShapesWithImageSharp
{
    using System.Linq;
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
            DrawArc();
            DrawSerializedOPenSansLetterShape();
        }

        private static void DrawSerializedOPenSansLetterShape()
        {
            var path = @"36.57813x49.16406 35.41797x43.67969 35.41797x43.67969 35.13672x43.67969 35.13672x43.67969 34.41629x44.54843 33.69641x45.34412 32.97708x46.06674 32.2583x46.71631 31.54007x47.29282 30.82239x47.79626 30.10526x48.22665 29.38867x48.58398 29.38867x48.58398 28.65012x48.88474 27.86707x49.14539 27.03952x49.36594 26.16748x49.54639 25.25095x49.68674 24.28992x49.78699 23.28439x49.84714 22.23438x49.86719 22.23438x49.86719 21.52775x49.85564 20.84048x49.82104 20.17258x49.76337 19.52405x49.68262 18.28506x49.4519 17.12354x49.12891 16.03946x48.71362 15.03284x48.20605 14.10367x47.6062 13.25195x46.91406 13.25195x46.91406 12.48978x46.13678 11.82922x45.28149 11.27029x44.34821 10.81299x43.33691 10.45731x42.24762 10.20325x41.08032 10.05081x39.83502 10.0127x39.18312 10x38.51172 10x38.51172 10.01823x37.79307 10.07292x37.09613 10.16407x36.42088 10.29169x35.76733 10.6563x34.52533 11.16675x33.37012 11.82304x32.3017 12.62518x31.32007 13.57317x30.42523 14.10185x30.01036 14.66699x29.61719 15.2686x29.24571 15.90666x28.89594 16.58119x28.56786 17.29218x28.26147 18.03962x27.97679 18.82353x27.71381 19.6439x27.47252 20.50073x27.25293 22.32378x26.87885 24.29266x26.59155 26.40739x26.39105 28.66797x26.27734 28.66797x26.27734 35.20703x26.06641 35.20703x26.06641 35.20703x23.67578 35.20703x23.67578 35.17654x22.57907 35.08508x21.55652 34.93265x20.60812 34.71924x19.73389 34.44485x18.93381 34.1095x18.20789 33.71317x17.55612 33.25586x16.97852 33.25586x16.97852 32.73154x16.47177 32.13416x16.03259 31.46371x15.66098 30.72021x15.35693 29.90366x15.12045 29.01404x14.95154 28.05136x14.85019 27.01563x14.81641 27.01563x14.81641 25.79175x14.86255 24.52832x15.00098 23.88177x15.1048 23.22534x15.23169 21.88281x15.55469 20.50073x15.96997 19.0791x16.47754 17.61792x17.07739 16.11719x17.76953 16.11719x17.76953 14.32422x13.30469 14.32422x13.30469 15.04465x12.92841 15.7821x12.573 17.30811x11.9248 18.90222x11.36011 20.56445x10.87891 20.56445x10.87891 22.26184x10.49438 23.96143x10.21973 24.81204x10.1236 25.66321x10.05493 26.51492x10.01373 27.36719x10 27.36719x10 29.03409x10.04779 29.82572x10.10753 30.58948x10.19116 31.32536x10.29869 32.03336x10.43011 32.71348x10.58543 33.36572x10.76465 34.58658x11.19476 35.69592x11.72046 36.69376x12.34174 37.58008x13.05859 37.58008x13.05859 38.35873x13.88092 39.03357x14.8186 39.60458x15.87164 40.07178x17.04004 40.26644x17.6675 40.43515x18.32379 40.5779x19.00893 40.6947x19.7229 40.78555x20.46571 40.85043x21.23737 40.88937x22.03786 40.90234x22.86719 40.90234x22.86719 40.90234x49.16406 
23.39453x45.05078 24.06655x45.03911 24.72031x45.00409 25.97302x44.86401 27.15268x44.63055 28.25928x44.30371 29.29282x43.88348 30.2533x43.36987 31.14072x42.76288 31.95508x42.0625 31.95508x42.0625 32.6843x41.27808 33.31628x40.41895 33.85104x39.48511 34.28857x38.47656 34.62888x37.39331 34.87195x36.23535 35.01779x35.00269 35.06641x33.69531 35.06641x33.69531 35.06641x30.21484 35.06641x30.21484 29.23047x30.46094 29.23047x30.46094 27.55093x30.54855 25.9928x30.68835 24.55606x30.88034 23.24072x31.12451 22.04678x31.42087 20.97424x31.76941 20.0231x32.17014 19.19336x32.62305 19.19336x32.62305 18.47238x33.13528 17.84753x33.71399 17.31882x34.35916 16.88623x35.0708 16.54977x35.84891 16.30945x36.69348 16.16525x37.60452 16.11719x38.58203 16.11719x38.58203 16.14713x39.34943 16.23694x40.06958 16.38663x40.74249 16.59619x41.36816 17.19495x42.47778 18.0332x43.39844 18.0332x43.39844 19.08679x44.12134 19.68527x44.40533 20.33154x44.6377 21.0256x44.81842 21.76746x44.94751 22.5571x45.02496 23.39453x45.05078";
            var paths = path.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            var polys = paths.Select(line => {
                var pl = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

                var points = pl.Select(p => p.Split('x'))
                            .Select(p => {
                                return new Vector2(float.Parse(p[0]), float.Parse(p[1]));
                            })
                            .ToArray();
                return new Polygon(new LinearLineSegment(points));
            }).ToArray();
            var complex = new ComplexPolygon(polys);
            complex.SaveImage("letter", "a.png");
        }

        private static void DrawOval()
        {
            new Ellipse(0, 0, 10, 20).Scale(5).SaveImage("Curves", "Ellipse.png");
        }

        private static void DrawArc()
        {
            new Polygon(new BezierLineSegment( new[] {
                        new Vector2(10, 400),
                        new Vector2(30, 10),
                        new Vector2(240, 30),
                        new Vector2(300, 400)
            })).SaveImage("Curves", "Arc.png");
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
                img.Fill(Color.HotPink, new ShapeRegion(shape), new ImageSharp.Drawing.GraphicsOptions(true));
               // img.Draw(Color.LawnGreen, 1, new ShapePath(shape));

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
