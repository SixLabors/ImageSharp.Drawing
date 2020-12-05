// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Tests.TestUtilities.ImageComparison;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Xunit;

namespace SixLabors.ImageSharp.Drawing.Tests.Drawing
{
    [GroupOutput("Drawing/GradientBrushes")]
    public class FillEllipticGradientBrushTests
    {
        private static readonly ImageComparer TolerantComparer = ImageComparer.TolerantPercentage(0.01f);

        [Theory]
        [WithBlankImage(10, 10, PixelTypes.Rgba32)]
        public void WithEqualColorsReturnsUnicolorImage<TPixel>(
            TestImageProvider<TPixel> provider)
            where TPixel : unmanaged, IPixel<TPixel>
        {
            Color red = Color.Red;

            using (Image<TPixel> image = provider.GetImage())
            {
                var unicolorLinearGradientBrush =
                    new EllipticGradientBrush(
                        new Point(0, 0),
                        new Point(10, 0),
                        1.0f,
                        GradientRepetitionMode.None,
                        new ColorStop(0, red),
                        new ColorStop(1, red));

                image.Mutate(x => x.Fill(unicolorLinearGradientBrush));

                image.DebugSave(provider, appendPixelTypeToFileName: false, appendSourceFileOrDescription: false);

                // no need for reference image in this test:
                image.ComparePixelBufferTo(red);
            }
        }

        [Theory]
        [WithBlankImage(200, 200, PixelTypes.Rgba32, 0.1)]
        [WithBlankImage(200, 200, PixelTypes.Rgba32, 0.4)]
        [WithBlankImage(200, 200, PixelTypes.Rgba32, 0.8)]
        [WithBlankImage(200, 200, PixelTypes.Rgba32, 1.0)]
        [WithBlankImage(200, 200, PixelTypes.Rgba32, 1.2)]
        [WithBlankImage(200, 200, PixelTypes.Rgba32, 1.6)]
        [WithBlankImage(200, 200, PixelTypes.Rgba32, 2.0)]
        public void AxisParallelEllipsesWithDifferentRatio<TPixel>(
            TestImageProvider<TPixel> provider,
            float ratio)
            where TPixel : unmanaged, IPixel<TPixel>
        {
            Color yellow = Color.Yellow;
            Color red = Color.Red;
            Color black = Color.Black;

            provider.VerifyOperation(
                TolerantComparer,
                image =>
                    {
                        var unicolorLinearGradientBrush = new EllipticGradientBrush(
                            new Point(image.Width / 2, image.Height / 2),
                            new Point(image.Width / 2, image.Width * 2 / 3),
                            ratio,
                            GradientRepetitionMode.None,
                            new ColorStop(0, yellow),
                            new ColorStop(1, red),
                            new ColorStop(1, black));

                        image.Mutate(x => x.Fill(unicolorLinearGradientBrush));
                    },
                $"{ratio:F2}",
                false,
                false);
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
        public void RotatedEllipsesWithDifferentRatio<TPixel>(
            TestImageProvider<TPixel> provider,
            float ratio,
            float rotationInDegree)
            where TPixel : unmanaged, IPixel<TPixel>
        {
            FormattableString variant = $"{ratio:F2}_AT_{rotationInDegree:00}deg";

            provider.VerifyOperation(
                TolerantComparer,
                image =>
                    {
                        Color yellow = Color.Yellow;
                        Color red = Color.Red;
                        Color black = Color.Black;

                        var center = new Point(image.Width / 2, image.Height / 2);

                        double rotation = Math.PI * rotationInDegree / 180.0;
                        double cos = Math.Cos(rotation);
                        double sin = Math.Sin(rotation);

                        int offsetY = image.Height / 6;
                        int axisX = center.X + (int)-(offsetY * sin);
                        int axisY = center.Y + (int)(offsetY * cos);

                        var unicolorLinearGradientBrush = new EllipticGradientBrush(
                            center,
                            new Point(axisX, axisY),
                            ratio,
                            GradientRepetitionMode.None,
                            new ColorStop(0, yellow),
                            new ColorStop(1, red),
                            new ColorStop(1, black));

                        image.Mutate(x => x.Fill(unicolorLinearGradientBrush));
                    },
                variant,
                false,
                false);
        }
    }
}
