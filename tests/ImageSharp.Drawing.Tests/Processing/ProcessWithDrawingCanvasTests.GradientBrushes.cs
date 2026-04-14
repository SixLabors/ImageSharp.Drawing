// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Globalization;
using System.Numerics;
using System.Text;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Tests.TestUtilities.ImageComparison;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace SixLabors.ImageSharp.Drawing.Tests.Processing;

public partial class ProcessWithDrawingCanvasTests
{
    private static readonly ImageComparer EllipticGradientTolerantComparer = ImageComparer.TolerantPercentage(0.01F);
    private static readonly ImageComparer LinearGradientTolerantComparer = ImageComparer.TolerantPercentage(0.01F);
    private static readonly ImageComparer RadialGradientTolerantComparer = ImageComparer.TolerantPercentage(0.01F);
    private static readonly ImageComparer SweepGradientTolerantComparer = ImageComparer.TolerantPercentage(0.01F);

    [Theory]
    [WithBlankImage(200, 200, PixelTypes.Rgba32, 0F, 360F)]
    [WithBlankImage(200, 200, PixelTypes.Rgba32, 90F, 450F)]
    [WithBlankImage(200, 200, PixelTypes.Rgba32, 180F, 540F)]
    [WithBlankImage(200, 200, PixelTypes.Rgba32, 270F, 630F)]
    public void FillSweepGradientBrush_RendersFullSweep_Every90Degrees<TPixel>(
        TestImageProvider<TPixel> provider,
        float start,
        float end)
        where TPixel : unmanaged, IPixel<TPixel>
        => provider.VerifyOperation(
            SweepGradientTolerantComparer,
            image =>
            {
                SweepGradientBrush brush = new(
                    new Point(100, 100),
                    start,
                    end,
                    GradientRepetitionMode.None,
                    new ColorStop(0, Color.Red),
                    new ColorStop(0.25F, Color.Yellow),
                    new ColorStop(0.5F, Color.Green),
                    new ColorStop(0.75F, Color.Blue),
                    new ColorStop(1, Color.Red));

                image.Mutate(ctx => ctx.Paint(canvas => canvas.Fill(brush)));
            },
            $"start({start},end{end})",
            false,
            false);

    [Theory]
    [WithBlankImage(200, 200, PixelTypes.Rgba32)]
    public void FillRadialGradientBrushWithEqualColorsReturnsUnicolorImage<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using Image<TPixel> image = provider.GetImage();
        Color red = Color.Red;

        RadialGradientBrush brush =
            new(
                new Point(0, 0),
                100,
                GradientRepetitionMode.None,
                new ColorStop(0, red),
                new ColorStop(1, red));

        image.Mutate(ctx => ctx.Paint(canvas => canvas.Fill(brush)));
        image.DebugSave(provider, appendPixelTypeToFileName: false, appendSourceFileOrDescription: false);

