// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Drawing;
using System.Drawing.Drawing2D;
using System.Numerics;
using System.Runtime.InteropServices;
using BenchmarkDotNet.Attributes;
using GeoJSON.Net.Feature;
using Newtonsoft.Json;
using SharpBlaze;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Shapes.PolygonClipper;
using SixLabors.ImageSharp.Drawing.Tests;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SkiaSharp;
using BlazeMatrix = SharpBlaze.Matrix;
using SDPointF = System.Drawing.PointF;

namespace SixLabors.ImageSharp.Drawing.Benchmarks.Drawing;

public abstract class DrawPolygon
{
    private PointF[][] points;

    private Image<Rgba32> image;
    private bool isImageSharp;

    private SDPointF[][] sdPoints;
    private Bitmap sdBitmap;
    private Graphics sdGraphics;
    private bool isSystem;

    private SKPath skPath;
    private SKSurface skSurface;
    private bool isSkia;

    private Executor executor;
    private DestinationImage<TileDescriptor_8x16> vecDst;
    private bool isBlaze;

    protected abstract int Width { get; }

    protected abstract int Height { get; }

    protected abstract float Thickness { get; }

    protected virtual PointF[][] GetPoints(FeatureCollection features) =>
        features.Features.SelectMany(f => PolygonFactory.GetGeoJsonPoints(f, Matrix3x2.CreateScale(60, 60))).ToArray();

    [GlobalSetup]
    public void Setup()
    {
        string jsonContent = File.ReadAllText(TestFile.GetInputFileFullPath(TestImages.GeoJson.States));

        FeatureCollection featureCollection = JsonConvert.DeserializeObject<FeatureCollection>(jsonContent);

        this.points = this.GetPoints(featureCollection);
        this.sdPoints = this.points.Select(pts => pts.Select(p => new SDPointF(p.X, p.Y)).ToArray()).ToArray();

        this.skPath = new SKPath();

        foreach (PointF[] ptArr in this.points.Where(pts => pts.Length > 2))
        {
            this.skPath.MoveTo(ptArr[0].X, ptArr[1].Y);

            for (int i = 1; i < ptArr.Length; i++)
            {
                this.skPath.LineTo(ptArr[i].X, ptArr[i].Y);
            }

            this.skPath.LineTo(ptArr[0].X, ptArr[1].Y);
        }

        this.executor = new SerialExecutor();
        this.vecDst = new DestinationImage<TileDescriptor_8x16>();
        this.vecDst.UpdateSize(new IntSize(this.Width, this.Height));
        this.vecDst.ClearImage();

        this.image = new Image<Rgba32>(this.Width, this.Height);
        this.sdBitmap = new Bitmap(this.Width, this.Height);
        this.sdGraphics = Graphics.FromImage(this.sdBitmap);
        this.sdGraphics.InterpolationMode = InterpolationMode.Default;
        this.sdGraphics.SmoothingMode = SmoothingMode.AntiAlias;
        this.skSurface = SKSurface.Create(new SKImageInfo(this.Width, this.Height));
    }

    [GlobalCleanup]
    public unsafe void Cleanup()
    {
        string dir = "Images/DrawPolygon";
        Directory.CreateDirectory(dir);

        if (this.isImageSharp)
        {
            this.image.SaveAsPng(System.IO.Path.Combine(dir, "ImageSharp.png"));
            this.isImageSharp = false;
        }

        if (this.isBlaze)
        {
            using var blazeImage = Image.WrapMemory<Rgba32>(
            this.vecDst.GetImageData(),
            this.vecDst.GetBytesPerRow() * this.vecDst.GetImageHeight(),
            this.vecDst.GetImageWidth(),
            this.vecDst.GetImageHeight());

            blazeImage.SaveAsPng(System.IO.Path.Combine(dir, "Blaze.png"));
            this.isBlaze = false;
        }

        if (this.isSystem)
        {
            this.sdBitmap.Save(System.IO.Path.Combine(dir, "SystemDrawing.png"));
            this.isSystem = false;
        }

        if (this.isSkia)
        {
            using var skSnapshot = this.skSurface.Snapshot();
            using var skEncoded = skSnapshot.Encode();
            using var skFile = new FileStream(System.IO.Path.Combine(dir, "SkiaSharp.png"), FileMode.Create);
            skEncoded.SaveTo(skFile);
            this.isSkia = false;
        }

        this.image.Dispose();
        this.sdGraphics.Dispose();
        this.sdBitmap.Dispose();
        this.skSurface.Dispose();
        this.skPath.Dispose();
    }

