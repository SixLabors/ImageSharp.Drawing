// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Drawing;
using System.Drawing.Drawing2D;
using System.Numerics;
using BenchmarkDotNet.Attributes;
using GeoJSON.Net.Feature;
using Newtonsoft.Json;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Processing.Backends;
using SixLabors.ImageSharp.Drawing.Tests;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SkiaSharp;
using Pen = System.Drawing.Pen;
using SDPointF = System.Drawing.PointF;

namespace SixLabors.ImageSharp.Drawing.Benchmarks.Drawing;

public abstract class DrawPolygon
{
    private PointF[][] points;

    private Image<Rgba32> image;
    private Image<Rgba32> webGpuImage;

    private Bitmap sdBitmap;
    private Graphics sdGraphics;
    private GraphicsPath sdPath;
    private Pen sdPen;

    private SKPath skPath;
    private SKSurface skSurface;
    private SKPaint skPaint;

    private SolidPen isPen;

    private IPath imageSharpPath;

    private IPath strokedImageSharpPath;
    private WebGPUDrawingBackend webGpuBackend;
    private Configuration webGpuConfiguration;

    protected abstract int Width { get; }

    protected abstract int Height { get; }

    protected abstract float Thickness { get; }

    protected virtual PointF[][] GetPoints(FeatureCollection features) =>
        features.Features.SelectMany(f => PolygonFactory.GetGeoJsonPoints(f, Matrix3x2.CreateScale(60, 60))).ToArray();