        // No reference image needed: the whole output should be a single color.
        image.ComparePixelBufferTo(red);
    }

    [Theory]
    [WithBlankImage(200, 200, PixelTypes.Rgba32, 100, 100)]
    [WithBlankImage(200, 200, PixelTypes.Rgba32, 0, 0)]
    [WithBlankImage(200, 200, PixelTypes.Rgba32, 100, 0)]
    [WithBlankImage(200, 200, PixelTypes.Rgba32, 0, 100)]
    [WithBlankImage(200, 200, PixelTypes.Rgba32, -40, 100)]
    public void FillRadialGradientBrushWithDifferentCentersReturnsImage<TPixel>(
        TestImageProvider<TPixel> provider,
        int centerX,
        int centerY)
        where TPixel : unmanaged, IPixel<TPixel>
        => provider.VerifyOperation(
            RadialGradientTolerantComparer,
            image =>
            {
                RadialGradientBrush brush = new(
                    new Point(centerX, centerY),
                    image.Width / 2F,
                    GradientRepetitionMode.None,
                    new ColorStop(0, Color.Red),
                    new ColorStop(1, Color.Yellow));

                image.Mutate(ctx => ctx.Paint(canvas => canvas.Fill(brush)));
            },
            $"center({centerX},{centerY})",
            false,
            false);

    [Fact]
    public void SweepGradientBrush_Transform_TranslationMovesCenter()
    {
        SweepGradientBrush brush = new(
            new PointF(100, 100),
            0F,
            360F,
            GradientRepetitionMode.None,
            new ColorStop(0, Color.Red),
            new ColorStop(1, Color.Blue));

        Matrix4x4 matrix = Matrix4x4.CreateTranslation(50F, 30F, 0F);

        SweepGradientBrush transformed = Assert.IsType<SweepGradientBrush>(brush.Transform(matrix));

        Assert.Equal(150F, transformed.Center.X, 0.01F);
        Assert.Equal(130F, transformed.Center.Y, 0.01F);
    }

    [Fact]
    public void SweepGradientBrush_Transform_RotationRotatesAngles()
    {
        SweepGradientBrush brush = new(
            new PointF(100, 100),
            0F,
            90F,
            GradientRepetitionMode.None,
            new ColorStop(0, Color.Red),
            new ColorStop(1, Color.Blue));

        // Rotate 90 degrees counter-clockwise in design grid (y-up).
        // In screen space (y-down), Matrix4x4.CreateRotationZ(pi/2) rotates clockwise,
        // which corresponds to counter-clockwise on the design grid.
        Matrix4x4 matrix = Matrix4x4.CreateRotationZ(MathF.PI / 2F);

        SweepGradientBrush transformed = Assert.IsType<SweepGradientBrush>(brush.Transform(matrix));

        // The 90-degree sweep should be preserved.
        float sweep = transformed.EndAngleDegrees - transformed.StartAngleDegrees;
        Assert.Equal(90F, sweep, 0.5F);
    }

    [Fact]
    public void SweepGradientBrush_Transform_ReflectionFlipsSweepDirection()
    {
        SweepGradientBrush brush = new(
            new PointF(100, 100),
            0F,
            90F,
            GradientRepetitionMode.None,
            new ColorStop(0, Color.Red),
            new ColorStop(1, Color.Blue));

        // Reflect across Y axis (negative determinant).
        Matrix4x4 matrix = Matrix4x4.CreateScale(-1F, 1F, 1F);

        SweepGradientBrush transformed = Assert.IsType<SweepGradientBrush>(brush.Transform(matrix));

        // Reflection should flip the sweep direction: positive 90 becomes negative 90.
        float sweep = transformed.EndAngleDegrees - transformed.StartAngleDegrees;
        Assert.Equal(-90F, sweep, 0.5F);
    }

    [Fact]
    public void SweepGradientBrush_Transform_FullSweepPreserved()
    {
        // Equal start/end = full 360 sweep.
        SweepGradientBrush brush = new(
            new PointF(50, 50),
            45F,
            45F,
            GradientRepetitionMode.None,
            new ColorStop(0, Color.Red),
            new ColorStop(1, Color.Blue));

        Matrix4x4 matrix =
            Matrix4x4.CreateScale(2F)
            * Matrix4x4.CreateTranslation(10F, 20F, 0F);

        SweepGradientBrush transformed = Assert.IsType<SweepGradientBrush>(brush.Transform(matrix));

        // Full sweep should remain a full 360 degrees.
        float sweep = MathF.Abs(transformed.EndAngleDegrees - transformed.StartAngleDegrees);
        Assert.Equal(360F, sweep, 0.5F);
    }

    [Fact]
    public void RadialGradientBrush_Transform_UsesAverageScaleForRadii()
    {
        RadialGradientBrush brush = new(
            new PointF(10, 20),
            4F,
            new PointF(30, 40),
            8F,
            GradientRepetitionMode.None,
            new ColorStop(0, Color.Red),
            new ColorStop(1, Color.Blue));

        Matrix4x4 matrix =
            Matrix4x4.CreateScale(2F, 4F, 1F)
            * Matrix4x4.CreateTranslation(5F, 7F, 0F);

        RadialGradientBrush transformed = Assert.IsType<RadialGradientBrush>(brush.Transform(matrix));

        Assert.Equal(PointF.Transform(brush.Center0, matrix), transformed.Center0);
        Assert.Equal(PointF.Transform(brush.Center1.Value, matrix), transformed.Center1.Value);
        Assert.Equal(12F, transformed.Radius0, 5);
        Assert.Equal(24F, transformed.Radius1.Value, 5);
    }

    [Theory]
    [WithBlankImage(10, 10, PixelTypes.Rgba32)]
    public void FillEllipticGradientBrushWithEqualColorsReturnsUnicolorImage<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        Color red = Color.Red;

        using Image<TPixel> image = provider.GetImage();

        EllipticGradientBrush unicolorEllipticGradientBrush =
            new(
                new Point(0, 0),
                new Point(10, 0),
                1.0F,
                GradientRepetitionMode.None,
                new ColorStop(0, red),
                new ColorStop(1, red));

        DrawingOptions options = new();
        image.Mutate(ctx => ctx.Paint(options, canvas => canvas.Fill(unicolorEllipticGradientBrush)));
        image.DebugSave(provider, appendPixelTypeToFileName: false, appendSourceFileOrDescription: false);

        // No reference image needed: the whole output should be a single color.
        image.ComparePixelBufferTo(red);
    }

    [Theory]
    [WithBlankImage(200, 200, PixelTypes.Rgba32, 0.1)]
    [WithBlankImage(200, 200, PixelTypes.Rgba32, 0.4)]
    [WithBlankImage(200, 200, PixelTypes.Rgba32, 0.8)]
    [WithBlankImage(200, 200, PixelTypes.Rgba32, 1.0)]
    [WithBlankImage(200, 200, PixelTypes.Rgba32, 1.2)]
    [WithBlankImage(200, 200, PixelTypes.Rgba32, 1.6)]
    [WithBlankImage(200, 200, PixelTypes.Rgba32, 2.0)]
    public void FillEllipticGradientBrushAxisParallelEllipsesWithDifferentRatio<TPixel>(TestImageProvider<TPixel> provider, float ratio)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using Image<TPixel> image = provider.GetImage();

        Color yellow = Color.Yellow;
        Color red = Color.Red;
        Color black = Color.Black;

        EllipticGradientBrush brush = new(
            new Point(image.Width / 2, image.Height / 2),
            new Point(image.Width / 2, image.Width * 2 / 3),
            ratio,
            GradientRepetitionMode.None,
            new ColorStop(0, yellow),
            new ColorStop(1, red),
            new ColorStop(1, black));

        FormattableString outputDetails = $"{ratio:F2}";
        DrawingOptions options = new();
        image.Mutate(ctx => ctx.Paint(options, canvas => canvas.Fill(brush)));
        image.DebugSave(provider, outputDetails, appendPixelTypeToFileName: false, appendSourceFileOrDescription: false);
        image.CompareToReferenceOutput(
            EllipticGradientTolerantComparer,
            provider,
            outputDetails,
            appendPixelTypeToFileName: false,
            appendSourceFileOrDescription: false);
    }

    [Theory]
    [WithBlankImage(200, 200, PixelTypes.Rgba32, 0.1, 0)]
    [WithBlankImage(200, 200, PixelTypes.Rgba32, 0.4, 0)]
    [WithBlankImage(200, 200, PixelTypes.Rgba32, 0.8, 0)]
    [WithBlankImage(200, 200, PixelTypes.Rgba32, 1.0, 0)]
    [WithBlankImage(200, 200, PixelTypes.Rgba32, 0.1, 45)]
    [WithBlankImage(200, 200, PixelTypes.Rgba32, 0.4, 45)]
    [WithBlankImage(200, 200, PixelTypes.Rgba32, 0.8, 45)]
    [WithBlankImage(200, 200, PixelTypes.Rgba32, 1.0, 45)]
    [WithBlankImage(200, 200, PixelTypes.Rgba32, 0.1, 90)]
    [WithBlankImage(200, 200, PixelTypes.Rgba32, 0.4, 90)]
    [WithBlankImage(200, 200, PixelTypes.Rgba32, 0.8, 90)]
    [WithBlankImage(200, 200, PixelTypes.Rgba32, 1.0, 90)]
    [WithBlankImage(200, 200, PixelTypes.Rgba32, 0.1, 30)]
    [WithBlankImage(200, 200, PixelTypes.Rgba32, 0.4, 30)]
    [WithBlankImage(200, 200, PixelTypes.Rgba32, 0.8, 30)]
    [WithBlankImage(200, 200, PixelTypes.Rgba32, 1.0, 30)]
    public void FillEllipticGradientBrushRotatedEllipsesWithDifferentRatio<TPixel>(
        TestImageProvider<TPixel> provider,
        float ratio,
        float rotationInDegree)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using Image<TPixel> image = provider.GetImage();

        Color yellow = Color.Yellow;
        Color red = Color.Red;
        Color black = Color.Black;

        Point center = new(image.Width / 2, image.Height / 2);

        double rotation = Math.PI * rotationInDegree / 180.0;
        double cos = Math.Cos(rotation);
        double sin = Math.Sin(rotation);

        int offsetY = image.Height / 6;
        int axisX = center.X + (int)-(offsetY * sin);
        int axisY = center.Y + (int)(offsetY * cos);

        EllipticGradientBrush brush = new(
            center,
            new Point(axisX, axisY),
            ratio,
            GradientRepetitionMode.None,
            new ColorStop(0, yellow),
            new ColorStop(1, red),
            new ColorStop(1, black));

        FormattableString outputDetails = $"{ratio:F2}_AT_{rotationInDegree:00}deg";
        DrawingOptions options = new();
        image.Mutate(ctx => ctx.Paint(options, canvas => canvas.Fill(brush)));
        image.DebugSave(provider, outputDetails, appendPixelTypeToFileName: false, appendSourceFileOrDescription: false);
        image.CompareToReferenceOutput(
            EllipticGradientTolerantComparer,
            provider,
            outputDetails,
            appendPixelTypeToFileName: false,
            appendSourceFileOrDescription: false);
    }

    public enum FillLinearGradientBrushImageCorner
    {
        TopLeft = 0,
        TopRight = 1,
        BottomLeft = 2,
        BottomRight = 3
    }

    [Theory]
    [WithBlankImage(10, 10, PixelTypes.Rgba32)]
    public void FillLinearGradientBrushWithEqualColorsReturnsUnicolorImage<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using Image<TPixel> image = provider.GetImage();
        Color red = Color.Red;

        LinearGradientBrush brush = new(
            new Point(0, 0),
            new Point(10, 0),
            GradientRepetitionMode.None,
            new ColorStop(0, red),
            new ColorStop(1, red));

        image.Mutate(ctx => ctx.Paint(canvas => canvas.Fill(brush)));

        image.DebugSave(provider, appendPixelTypeToFileName: false, appendSourceFileOrDescription: false);

        // No reference image needed: the whole output should be a single color.
        image.ComparePixelBufferTo(red);
    }

    [Theory]
    [WithBlankImage(20, 10, PixelTypes.Rgba32)]
    [WithBlankImage(20, 10, PixelTypes.Argb32)]
    [WithBlankImage(20, 10, PixelTypes.Rgb24)]
    public void FillLinearGradientBrushDoesNotDependOnSinglePixelType<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => provider.VerifyOperation(
            LinearGradientTolerantComparer,
            image =>
            {
                LinearGradientBrush brush = new(
                    new Point(0, 0),
                    new Point(image.Width, 0),
                    GradientRepetitionMode.None,
                    new ColorStop(0, Color.Blue),
                    new ColorStop(1, Color.Yellow));

                image.Mutate(ctx => ctx.Paint(canvas => canvas.Fill(brush)));
            },
            appendSourceFileOrDescription: false);

    [Theory]
    [WithBlankImage(500, 10, PixelTypes.Rgba32)]
    public void FillLinearGradientBrushHorizontalReturnsUnicolorColumns<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => provider.VerifyOperation(
            LinearGradientTolerantComparer,
            image =>
            {
                LinearGradientBrush brush = new(
                    new Point(0, 0),
                    new Point(image.Width, 0),
                    GradientRepetitionMode.None,
                    new ColorStop(0, Color.Red),
                    new ColorStop(1, Color.Yellow));

                image.Mutate(ctx => ctx.Paint(canvas => canvas.Fill(brush)));
            },
            false,
            false);

    [Theory]
    [WithBlankImage(500, 10, PixelTypes.Rgba32, GradientRepetitionMode.DontFill)]
    [WithBlankImage(500, 10, PixelTypes.Rgba32, GradientRepetitionMode.None)]
    [WithBlankImage(500, 10, PixelTypes.Rgba32, GradientRepetitionMode.Repeat)]
    [WithBlankImage(500, 10, PixelTypes.Rgba32, GradientRepetitionMode.Reflect)]
    public void FillLinearGradientBrushHorizontalGradientWithRepMode<TPixel>(
        TestImageProvider<TPixel> provider,
        GradientRepetitionMode repetitionMode)
        where TPixel : unmanaged, IPixel<TPixel>
        => provider.VerifyOperation(
            LinearGradientTolerantComparer,
            image =>
            {
                LinearGradientBrush brush = new(
                    new Point(0, 0),
                    new Point(image.Width / 10, 0),
                    repetitionMode,
                    new ColorStop(0, Color.Red),
                    new ColorStop(1, Color.Yellow));

                image.Mutate(ctx => ctx.Paint(canvas => canvas.Fill(brush)));
            },
            $"{repetitionMode}",
            false,
            false);

    [Theory]
    [WithBlankImage(200, 100, PixelTypes.Rgba32, new[] { 0.5f })]
    [WithBlankImage(200, 100, PixelTypes.Rgba32, new[] { 0.2f, 0.4f, 0.6f, 0.8f })]
    [WithBlankImage(200, 100, PixelTypes.Rgba32, new[] { 0.1f, 0.3f, 0.6f })]
    public void FillLinearGradientBrushWithDoubledStopsProduceDashedPatterns<TPixel>(
        TestImageProvider<TPixel> provider,
        float[] pattern)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        string variant = string.Join("_", pattern.Select(i => i.ToString(CultureInfo.InvariantCulture)));

        Assert.True(pattern.Length > 0);

        Color black = Color.Black;
        Color white = Color.White;

        ColorStop[] colorStops =
            [
                .. Enumerable.Repeat(new ColorStop(0, black), 1),
                .. pattern.SelectMany(
                    (f, index) =>
                    new[]
                    {
                        new ColorStop(f, index % 2 == 0 ? black : white),
                        new ColorStop(f, index % 2 == 0 ? white : black)
                    }),
                .. Enumerable.Repeat(new ColorStop(1, pattern.Length % 2 == 0 ? black : white), 1),
            ];

        using Image<TPixel> image = provider.GetImage();

        LinearGradientBrush brush =
            new(
                new Point(0, 0),
                new Point(image.Width, 0),
                GradientRepetitionMode.None,
                colorStops);

        image.Mutate(ctx => ctx.Paint(canvas => canvas.Fill(brush)));

        image.DebugSave(
            provider,
            variant,
            appendPixelTypeToFileName: false,
            appendSourceFileOrDescription: false);

        Assert.All(
            Enumerable.Range(0, image.Width).Select(i => image[i, 0]),
            color => Assert.True(
                color.Equals(black.ToPixel<TPixel>()) || color.Equals(white.ToPixel<TPixel>())));

        image.CompareToReferenceOutput(
            LinearGradientTolerantComparer,
            provider,
            variant,
            appendPixelTypeToFileName: false,
            appendSourceFileOrDescription: false);
    }

    [Theory]
    [WithBlankImage(10, 500, PixelTypes.Rgba32)]
    public void FillLinearGradientBrushVerticalBrushReturnsUnicolorRows<TPixel>(
        TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        provider.VerifyOperation(
            image =>
            {
                LinearGradientBrush brush = new(
                    new Point(0, 0),
                    new Point(0, image.Height),
                    GradientRepetitionMode.None,
                    new ColorStop(0, Color.Red),
                    new ColorStop(1, Color.Yellow));

                image.Mutate(ctx => ctx.Paint(canvas => canvas.Fill(brush)));

                VerifyAllRowsAreUnicolor(image);
            },
            false,
            false);

        static void VerifyAllRowsAreUnicolor(Image<TPixel> image)
        {
            for (int y = 0; y < image.Height; y++)
            {
                Span<TPixel> row = image.GetRootFramePixelBuffer().DangerousGetRowSpan(y);
                TPixel firstColorOfRow = row[0];
                foreach (TPixel p in row)
                {
                    Assert.Equal(firstColorOfRow, p);
                }
            }
        }
    }

    [Theory]
    [WithBlankImage(200, 200, PixelTypes.Rgba32, FillLinearGradientBrushImageCorner.TopLeft)]
    [WithBlankImage(200, 200, PixelTypes.Rgba32, FillLinearGradientBrushImageCorner.TopRight)]
    [WithBlankImage(200, 200, PixelTypes.Rgba32, FillLinearGradientBrushImageCorner.BottomLeft)]
    [WithBlankImage(200, 200, PixelTypes.Rgba32, FillLinearGradientBrushImageCorner.BottomRight)]
    public void FillLinearGradientBrushDiagonalReturnsCorrectImages<TPixel>(
        TestImageProvider<TPixel> provider,
        FillLinearGradientBrushImageCorner startCorner)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using Image<TPixel> image = provider.GetImage();

        Assert.True(
            image.Height == image.Width,
            "For the math check block at the end the image must be squared, but it is not.");

        int startX = (int)startCorner % 2 == 0 ? 0 : image.Width - 1;
        int startY = startCorner > FillLinearGradientBrushImageCorner.TopRight ? 0 : image.Height - 1;
        int endX = image.Height - startX - 1;
        int endY = image.Width - startY - 1;

        LinearGradientBrush brush =
            new(
                new Point(startX, startY),
                new Point(endX, endY),
                GradientRepetitionMode.None,
                new ColorStop(0, Color.Red),
                new ColorStop(1, Color.Yellow));

        image.Mutate(ctx => ctx.Paint(canvas => canvas.Fill(brush)));
        image.DebugSave(
            provider,
            startCorner,
            appendPixelTypeToFileName: false,
            appendSourceFileOrDescription: false);

        int verticalSign = startY == 0 ? 1 : -1;
        int horizontalSign = startX == 0 ? 1 : -1;

        for (int i = 0; i < image.Height; i++)
        {
            TPixel colorOnDiagonal = image[i, i];
            int orthoCount = 0;
            for (int offset = -orthoCount; offset < orthoCount; offset++)
            {
                Assert.Equal(colorOnDiagonal, image[i + (horizontalSign * offset), i + (verticalSign * offset)]);
            }
        }

        image.CompareToReferenceOutput(
            LinearGradientTolerantComparer,
            provider,
            startCorner,
            appendPixelTypeToFileName: false,
            appendSourceFileOrDescription: false);
    }

    [Theory]
    [WithBlankImage(500, 500, PixelTypes.Rgba32, 0, 0, 499, 499, new[] { 0f, .2f, .5f, .9f }, new[] { 0, 0, 1, 1 })]
    [WithBlankImage(500, 500, PixelTypes.Rgba32, 0, 499, 499, 0, new[] { 0f, 0.2f, 0.5f, 0.9f }, new[] { 0, 1, 2, 3 })]
    [WithBlankImage(500, 500, PixelTypes.Rgba32, 499, 499, 0, 0, new[] { 0f, 0.7f, 0.8f, 0.9f }, new[] { 0, 1, 2, 0 })]
    [WithBlankImage(500, 500, PixelTypes.Rgba32, 0, 0, 499, 499, new[] { 0f, .5f, 1f }, new[] { 0, 1, 3 })]
    public void FillLinearGradientBrushArbitraryGradients<TPixel>(
        TestImageProvider<TPixel> provider,
        int startX,
        int startY,
        int endX,
        int endY,
        float[] stopPositions,
        int[] stopColorCodes)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        Color[] colors =
        [
            Color.Navy, Color.LightGreen, Color.Yellow,
            Color.Red
        ];

        StringBuilder coloringVariant = new();
        ColorStop[] colorStops = new ColorStop[stopPositions.Length];

        for (int i = 0; i < stopPositions.Length; i++)
        {
            Color color = colors[stopColorCodes[i % colors.Length]];
            float position = stopPositions[i];
            colorStops[i] = new ColorStop(position, color);
            coloringVariant.AppendFormat(CultureInfo.InvariantCulture, "{0}@{1};", color.ToPixel<Rgba32>().ToHex(), position);
        }

        FormattableString variant = $"({startX},{startY})_TO_({endX},{endY})__[{coloringVariant}]";

        provider.VerifyOperation(
            image =>
            {
                LinearGradientBrush brush = new(
                    new Point(startX, startY),
                    new Point(endX, endY),
                    GradientRepetitionMode.None,
                    colorStops);

                image.Mutate(ctx => ctx.Paint(canvas => canvas.Fill(brush)));
            },
            variant,
            false,
            false);
    }

    [Theory]
    [WithBlankImage(200, 200, PixelTypes.Rgba32, 0, 0, 199, 199, new[] { 0f, .25f, .5f, .75f, 1f }, new[] { 0, 1, 2, 3, 4 })]
    public void FillLinearGradientBrushMultiplePointGradients<TPixel>(
        TestImageProvider<TPixel> provider,
        int startX,
        int startY,
        int endX,
        int endY,
        float[] stopPositions,
        int[] stopColorCodes)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        Color[] colors =
        [
            Color.Black, Color.Blue, Color.Red,
            Color.White, Color.Lime
        ];

        StringBuilder coloringVariant = new();
        ColorStop[] colorStops = new ColorStop[stopPositions.Length];

        for (int i = 0; i < stopPositions.Length; i++)
        {
            Color color = colors[stopColorCodes[i % colors.Length]];
            float position = stopPositions[i];
            colorStops[i] = new ColorStop(position, color);
            coloringVariant.AppendFormat(CultureInfo.InvariantCulture, "{0}@{1};", color.ToPixel<Rgba32>().ToHex(), position);
        }

        FormattableString variant = $"({startX},{startY})_TO_({endX},{endY})__[{coloringVariant}]";

        provider.VerifyOperation(
            image =>
            {
                LinearGradientBrush brush = new(
                    new Point(startX, startY),
                    new Point(endX, endY),
                    GradientRepetitionMode.None,
                    colorStops);

                image.Mutate(ctx => ctx.Paint(canvas => canvas.Fill(brush)));
            },
            variant,
            false,
            false);
    }

    [Theory]
    [WithBlankImage(200, 200, PixelTypes.Rgba32)]
    public void FillLinearGradientBrushGradientsWithTransparencyOnExistingBackground<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        provider.VerifyOperation(
            image =>
            {
                int width = image.Width;
                int height = image.Height;

                image.Mutate(ctx =>
                {
                    ctx.Paint(canvas => canvas.Fill(Brushes.Solid(Color.Red)));

                    DrawingOptions glossOptions = new()
                    {
                        GraphicsOptions = new GraphicsOptions
                        {
                            Antialias = true,
                            ColorBlendingMode = PixelColorBlendingMode.Normal,
                            AlphaCompositionMode = PixelAlphaCompositionMode.SrcAtop
                        }
                    };

                    IPathCollection glossPath = BuildGloss(width, height);
                    LinearGradientBrush linearGradientBrush = new(
                        new Point(0, 0),
                        new Point(0, height / 2),
                        GradientRepetitionMode.Repeat,
                        new ColorStop(0, Color.White.WithAlpha(0.5f)),
                        new ColorStop(1, Color.White.WithAlpha(0.25f)));

                    ctx.Paint(glossOptions, canvas => canvas.Fill(linearGradientBrush, glossPath));
                });
            });

        static IPathCollection BuildGloss(int imageWidth, int imageHeight)
        {
            PathBuilder pathBuilder = new();
            pathBuilder.AddLine(new PointF(0, 0), new PointF(imageWidth, 0));
            pathBuilder.AddLine(new PointF(imageWidth, 0), new PointF(imageWidth, imageHeight * 0.4f));
            pathBuilder.AddQuadraticBezier(
                new PointF(imageWidth, imageHeight * 0.4f),
                new PointF(imageWidth / 2f, imageHeight * 0.6f),
                new PointF(0, imageHeight * 0.4f));
            pathBuilder.CloseFigure();
            return new PathCollection(pathBuilder.Build());
        }
    }

    [Theory]
    [WithBlankImage(200, 200, PixelTypes.Rgb24)]
    public void FillLinearGradientBrushBrushApplicatorIsThreadSafeIssue1044<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => provider.VerifyOperation(
            LinearGradientTolerantComparer,
            image =>
            {
                PathGradientBrush brush = new(
                    [new PointF(0, 0), new PointF(200, 0), new PointF(200, 200), new PointF(0, 200), new PointF(0, 0)],
                    [Color.Red, Color.Yellow, Color.Green, Color.DarkCyan, Color.Red]);

                image.Mutate(ctx => ctx.Paint(canvas => canvas.Fill(brush)));
            },
            false,
            false);

    [Theory]
    [WithBlankImage(200, 200, PixelTypes.Rgba32)]
    public void FillLinearGradientBrushRotatedGradient<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => provider.VerifyOperation(
            image =>
            {
                LinearGradientBrush brush = new(
                    new Point(0, 0),
                    new Point(200, 200),
                    new Point(0, 100),
                    GradientRepetitionMode.None,
                    new ColorStop(0, Color.Red),
                    new ColorStop(1, Color.Yellow));

                image.Mutate(ctx => ctx.Paint(canvas => canvas.Fill(brush)));
            },
            false,
            false);
}