    [Benchmark]
    public void SystemDrawing()
    {
        using var pen = new System.Drawing.Pen(System.Drawing.Color.White, this.Thickness);

        foreach (SDPointF[] loop in this.sdPoints)
        {
            this.sdGraphics.DrawPolygon(pen, loop);
        }

        this.isSystem = true;
    }

    [Benchmark]
    public void ImageSharp()
    {
        this.image.Mutate(
            c =>
            {
                foreach (PointF[] loop in this.points)
                {
                    c.DrawPolygon(Color.White, this.Thickness, loop);
                }
            });
        this.isImageSharp = true;
    }

    [Benchmark(Baseline = true)]
    public void SkiaSharp()
    {
        using var paint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = SKColors.White,
            StrokeWidth = this.Thickness,
            IsAntialias = true,
        };

        this.skSurface.Canvas.DrawPath(this.skPath, paint);
        this.isSkia = true;
    }

    [Benchmark]
    public void Blaze()
    {
        VectorImageBuilder builder = new();

        foreach (PointF[] loop in this.points)
        {
            var loopPolygon = new Polygon(loop);
            var brush = new Processing.SolidBrush(Color.White);
            var pen = new SolidPen(brush, this.Thickness);
            List<List<PointF>> outline = GenerateOutlineList(loopPolygon, pen.StrokeWidth, pen.JointStyle, pen.EndCapStyle);

            foreach (List<PointF> line in outline)
            {
                Span<PointF> ptArr = CollectionsMarshal.AsSpan(line);

                builder.MoveTo(new FloatPoint(ptArr[0].X, ptArr[1].Y));
                for (int i = 1; i < ptArr.Length; i++)
                {
                    builder.LineTo(new FloatPoint(ptArr[i].X, ptArr[i].Y));
                }

                builder.LineTo(new FloatPoint(ptArr[0].X, ptArr[1].Y));

                builder.Close();
            }
        }

        VectorImage image = builder.ToVectorImage(Color.White.ToPixel<Rgba32>().PackedValue);

        this.vecDst.DrawImage(image, BlazeMatrix.Identity, this.executor);

        this.isBlaze = true;
    }

    private static List<List<PointF>> GenerateOutlineList(IPath path, float width, JointStyle jointStyle, EndCapStyle endCapStyle)
    {
        List<List<PointF>> strokedLines = [];

        if (width <= 0)
        {
            return strokedLines;
        }

        PolygonStroker stroker = new()
        {
            Width = width,
            LineJoin = GetLineJoin(jointStyle),
            LineCap = GetLineCap(endCapStyle)
        };
        foreach (ISimplePath simplePath in path.Flatten())
        {
            stroker.Reset();

            int pointCount = 0;
            if (simplePath is Path concretePath)
            {
                foreach (ILineSegment line in concretePath.LineSegments)
                {
                    ReadOnlySpan<PointF> points = line.Flatten().Span;
                    stroker.AddLinePath(points);
                    pointCount += points.Length;
                }
            }
            else
            {
                ReadOnlySpan<PointF> points = simplePath.Points.Span;
                stroker.AddLinePath(points);
                pointCount = points.Length;
            }

            bool isClosed = simplePath.IsClosed || endCapStyle is EndCapStyle.Polygon or EndCapStyle.Joined;
            if (isClosed)
            {
                stroker.ClosePath();
            }

            List<PointF> lineBuilder = new(pointCount * 4);
            stroker.FinishPath(lineBuilder);
            strokedLines.Add(lineBuilder);
        }

        return strokedLines;

        static LineJoin GetLineJoin(JointStyle value) => value switch
        {
            JointStyle.Square => LineJoin.BevelJoin,
            JointStyle.Round => LineJoin.RoundJoin,
            _ => LineJoin.MiterJoin,
        };

        static LineCap GetLineCap(EndCapStyle value) => value switch
        {
            EndCapStyle.Round => LineCap.Round,
            EndCapStyle.Square => LineCap.Square,
            _ => LineCap.Butt,
        };
    }
}

public class DrawPolygonAll : DrawPolygon
{
    protected override int Width => 7200;

    protected override int Height => 4800;

    protected override float Thickness => 2f;
}

public class DrawPolygonMediumThin : DrawPolygon
{
    protected override int Width => 1000;

    protected override int Height => 1000;

    protected override float Thickness => 1f;

    protected override PointF[][] GetPoints(FeatureCollection features)
    {
        Feature state = features.Features.Single(f => (string)f.Properties["NAME"] == "Mississippi");

        Matrix3x2 transform = Matrix3x2.CreateTranslation(-87, -54)
                              * Matrix3x2.CreateScale(60, 60);
        return PolygonFactory.GetGeoJsonPoints(state, transform).ToArray();
    }
}

public class DrawPolygonMediumThick : DrawPolygonMediumThin
{
    protected override float Thickness => 10f;
}