    [GlobalSetup]
    public void Setup()
    {
        // Tiled rasterization benefits from a warmed worker pool. Doing this once in setup
        // reduces first-iteration noise without affecting per-method correctness.
        ThreadPool.GetMinThreads(out int minWorkerThreads, out int minCompletionPortThreads);
        int desiredWorkerThreads = Math.Max(minWorkerThreads, Environment.ProcessorCount);
        ThreadPool.SetMinThreads(desiredWorkerThreads, minCompletionPortThreads);
        Parallel.For(0, desiredWorkerThreads, static _ => { });

        string jsonContent = File.ReadAllText(TestFile.GetInputFileFullPath(TestImages.GeoJson.States));
        FeatureCollection featureCollection = JsonConvert.DeserializeObject<FeatureCollection>(jsonContent);

        this.points = this.GetPoints(featureCollection);

        // Prebuild a single multi-subpath geometry for each library so the benchmark focuses on stroking/rasterization.
        this.sdPath = new GraphicsPath(FillMode.Winding);
        this.skPath = new SKPath();
        PathBuilder pb = new();

        foreach (PointF[] loop in this.points)
        {
            if (loop.Length < 3)
            {
                continue;
            }

            // System.Drawing: one GraphicsPath with multiple closed figures.
            SDPointF firstSd = new(loop[0].X, loop[0].Y);
            SDPointF[] sdPoly = new SDPointF[loop.Length];
            for (int i = 0; i < loop.Length; i++)
            {
                sdPoly[i] = new SDPointF(loop[i].X, loop[i].Y);
            }

            this.sdPath.StartFigure();
            this.sdPath.AddPolygon(sdPoly);
            this.sdPath.CloseFigure();

            // Skia: one SKPath with multiple closed contours.
            this.skPath.MoveTo(loop[0].X, loop[0].Y);
            for (int i = 1; i < loop.Length; i++)
            {
                this.skPath.LineTo(loop[i].X, loop[i].Y);
            }

            this.skPath.Close();

            // ImageSharp: one IPath with multiple closed figures.
            pb.StartFigure();
            pb.AddLines(loop);
            pb.CloseFigure();
        }

        this.imageSharpPath = pb.Build();

        this.image = new Image<Rgba32>(this.Width, this.Height);
        this.isPen = new SolidPen(Color.White, this.Thickness);
        this.strokedImageSharpPath = this.isPen.GeneratePath(this.imageSharpPath);
        this.webGpuBackend = new WebGPUDrawingBackend();
        this.webGpuConfiguration = Configuration.Default.Clone();
        this.webGpuConfiguration.SetDrawingBackend(this.webGpuBackend);
        this.webGpuImage = new Image<Rgba32>(this.webGpuConfiguration, this.Width, this.Height);

        this.sdBitmap = new Bitmap(this.Width, this.Height);
        this.sdGraphics = Graphics.FromImage(this.sdBitmap);
        this.sdGraphics.InterpolationMode = InterpolationMode.Default;
        this.sdGraphics.SmoothingMode = SmoothingMode.AntiAlias;
        this.sdGraphics.PixelOffsetMode = PixelOffsetMode.Default;
        this.sdGraphics.CompositingMode = CompositingMode.SourceOver;

        this.sdPen = new Pen(System.Drawing.Color.White, this.Thickness);

        this.skSurface = SKSurface.Create(new SKImageInfo(this.Width, this.Height));
        this.skPaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = SKColors.White,
            StrokeWidth = this.Thickness,
            IsAntialias = true,
        };
    }

    [IterationSetup]
    public void IterationSetup()
    {
        // Clear all targets to avoid overdraw effects influencing results.
        this.sdGraphics.Clear(System.Drawing.Color.Transparent);
        this.skSurface.Canvas.Clear(SKColors.Transparent);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        this.sdPen.Dispose();
        this.sdPath.Dispose();
        this.sdGraphics.Dispose();
        this.sdBitmap.Dispose();

        this.skPaint.Dispose();
        this.skSurface.Dispose();
        this.skPath.Dispose();

        this.image.Dispose();
        this.webGpuImage.Dispose();
        this.webGpuBackend.Dispose();
    }

    [Benchmark]
    public void SystemDrawing()
        => this.sdGraphics.DrawPath(this.sdPen, this.sdPath);

    [Benchmark]
    public void ImageSharpCombinedPaths()
        => this.image.Mutate(c => c.ProcessWithCanvas(canvas => canvas.Draw(this.isPen, this.imageSharpPath)));

    [Benchmark(Description = "ImageSharp Combined Paths WebGPU Backend")]
    public void ImageSharpCombinedPathsWebGPUBackend()
        => this.webGpuImage.Mutate(c => c.ProcessWithCanvas(canvas => canvas.Draw(this.isPen, this.imageSharpPath)));

    [Benchmark]
    public void ImageSharpSeparatePaths()
        => this.image.Mutate(
            c => c.ProcessWithCanvas(canvas =>
                {
                    foreach (PointF[] loop in this.points)
                    {
                        canvas.Draw(Processing.Pens.Solid(Color.White, this.Thickness), new Polygon(loop));
                    }
                }));

    [Benchmark(Baseline = true)]
    public void SkiaSharp()
        => this.skSurface.Canvas.DrawPath(this.skPath, this.skPaint);

    [Benchmark]
    public IPath ImageSharpStrokeAndClip() => this.isPen.GeneratePath(this.imageSharpPath);

    [Benchmark]
    public void FillPolygon()
        => this.image.Mutate(c => c.ProcessWithCanvas(canvas => canvas.Fill(this.strokedImageSharpPath, Processing.Brushes.Solid(Color.White))));

    [Benchmark]
    public void FillPolygonWebGPUBackend()
        => this.webGpuImage.Mutate(c => c.ProcessWithCanvas(canvas => canvas.Fill(this.strokedImageSharpPath, Processing.Brushes.Solid(Color.White))));
}

public class DrawPolygonAll : DrawPolygon
{
    protected override int Width => 7200;

    protected override int Height => 4800;

    protected override float Thickness => 2F;
}

public class DrawPolygonMediumThin : DrawPolygon
{
    protected override int Width => 1000;

    protected override int Height => 1000;

    protected override float Thickness => 1F;

    protected override PointF[][] GetPoints(FeatureCollection features)
    {
        Feature state = features.Features.Single(f => (string)f.Properties["NAME"] == "Mississippi");

        Matrix3x2 transform = Matrix3x2.CreateTranslation(-87, -54)
                              * Matrix3x2.CreateScale(60, 60);
        return [.. PolygonFactory.GetGeoJsonPoints(state, transform)];
    }
}

public class DrawPolygonMediumThick : DrawPolygonMediumThin
{
    protected override float Thickness => 10F;
}
