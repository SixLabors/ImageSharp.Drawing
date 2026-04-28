// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Processing.Backends;
using SixLabors.ImageSharp.Drawing.Tests.TestUtilities;
using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace SixLabors.ImageSharp.Drawing.Tests.Processing;

public partial class DrawingCanvasTests
{
    [Theory]
    [WithBlankImage(220, 160, PixelTypes.Rgba32)]
    public void Process_PathBuilder_MatchesReference<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using Image<TPixel> target = provider.GetImage();

        PathBuilder blurBuilder = new();
        blurBuilder.AddArc(new PointF(55, 40), 55, 40, 0, 0, 360);
        blurBuilder.CloseAllFigures();

        PathBuilder pixelateBuilder = new();
        pixelateBuilder.AddLine(110, 80, 220, 80);
        pixelateBuilder.AddLine(220, 80, 165, 160);
        pixelateBuilder.AddLine(165, 160, 110, 80);
        pixelateBuilder.CloseAllFigures();

        using (DrawingCanvas<TPixel> canvas = CreateCanvas(provider, target, new DrawingOptions()))
        {
            DrawProcessScenario(canvas);
            canvas.Apply(blurBuilder, ctx => ctx.GaussianBlur(6F));
            canvas.Apply(pixelateBuilder, ctx => ctx.Pixelate(10));
            canvas.Flush();
        }

