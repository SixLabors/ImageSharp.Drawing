using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Xunit;
using Xunit.Abstractions;

namespace SixLabors.ImageSharp.Drawing.Tests
{
    public class _IntersectionExperiments
    {
        private const float Inf = 100; 
        
        private readonly IBrush Brush = Brushes.Solid(Color.Red);

        private readonly ITestOutputHelper output;

        public _IntersectionExperiments(ITestOutputHelper output)
        {
            this.output = output;
        }

        private static Polygon  MakePolygon(params (float x, float y)[] coords)
        {
            PointF[] points = coords.Select(c => new PointF(c.x, c.y)).ToArray();
            return new Polygon(new LinearLineSegment(points));
        }

        private void DrawRegion(ITestImageProvider provider, IPath path, float scale = 100f)
        {
            path = path.Transform(Matrix3x2.CreateScale(scale) * Matrix3x2.CreateTranslation(10, 10));
            using Image image = provider.GetImage(ctx => ctx.Fill(Brush, path));

            image.DebugSave(provider, appendPixelTypeToFileName: false, appendSourceFileOrDescription: false);
        }

        private void DrawPath(ITestImageProvider provider, Polygon polygon, Polygon hole, float scale = 100f) =>
            DrawRegion(provider, new ComplexPolygon(polygon, hole), scale);
        
        private static (PointF Start, PointF End) MakeHLine(float y)
        {

            return (new PointF(-Inf, y), new PointF(Inf, y));
        }

        private void PrintIntersections(ReadOnlySpan<PointF> points, float y)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append($"@ y={y} | ");

            bool start = true;

            foreach (PointF p in points)
            {
                sb.Append($"({p.X},{p.Y})");
                if (start)
                {
                    sb.Append("--");
                }
                else
                {
                    sb.Append("  ");
                }
                // sb.Append(" ");

                start = !start;
            }
            this.output.WriteLine(sb.ToString());
        }

        private void PrintIntersections(IPath path, float y)
        {
            var line = MakeHLine(y);

            PointF[] points = path.FindIntersections(line.Start, line.End).ToArray();
            PrintIntersections(points, y);
        }
        
        [Theory]
        [WithBlankImages(1000, 1000, PixelTypes.Rgba32)]
        public void Case0<TPixel>(TestImageProvider<TPixel> provider)
            where TPixel : unmanaged, IPixel<TPixel>
        {
            IPath path = MakePolygon((0,0), (10,10), (20,0), (20,20), (0,20) );


            PrintIntersections(path, 10);

            DrawRegion(provider, path, 10);
        }

        [Theory]
        [WithBlankImages(1000, 1000, PixelTypes.Rgba32)]
        public void Case1<TPixel>(TestImageProvider<TPixel> provider)
            where TPixel : unmanaged, IPixel<TPixel>
        {
            IPath path = MakePolygon((0, 0), (2, 0), (3, 1), (3, 0), (6, 0), (6, 2), (5, 2), (5, 1), (4, 1), (4, 2), (2, 2), (1, 1), (0, 2));

            PrintIntersections(path, 0.5f);
            PrintIntersections(path, 1);
            PrintIntersections(path, 1.5f);

            DrawRegion(provider, path);
            
        }
        
        [Theory]
        [WithBlankImages(1000, 1000, PixelTypes.Rgba32)]
        public void Case2<TPixel>(TestImageProvider<TPixel> provider)
            where TPixel : unmanaged, IPixel<TPixel>
        {
            IPath path = MakePolygon((0, 3), (3, 3), (3, 0), (1, 2), (1, 1), (0, 0));
            
            PrintIntersections(path, 1);

            DrawRegion(provider, path);
        }
        
        
        [Theory]
        [WithBlankImages(1000, 1000, PixelTypes.Rgba32)]
        public void FindBothIntersections<TPixel>(TestImageProvider<TPixel> provider)
            where TPixel : unmanaged, IPixel<TPixel>
        {
            var poly = new Polygon(new LinearLineSegment(
                            new PointF(10, 10),
                            new PointF(200, 150),
                            new PointF(50, 300)));
            IEnumerable<PointF> intersections = poly.FindIntersections(new PointF(float.MinValue, 55), new PointF(float.MaxValue, 55));
            Assert.Equal(2, intersections.Count());
            
            DrawRegion(provider, poly, 1f);
        }

