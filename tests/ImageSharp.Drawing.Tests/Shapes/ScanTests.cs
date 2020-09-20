using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Xunit;
using IOPath = System.IO.Path;

namespace SixLabors.ImageSharp.Drawing.Tests
{
    public class ScanTests
    {
        private const float Inf = 1000;

        private readonly IBrush TestBrush = Brushes.Solid(Color.Red);
        
        private void DebugDraw(IPath path, float scale = 100f, [CallerMemberName]string testMethod = "")
        {
            path = path.Transform(Matrix3x2.CreateScale(scale) * Matrix3x2.CreateTranslation(10, 10));

            Rectangle bounds = (Rectangle)path.Bounds;

            using Image img = new Image<Rgba32>(bounds.Width + 10, bounds.Height+10);
            img.Mutate(ctx => ctx.Fill(TestBrush, path));
            
            string outDir = TestEnvironment.CreateOutputDirectory(nameof(ScanTests));
            string outFile = IOPath.Combine(outDir, testMethod+".png");
            img.SaveAsPng(outFile);
        }
        
        private static Polygon  MakePolygon(params (float x, float y)[] coords)
        {
            PointF[] points = coords.Select(c => new PointF(c.x, c.y)).ToArray();
            return new Polygon(new LinearLineSegment(points));
        }
        
        private static (PointF Start, PointF End) MakeHLine(float y)
        {

            return (new PointF(-Inf, y), new PointF(Inf, y));
        }
        
        [Fact]
        public void Case01()
        {
            IPath path = MakePolygon((0,0), (10,10), (20,0), (20,20), (0,20) );
            
            DebugDraw(path);
        }
    }
}