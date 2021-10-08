// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Linq;
using System.Numerics;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using IODirectory = System.IO.Directory;
using IOPath = System.IO.Path;

namespace SixLabors.Shapes.DrawShapesWithImageSharp
{
    public static class Program
    {
        public static void Main(string[] args)
        {
            OutputClippedRectangle();
            OutputStars();

            ImageSharpLogo.SaveLogo(300, "ImageSharp.png");
        }

        private static void OutputStars()
        {
            OutputStarOutline(5, 150, 250, width: 20, jointStyle: JointStyle.Miter);
            OutputStarOutline(5, 150, 250, width: 20, jointStyle: JointStyle.Round);
            OutputStarOutline(5, 150, 250, width: 20, jointStyle: JointStyle.Square);

            OutputStarOutlineDashed(5, 150, 250, width: 20, jointStyle: JointStyle.Square, cap: EndCapStyle.Butt);
            OutputStarOutlineDashed(5, 150, 250, width: 20, jointStyle: JointStyle.Round, cap: EndCapStyle.Round);
            OutputStarOutlineDashed(5, 150, 250, width: 20, jointStyle: JointStyle.Square, cap: EndCapStyle.Square);

            OutputStar(3, 5);
            OutputStar(4);
            OutputStar(5);
            OutputStar(6);
            OutputStar(20, 100, 200);

            OutputDrawnShape();
            OutputDrawnShapeHourGlass();

            DrawOval();
            DrawArc();
            DrawSerializedOPenSansLetterShape_a();
            DrawSerializedOPenSansLetterShape_o();

            DrawFatL();

            DrawText("Hello World");
            DrawText(
                "Hello World Hello World Hello World Hello World Hello World Hello World Hello World",
                new Path(new CubicBezierLineSegment(
                new Vector2(0, 0),
                new Vector2(150, -150),
                new Vector2(250, -150),
                new Vector2(400, 0))));
        }

        private static void DrawText(string text)
        {
            FontFamily fam = SystemFonts.Get("Arial");
            var font = new Font(fam, 30);
            var style = new RendererOptions(font, 72);
            IPathCollection glyphs = TextBuilder.GenerateGlyphs(text, style);

            glyphs.SaveImage("Text", text + ".png");
        }