        target.DebugSave(provider, appendSourceFileOrDescription: false);
        target.CompareToReferenceOutput(provider, appendSourceFileOrDescription: false);
    }

    [Theory]
    [WithBlankImage(220, 160, PixelTypes.Rgba32)]
    public void Process_Path_MatchesReference<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using Image<TPixel> target = provider.GetImage();
        IPath blurPath = CreateBlurEllipsePath();
        IPath pixelatePath = CreatePixelateTrianglePath();

        using (DrawingCanvas<TPixel> canvas = CreateCanvas(provider, target, new DrawingOptions()))
        {
            DrawProcessScenario(canvas);
            canvas.Apply(blurPath, ctx => ctx.GaussianBlur(6F));
            canvas.Apply(pixelatePath, ctx => ctx.Pixelate(10));
            canvas.Flush();
        }

        target.DebugSave(provider, appendSourceFileOrDescription: false);
        target.CompareToReferenceOutput(provider, appendSourceFileOrDescription: false);
    }

    [Theory]
    [WithBlankImage(220, 160, PixelTypes.Rgba32)]
    public void Process_NoCpuFrame_UsesBackendReadback_MatchesReference<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using Image<TPixel> target = provider.GetImage();
        IPath blurPath = CreateBlurEllipsePath();
        IPath pixelatePath = CreatePixelateTrianglePath();

        MemoryCanvasFrame<TPixel> proxyFrame = new(new Buffer2DRegion<TPixel>(target.Frames.RootFrame.PixelBuffer));
        MirroringCpuReadbackTestBackend<TPixel> mirroringBackend = new(proxyFrame, target);

        NativeSurface nativeSurface = new UnsupportedNativeSurface();
        Configuration configuration = provider.Configuration.Clone();
        configuration.SetDrawingBackend(mirroringBackend);

        using (DrawingCanvas<TPixel> canvas = new(
                   configuration,
                   new DrawingOptions(),
                   new NativeCanvasFrame<TPixel>(target.Bounds, nativeSurface)))
        {
            DrawProcessScenario(canvas);
            canvas.Apply(blurPath, ctx => ctx.GaussianBlur(6F));
            canvas.Apply(pixelatePath, ctx => ctx.Pixelate(10));
            canvas.Flush();
        }

        Assert.True(mirroringBackend.ReadbackCallCount > 0);
        Assert.Same(configuration, mirroringBackend.LastReadbackConfiguration);
        target.DebugSave(provider, appendSourceFileOrDescription: false);
        target.CompareToReferenceOutput(provider, appendSourceFileOrDescription: false);
    }

    [Fact]
    public void Process_UsesCanvasConfigurationForOperationContext()
    {
        Configuration configuration = Configuration.Default.Clone();
        using Image<Rgba32> target = new(configuration, 48, 36);
        bool callbackInvoked = false;
        bool sameConfiguration = false;

        target.Mutate(c => c.Paint(canvas =>
        {
            canvas.Fill(Brushes.Solid(Color.CornflowerBlue));
            canvas.Apply(new Rectangle(8, 6, 28, 20), ctx =>
            {
                callbackInvoked = true;
                sameConfiguration = ReferenceEquals(configuration, ctx.Configuration);
                ctx.GaussianBlur(2F);
            });
        }));

        Assert.True(callbackInvoked);
        Assert.True(sameConfiguration);
    }

    private static void DrawProcessScenario(DrawingCanvas canvas)
    {
        canvas.Clear(Brushes.Solid(Color.White));

        canvas.Draw(Pens.Solid(Color.DimGray, 3), new Rectangle(10, 10, 220, 140));
        canvas.DrawEllipse(Pens.Solid(Color.CornflowerBlue, 6), new PointF(120, 80), new SizeF(110, 70));
        canvas.DrawArc(
            Pens.Solid(Color.ForestGreen, 4),
            new PointF(120, 80),
            new SizeF(90, 46),
            rotation: 15,
            startAngle: -25,
            sweepAngle: 220);
        canvas.DrawLine(
            Pens.Solid(Color.OrangeRed, 5),
            new PointF(18, 140),
            new PointF(76, 28),
            new PointF(166, 126),
            new PointF(222, 20));
        canvas.DrawBezier(
            Pens.Solid(Color.MediumVioletRed, 4),
            new PointF(20, 80),
            new PointF(70, 18),
            new PointF(168, 144),
            new PointF(220, 78));
    }

    private static EllipsePolygon CreateBlurEllipsePath()
        => new(new PointF(55, 40), new SizeF(110, 80));

    private static IPath CreatePixelateTrianglePath()
    {
        PathBuilder pathBuilder = new();
        pathBuilder.AddLine(110, 80, 220, 80);
        pathBuilder.AddLine(220, 80, 165, 160);
        pathBuilder.AddLine(165, 160, 110, 80);
        pathBuilder.CloseAllFigures();
        return pathBuilder.Build();
    }

    private sealed class UnsupportedNativeSurface : NativeSurface
    {
        public override TNativeTarget GetNativeTarget<TNativeTarget>()
            => throw new NotSupportedException();
    }

    /// <summary>
    /// Test backend that mirrors composition output into a CPU frame and optionally serves readback
    /// from a backing image for process-path tests.
    /// </summary>
    private sealed class MirroringCpuReadbackTestBackend<TPixel> : IDrawingBackend
        where TPixel : unmanaged, IPixel<TPixel>
    {
        private readonly ICanvasFrame<TPixel> proxyFrame;
        private readonly Image<TPixel>? readbackSource;

        public MirroringCpuReadbackTestBackend(ICanvasFrame<TPixel> proxyFrame, Image<TPixel>? readbackSource = null)
        {
            this.proxyFrame = proxyFrame;
            this.readbackSource = readbackSource;
        }

        public int ReadbackCallCount { get; private set; }

        public Configuration? LastReadbackConfiguration { get; private set; }

        public void FlushCompositions<TTargetPixel>(
            Configuration configuration,
            ICanvasFrame<TTargetPixel> target,
            CompositionScene compositionScene)
            where TTargetPixel : unmanaged, IPixel<TTargetPixel>
        {
            if (this.proxyFrame is not ICanvasFrame<TTargetPixel> typedProxyFrame)
            {
                throw new NotSupportedException("Mirroring test backend pixel format mismatch.");
            }

            DefaultDrawingBackend.Instance.FlushCompositions(configuration, typedProxyFrame, compositionScene);
        }

        public void ReadRegion<TTargetPixel>(
            Configuration configuration,
            ICanvasFrame<TTargetPixel> target,
            Rectangle sourceRectangle,
            Buffer2DRegion<TTargetPixel> destination)
            where TTargetPixel : unmanaged, IPixel<TTargetPixel>
        {
            this.LastReadbackConfiguration = configuration;

            if (this.readbackSource is null)
            {
                throw new NotSupportedException();
            }

            this.ReadbackCallCount++;

            Rectangle clipped = Rectangle.Intersect(this.readbackSource.Bounds, sourceRectangle);
            if (clipped.Width <= 0 || clipped.Height <= 0)
            {
                throw new ArgumentException("The requested readback rectangle does not intersect the target bounds.", nameof(sourceRectangle));
            }

            using Image<TPixel> cropped = this.readbackSource.Clone(ctx => ctx.Crop(clipped));
            using Image<TTargetPixel> converted = cropped.CloneAs<TTargetPixel>();
            Buffer2D<TTargetPixel> source = converted.Frames.RootFrame.PixelBuffer;
            int copyWidth = Math.Min(source.Width, destination.Width);
            int copyHeight = Math.Min(source.Height, destination.Height);

            for (int y = 0; y < copyHeight; y++)
            {
                source.DangerousGetRowSpan(y).Slice(0, copyWidth).CopyTo(destination.DangerousGetRowSpan(y));
            }
        }
    }
}