        [Theory]
        [WithBlankImages(1000, 1000, PixelTypes.Rgba32)]
        public void HandleClippingInnerCorner<TPixel>(TestImageProvider<TPixel> provider)
            where TPixel : unmanaged, IPixel<TPixel>
        {
            var simplePath = new Polygon(new LinearLineSegment(
                             new PointF(10, 10),
                             new PointF(200, 150),
                             new PointF(50, 300)));

            var hole1 = new Polygon(new LinearLineSegment(
                            new PointF(37, 85),
                            new PointF(130, 40),
                            new PointF(65, 137)));

            IPath poly = simplePath.Clip(hole1);
            
            DrawRegion(provider, poly, 1f);

            IEnumerable<PointF> intersections = poly.FindIntersections(new PointF(float.MinValue, 137), new PointF(float.MaxValue, 137));

            // returns an even number of points
            Assert.Equal(4, intersections.Count());
        }

        [Theory]
        [WithBlankImages(1000, 1000, PixelTypes.Rgba32)]
        public void CrossingCorner<TPixel>(TestImageProvider<TPixel> provider)
            where TPixel : unmanaged, IPixel<TPixel>
        {
            var simplePath = new Polygon(new LinearLineSegment(
                             new PointF(10, 10),
                             new PointF(200, 150),
                             new PointF(50, 300)));

            IEnumerable<PointF> intersections = simplePath.FindIntersections(new PointF(float.MinValue, 150), new PointF(float.MaxValue, 150));

            DrawRegion(provider, simplePath, 1f);
            // returns an even number of points
            Assert.Equal(2, intersections.Count());
        }

        [Theory]
        [WithBlankImages(1000, 1000, PixelTypes.Rgba32)]
        public void ClippingEdgefromInside<TPixel>(TestImageProvider<TPixel> provider)
            where TPixel : unmanaged, IPixel<TPixel>
        {
            IPath simplePath = new RectangularPolygon(10, 10, 100, 100).Clip(new RectangularPolygon(20, 0, 20, 20));

            DrawRegion(provider, simplePath, 1f);
            
            IEnumerable<PointF> intersections = simplePath.FindIntersections(new PointF(float.MinValue, 20), new PointF(float.MaxValue, 20));

            // returns an even number of points
            Assert.Equal(4, intersections.Count());
        }

        [Theory]
        [WithBlankImages(1000, 1000, PixelTypes.Rgba32)]
        public void ClippingEdgeFromOutside<TPixel>(TestImageProvider<TPixel> provider)
            where TPixel : unmanaged, IPixel<TPixel>
        {
            var simplePath = new Polygon(new LinearLineSegment(
                             new PointF(10, 10),
                             new PointF(100, 10),
                             new PointF(50, 300)));

            DrawRegion(provider, simplePath, 1f);
            
            IEnumerable<PointF> intersections = simplePath.FindIntersections(new PointF(float.MinValue, 10), new PointF(float.MaxValue, 10));

            // returns an even number of points
            Assert.Equal(0, intersections.Count() % 2);
        }

        [Theory]
        [WithBlankImages(1000, 1000, PixelTypes.Rgba32)]
        public void HandleClippingOutterCorner<TPixel>(TestImageProvider<TPixel> provider)
            where TPixel : unmanaged, IPixel<TPixel>
        {
            var simplePath = new Polygon(new LinearLineSegment(
                             new PointF(10, 10),
                             new PointF(200, 150),
                             new PointF(50, 300)));

            var hole1 = new Polygon(new LinearLineSegment(
                            new PointF(37, 85),
                            new PointF(130, 40),
                            new PointF(65, 137)));

            IPath poly = simplePath.Clip(hole1);

            DrawRegion(provider, poly, 1f);
            
            IEnumerable<PointF> intersections = poly.FindIntersections(new PointF(float.MinValue, 300), new PointF(float.MaxValue, 300));

            // returns an even number of points
            Assert.Equal(2, intersections.Count());
        }

        [Theory]
        [WithBlankImages(1000, 1000, PixelTypes.Rgba32)]
        public void MissingIntersection<TPixel>(TestImageProvider<TPixel> provider)
            where TPixel : unmanaged, IPixel<TPixel>
        {
            var simplePath = new Polygon(new LinearLineSegment(
                             new PointF(10, 10),
                             new PointF(200, 150),
                             new PointF(50, 300)));

            var hole1 = new Polygon(new LinearLineSegment(
                            new PointF(37, 85),
                            new PointF(130, 40),
                            new PointF(65, 137)));

            IPath poly = simplePath.Clip(hole1);
            
            DrawRegion(provider, poly, 1f);

            IEnumerable<PointF> intersections = poly.FindIntersections(new PointF(float.MinValue, 85), new PointF(float.MaxValue, 85));

            // returns an even number of points
            Assert.Equal(4, intersections.Count());
        }
    }
}