        private static void DrawText(string text, IPath path)
        {
            FontFamily fam = SystemFonts.Get("Arial");
            var font = new Font(fam, 30);
            var style = new RendererOptions(font, 72)
            {
                WrappingWidth = path.ComputeLength(),
                VerticalAlignment = VerticalAlignment.Top,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            IPathCollection glyphs = TextBuilder.GenerateGlyphs(text, path, style);

            glyphs.SaveImage("Text-Path", text + ".png");
        }

        private static void DrawFatL()
        {
            var shape = new Polygon(new LinearLineSegment(
                new Vector2(8, 8),
                new Vector2(64, 8),
                new Vector2(64, 64),
                new Vector2(120, 64),
                new Vector2(120, 120),
                new Vector2(8, 120)));
            shape.SaveImage("Issues", "ClippedCorner.png");
        }

        private static void DrawSerializedOPenSansLetterShape_a()
        {
            const string path = @"36.57813x49.16406 35.41797x43.67969 35.41797x43.67969 35.13672x43.67969 35.13672x43.67969 34.41629x44.54843 33.69641x45.34412 32.97708x46.06674 32.2583x46.71631 31.54007x47.29282 30.82239x47.79626 30.10526x48.22665 29.38867x48.58398 29.38867x48.58398 28.65012x48.88474 27.86707x49.14539 27.03952x49.36594 26.16748x49.54639 25.25095x49.68674 24.28992x49.78699 23.28439x49.84714 22.23438x49.86719 22.23438x49.86719 21.52775x49.85564 20.84048x49.82104 20.17258x49.76337 19.52405x49.68262 18.28506x49.4519 17.12354x49.12891 16.03946x48.71362 15.03284x48.20605 14.10367x47.6062 13.25195x46.91406 13.25195x46.91406 12.48978x46.13678 11.82922x45.28149 11.27029x44.34821 10.81299x43.33691 10.45731x42.24762 10.20325x41.08032 10.05081x39.83502 10.0127x39.18312 10x38.51172 10x38.51172 10.01823x37.79307 10.07292x37.09613 10.16407x36.42088 10.29169x35.76733 10.6563x34.52533 11.16675x33.37012 11.82304x32.3017 12.62518x31.32007 13.57317x30.42523 14.10185x30.01036 14.66699x29.61719 15.2686x29.24571 15.90666x28.89594 16.58119x28.56786 17.29218x28.26147 18.03962x27.97679 18.82353x27.71381 19.6439x27.47252 20.50073x27.25293 22.32378x26.87885 24.29266x26.59155 26.40739x26.39105 28.66797x26.27734 28.66797x26.27734 35.20703x26.06641 35.20703x26.06641 35.20703x23.67578 35.20703x23.67578 35.17654x22.57907 35.08508x21.55652 34.93265x20.60812 34.71924x19.73389 34.44485x18.93381 34.1095x18.20789 33.71317x17.55612 33.25586x16.97852 33.25586x16.97852 32.73154x16.47177 32.13416x16.03259 31.46371x15.66098 30.72021x15.35693 29.90366x15.12045 29.01404x14.95154 28.05136x14.85019 27.01563x14.81641 27.01563x14.81641 25.79175x14.86255 24.52832x15.00098 23.88177x15.1048 23.22534x15.23169 21.88281x15.55469 20.50073x15.96997 19.0791x16.47754 17.61792x17.07739 16.11719x17.76953 16.11719x17.76953 14.32422x13.30469 14.32422x13.30469 15.04465x12.92841 15.7821x12.573 17.30811x11.9248 18.90222x11.36011 20.56445x10.87891 20.56445x10.87891 22.26184x10.49438 23.96143x10.21973 24.81204x10.1236 25.66321x10.05493 26.51492x10.01373 27.36719x10 27.36719x10 29.03409x10.04779 29.82572x10.10753 30.58948x10.19116 31.32536x10.29869 32.03336x10.43011 32.71348x10.58543 33.36572x10.76465 34.58658x11.19476 35.69592x11.72046 36.69376x12.34174 37.58008x13.05859 37.58008x13.05859 38.35873x13.88092 39.03357x14.8186 39.60458x15.87164 40.07178x17.04004 40.26644x17.6675 40.43515x18.32379 40.5779x19.00893 40.6947x19.7229 40.78555x20.46571 40.85043x21.23737 40.88937x22.03786 40.90234x22.86719 40.90234x22.86719 40.90234x49.16406 
23.39453x45.05078 24.06655x45.03911 24.72031x45.00409 25.97302x44.86401 27.15268x44.63055 28.25928x44.30371 29.29282x43.88348 30.2533x43.36987 31.14072x42.76288 31.95508x42.0625 31.95508x42.0625 32.6843x41.27808 33.31628x40.41895 33.85104x39.48511 34.28857x38.47656 34.62888x37.39331 34.87195x36.23535 35.01779x35.00269 35.06641x33.69531 35.06641x33.69531 35.06641x30.21484 35.06641x30.21484 29.23047x30.46094 29.23047x30.46094 27.55093x30.54855 25.9928x30.68835 24.55606x30.88034 23.24072x31.12451 22.04678x31.42087 20.97424x31.76941 20.0231x32.17014 19.19336x32.62305 19.19336x32.62305 18.47238x33.13528 17.84753x33.71399 17.31882x34.35916 16.88623x35.0708 16.54977x35.84891 16.30945x36.69348 16.16525x37.60452 16.11719x38.58203 16.11719x38.58203 16.14713x39.34943 16.23694x40.06958 16.38663x40.74249 16.59619x41.36816 17.19495x42.47778 18.0332x43.39844 18.0332x43.39844 19.08679x44.12134 19.68527x44.40533 20.33154x44.6377 21.0256x44.81842 21.76746x44.94751 22.5571x45.02496 23.39453x45.05078";
            string[] paths = path.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            Polygon[] polys = paths.Select(line =>
            {
                string[] pl = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

                PointF[] points = pl.Select(p => p.Split('x'))
                            .Select(p => new PointF(float.Parse(p[0]), float.Parse(p[1])))
                            .ToArray();
                return new Polygon(new LinearLineSegment(points));
            }).ToArray();

            var complex = new ComplexPolygon(polys);
            complex.SaveImage("letter", "a.png");
        }

        private static void DrawSerializedOPenSansLetterShape_o()
        {
            const string path = @"45.40234x29.93359 45.3838x31.09519 45.32819x32.22452 45.23549x33.32157 45.10571x34.38635 44.93886x35.41886 44.73492x36.4191 44.49391x37.38706 44.21582x38.32275 43.90065x39.22617 43.5484x40.09732 43.15907x40.9362 42.73267x41.7428 42.26918x42.51713 41.76862x43.25919 41.23097x43.96897 40.65625x44.64648 40.65625x44.64648 40.04884x45.28719 39.41315x45.88657 38.74916x46.4446 38.05688x46.9613 37.33632x47.43667 36.58746x47.8707 35.81032x48.26339 35.00488x48.61475 34.17116x48.92477 33.30914x49.19345 32.41884x49.4208 31.50024x49.60681 30.55336x49.75149 29.57819x49.85483 28.57472x49.91683 27.54297x49.9375 27.54297x49.9375 26.2691x49.8996 25.03149x49.78589 23.83014x49.59637 22.66504x49.33105 21.53619x48.98993 20.4436x48.573 19.38727x48.08026 18.36719x47.51172 18.36719x47.51172 17.3938x46.87231 16.47754x46.16699 15.61841x45.39575 14.81641x44.55859 14.07153x43.65552 13.38379x42.68652 12.75317x41.65161 12.17969x40.55078 12.17969x40.55078 11.66882x39.39282 11.22607x38.18652 10.85144x36.93188 10.54492x35.62891 10.30652x34.27759 10.13623x32.87793 10.03406x31.42993 10x29.93359 10x29.93359 10.0184x28.77213 10.07361x27.64322 10.16562x26.54685 10.29443x25.48303 10.46005x24.45176 10.66248x23.45303 10.9017x22.48685 11.17773x21.55322 11.49057x20.65214 11.84021x19.7836 12.22665x18.94761 12.6499x18.14417 13.10995x17.37327 13.60681x16.63492 14.14047x15.92912 14.71094x15.25586 14.71094x15.25586 15.31409x14.61941 15.9458x14.02402 16.60608x13.46969 17.29492x12.95642 18.01233x12.48421 18.7583x12.05307 19.53284x11.66299 20.33594x11.31396 21.1676x11.006 22.02783x10.73911 22.91663x10.51327 23.83398x10.32849 24.77991x10.18478 25.75439x10.08212 26.75745x10.02053 27.78906x10 27.78906x10 28.78683x10.02101 29.75864x10.08405 30.70449x10.1891 31.62439x10.33618 32.51833x10.52528 33.38632x10.75641 34.22836x11.02956 35.04443x11.34473 35.83456x11.70192 36.59872x12.10114 37.33694x12.54237 38.04919x13.02563 38.7355x13.55092 39.39584x14.11823 40.03024x14.72755 40.63867x15.37891 40.63867x15.37891 41.21552x16.0661 41.75516x16.78296 42.25757x17.52948 42.72278x18.30566 43.15077x19.11151 43.54153x19.94702 43.89509x20.81219 44.21143x21.70703 44.49055x22.63153 44.73245x23.58569 44.93714x24.56952 45.10461x25.58301 45.23487x26.62616 45.32791x27.69897 45.38374x28.80145 45.40234x29.93359 
16.04688x29.93359 16.09302x31.72437 16.23145x33.40527 16.33527x34.20453 16.46216x34.97632 16.61212x35.72064 16.78516x36.4375 16.98126x37.12689 17.20044x37.78882 17.44269x38.42328 17.70801x39.03027 18.30786x40.16187 19x41.18359 19x41.18359 19.78168x42.08997 20.65015x42.87549 21.60541x43.54016 22.64746x44.08398 23.77631x44.50696 24.99194x44.80908 26.29437x44.99036 26.97813x45.03568 27.68359x45.05078 27.68359x45.05078 28.38912x45.03575 29.07309x44.99063 30.37634x44.81018 31.59335x44.50943 32.72412x44.08838 33.76865x43.54703 34.72693x42.88538 35.59897x42.10342 36.38477x41.20117 36.38477x41.20117 37.08102x40.18301 37.68445x39.05334 37.95135x38.44669 38.19504x37.81216 38.41552x37.14976 38.61279x36.45947 38.78686x35.74131 38.93771x34.99527 39.06536x34.22135 39.1698x33.41956 39.30905x31.73233 39.35547x29.93359 39.35547x29.93359 39.30905x28.15189 39.1698x26.48059 39.06536x25.68635 38.93771x24.91971 38.78686x24.18067 38.61279x23.46924 38.41552x22.78541 38.19504x22.12918 37.95135x21.50056 37.68445x20.89954 37.08102x19.7803 36.38477x18.77148 36.38477x18.77148 35.59787x17.87747 34.72253x17.10266 33.75876x16.44705 32.70654x15.91064 31.56589x15.49344 30.33679x15.19543 29.68908x15.09113 29.01926x15.01663 28.32732x14.97193 27.61328x14.95703 27.61328x14.95703 26.90796x14.97173 26.22461x15.01581 24.92383x15.19214 23.71094x15.48602 22.58594x15.89746 21.54883x16.42645 20.59961x17.073 19.73828x17.8371 18.96484x18.71875 18.96484x18.71875 18.28094x19.71686 17.68823x20.83032 17.42607x21.43031 17.18671x22.05914 16.97014x22.71681 16.77637x23.40332 16.60539x24.11867 16.45721x24.86285 16.33183x25.63588 16.22925x26.43774 16.09247x28.12799 16.04688x29.93359 ";
            string[] paths = path.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            Polygon[] polys = paths.Select(line =>
            {
                string[] pl = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

                PointF[] points = pl.Select(p => p.Split('x'))
                            .Select(p => new PointF(float.Parse(p[0]), float.Parse(p[1])))
                            .ToArray();

                return new Polygon(new LinearLineSegment(points));
            }).ToArray();

            var complex = new ComplexPolygon(polys);
            complex.SaveImage("letter", "o.png");
        }

        private static void DrawOval()
            => new EllipsePolygon(0, 0, 10, 20).Scale(5).SaveImage("Curves", "Ellipse.png");

        private static void DrawArc() => new Polygon(new CubicBezierLineSegment(
                new PointF[]
                {
                    new PointF(10, 400),
                    new PointF(30, 10),
                    new PointF(240, 30),
                    new PointF(300, 400),
                })).SaveImage(500, 500, "Curves", "Arc.png");

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

        private static void OutputStarOutline(int points, float inner = 10, float outer = 20, float width = 5, JointStyle jointStyle = JointStyle.Miter)
        {
            // center the shape outerRadii + 10 px away from edges
            float offset = outer + 10;

            var star = new Star(offset, offset, points, inner, outer);
            IPath outline = star.GenerateOutline(width, jointStyle);
            outline.SaveImage("Stars", $"StarOutline_{points}_{jointStyle}.png");
        }

        private static void OutputStarOutlineDashed(int points, float inner = 10, float outer = 20, float width = 5, JointStyle jointStyle = JointStyle.Miter, EndCapStyle cap = EndCapStyle.Butt)
        {
            // center the shape outerRadii + 10 px away from edges
            float offset = outer + 10;

            var star = new Star(offset, offset, points, inner, outer);
            IPath outline = star.GenerateOutline(width, new float[] { 3, 3 }, false, jointStyle, cap);
            outline.SaveImage("Stars", $"StarOutlineDashed_{points}_{jointStyle}_{cap}.png");
        }

        private static void OutputStar(int points, float inner = 10, float outer = 20)
        {
            // center the shape outerRadii + 10 px away from edges
            float offset = outer + 10;

            var star = new Star(offset, offset, points, inner, outer);
            star.SaveImage("Stars", $"Star_{points}.png");
        }

        private static void OutputClippedRectangle()
        {
            var rect1 = new RectangularPolygon(10, 10, 40, 40);
            var rect2 = new RectangularPolygon(20, 0, 20, 20);
            IPath paths = rect1.Clip(rect2);

            paths.SaveImage("Clipping", "RectangleWithTopClipped.png");
        }

        public static void SaveImage(this IPath shape, params string[] path) => new PathCollection(shape).SaveImage(path);

        public static void SaveImage(this IPathCollection shape, params string[] path)
        {
            shape = shape.Translate(-shape.Bounds.Location) // touch top left
                    .Translate(new Vector2(10)); // move in from top left

            string fullPath = IOPath.GetFullPath(IOPath.Combine("Output", IOPath.Combine(path)));

            // pad even amount around shape
            int width = (int)(shape.Bounds.Left + shape.Bounds.Right);
            int height = (int)(shape.Bounds.Top + shape.Bounds.Bottom);

            using (var img = new Image<Rgba32>(width, height))
            {
                img.Mutate(i => i.Fill(Color.DarkBlue));

                foreach (IPath s in shape)
                {
                    // In ImageSharp.Drawing.Paths there is an extension method that takes in an IShape directly.
                    img.Mutate(i => i.Fill(Color.HotPink, s));
                }

                // img.Draw(Color.LawnGreen, 1, new ShapePath(shape));

                // Ensure directory exists
                IODirectory.CreateDirectory(IOPath.GetDirectoryName(fullPath));

                img.Save(fullPath);
            }
        }

        public static void SaveImage(this IPath shape, int width, int height, params string[] path)
            => new PathCollection(shape).SaveImage(width, height, path);

        public static void SaveImage(this IPathCollection shape, int width, int height, params string[] path)
        {
            string fullPath = IOPath.GetFullPath(IOPath.Combine("Output", IOPath.Combine(path)));

            using (var img = new Image<Rgba32>(width, height))
            {
                img.Mutate(i => i.Fill(Color.DarkBlue));

                // In ImageSharp.Drawing.Paths there is an extension method that takes in an IShape directly.
                foreach (IPath s in shape)
                {
                    // In ImageSharp.Drawing.Paths there is an extension method that takes in an IShape directly.
                    img.Mutate(i => i.Fill(Color.HotPink, s));
                }

                // img.Draw(Color.LawnGreen, 1, new ShapePath(shape));

                // Ensure directory exists
                IODirectory.CreateDirectory(IOPath.GetDirectoryName(fullPath));

                img.Save(fullPath);
            }
        }
    }
}
