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
    public class FillPathGradientBrushTests
    {
        private static readonly ImageComparer TolerantComparer = ImageComparer.TolerantPercentage(0.01f);

        [Theory]
        [WithBlankImage(10, 10, PixelTypes.Rgba32)]
        public void FillRectangleWithDifferentColors<TPixel>(TestImageProvider<TPixel> provider)
            where TPixel : unmanaged, IPixel<TPixel>
            => provider.VerifyOperation(
                TolerantComparer,
                image =>
                {
                    PointF[] points = { new PointF(0, 0), new PointF(10, 0), new PointF(10, 10), new PointF(0, 10) };
                    Color[] colors = { Color.Black, Color.Red, Color.Yellow, Color.Green };

                    var brush = new PathGradientBrush(points, colors);

                    image.Mutate(x => x.Fill(brush));
                    image.DebugSave(provider, appendPixelTypeToFileName: false, appendSourceFileOrDescription: false);
                });

        [Theory]
        [WithBlankImage(20, 20, PixelTypes.Rgba32)]
        public void FillTriangleWithDifferentColors<TPixel>(TestImageProvider<TPixel> provider)
            where TPixel : unmanaged, IPixel<TPixel>
            => provider.VerifyOperation(
                TolerantComparer,
                image =>
                {
                    PointF[] points = { new PointF(10, 0), new PointF(20, 20), new PointF(0, 20) };
                    Color[] colors = { Color.Red, Color.Green, Color.Blue };

                    var brush = new PathGradientBrush(points, colors);

                    image.Mutate(x => x.Fill(brush));
                    image.DebugSave(provider, appendPixelTypeToFileName: false, appendSourceFileOrDescription: false);
                });

        [Theory]
        [WithBlankImage(20, 20, PixelTypes.HalfSingle)]
        public void FillTriangleWithGreyscale<TPixel>(TestImageProvider<TPixel> provider)
            where TPixel : unmanaged, IPixel<TPixel>
            => provider.VerifyOperation(
                ImageComparer.TolerantPercentage(0.02f),
                image =>
                {
                    PointF[] points = { new PointF(10, 0), new PointF(20, 20), new PointF(0, 20) };

                    var c1 = default(Rgba32);
                    var c2 = default(Rgba32);
                    var c3 = default(Rgba32);
                    new HalfSingle(-1).ToRgba32(ref c1);
                    new HalfSingle(0).ToRgba32(ref c2);
                    new HalfSingle(1).ToRgba32(ref c3);

                    Color[] colors = { new Color(c1), new Color(c2), new Color(c3) };

                    var brush = new PathGradientBrush(points, colors);

                    image.Mutate(x => x.Fill(brush));
                    image.DebugSave(provider, appendPixelTypeToFileName: false, appendSourceFileOrDescription: false);
                });

        [Theory]
        [WithBlankImage(20, 20, PixelTypes.Rgba32)]
        public void FillTriangleWithDifferentColorsCenter<TPixel>(TestImageProvider<TPixel> provider)
            where TPixel : unmanaged, IPixel<TPixel>
            => provider.VerifyOperation(
                TolerantComparer,
                image =>
                {
                    PointF[] points = { new PointF(10, 0), new PointF(20, 20), new PointF(0, 20) };
                    Color[] colors = { Color.Red, Color.Green, Color.Blue };

                    var brush = new PathGradientBrush(points, colors, Color.White);

                    image.Mutate(x => x.Fill(brush));
                    image.DebugSave(provider, appendPixelTypeToFileName: false, appendSourceFileOrDescription: false);
                });

        [Theory]
        [WithBlankImage(10, 10, PixelTypes.Rgba32)]
        public void FillRectangleWithSingleColor<TPixel>(TestImageProvider<TPixel> provider)
            where TPixel : unmanaged, IPixel<TPixel>
        {
            using (Image<TPixel> image = provider.GetImage())
            {
                PointF[] points = { new PointF(0, 0), new PointF(10, 0), new PointF(10, 10), new PointF(0, 10) };
                Color[] colors = { Color.Red };

                var brush = new PathGradientBrush(points, colors);

                image.Mutate(x => x.Fill(brush));

                image.ComparePixelBufferTo(Color.Red);
            }
        }

        [Theory]
        [WithBlankImage(10, 10, PixelTypes.Rgba32)]
        public void ShouldRotateTheColorsWhenThereAreMorePoints<TPixel>(TestImageProvider<TPixel> provider)
            where TPixel : unmanaged, IPixel<TPixel>
            => provider.VerifyOperation(
                TolerantComparer,
                image =>
                {
                    PointF[] points = { new PointF(0, 0), new PointF(10, 0), new PointF(10, 10), new PointF(0, 10) };
                    Color[] colors = { Color.Red, Color.Yellow };

                    var brush = new PathGradientBrush(points, colors);

                    image.Mutate(x => x.Fill(brush));
                    image.DebugSave(provider, appendPixelTypeToFileName: false, appendSourceFileOrDescription: false);
                });

        [Theory]
        [WithBlankImage(10, 10, PixelTypes.Rgba32)]
        public void FillWithCustomCenterColor<TPixel>(TestImageProvider<TPixel> provider)
            where TPixel : unmanaged, IPixel<TPixel>
            => provider.VerifyOperation(
                TolerantComparer,
                image =>
                {
                    PointF[] points = { new PointF(0, 0), new PointF(10, 0), new PointF(10, 10), new PointF(0, 10) };
                    Color[] colors = { Color.Black, Color.Red, Color.Yellow, Color.Green };

                    var brush = new PathGradientBrush(points, colors, Color.White);

                    image.Mutate(x => x.Fill(brush));
                    image.DebugSave(provider, appendPixelTypeToFileName: false, appendSourceFileOrDescription: false);
                });

        [Fact]
        public void ShouldThrowArgumentNullExceptionWhenLinesAreNull()
        {
            Color[] colors = { Color.Black, Color.Red, Color.Yellow, Color.Green };

            PathGradientBrush Create() => new PathGradientBrush(null, colors, Color.White);

            Assert.Throws<ArgumentNullException>(Create);
        }

        [Fact]
        public void ShouldThrowArgumentOutOfRangeExceptionWhenLessThan3PointsAreGiven()
        {
            PointF[] points = { new PointF(0, 0), new PointF(10, 0) };
            Color[] colors = { Color.Black, Color.Red, Color.Yellow, Color.Green };

            PathGradientBrush Create() => new PathGradientBrush(points, colors, Color.White);

            Assert.Throws<ArgumentOutOfRangeException>(Create);
        }

        [Fact]
        public void ShouldThrowArgumentNullExceptionWhenColorsAreNull()
        {
            PointF[] points = { new PointF(0, 0), new PointF(10, 0), new PointF(10, 10), new PointF(0, 10) };

            PathGradientBrush Create() => new PathGradientBrush(points, null, Color.White);

            Assert.Throws<ArgumentNullException>(Create);
        }

        [Fact]
        public void ShouldThrowArgumentOutOfRangeExceptionWhenEmptyColorArrayIsGiven()
        {
            PointF[] points = { new PointF(0, 0), new PointF(10, 0), new PointF(10, 10), new PointF(0, 10) };

            var colors = new Color[0];

            PathGradientBrush Create() => new PathGradientBrush(points, colors, Color.White);

            Assert.Throws<ArgumentOutOfRangeException>(Create);
        }

        [Theory]
        [WithBlankImage(100, 100, PixelTypes.Rgba32)]
        public void FillComplex<TPixel>(TestImageProvider<TPixel> provider)
            where TPixel : unmanaged, IPixel<TPixel>
            => provider.VerifyOperation(
                new TolerantImageComparer(0.2f),
                image =>
                {
                    var star = new Star(50, 50, 5, 20, 45);
                    PointF[] points = star.Points.ToArray();
                    Color[] colors =
                    {
                        Color.Red, Color.Yellow, Color.Green, Color.Blue, Color.Purple,
                        Color.Red, Color.Yellow, Color.Green, Color.Blue, Color.Purple
                    };

                    var brush = new PathGradientBrush(points, colors, Color.White);

                    image.Mutate(x => x.Fill(brush));
                },
                appendSourceFileOrDescription: false,
                appendPixelTypeToFileName: false);
    }
}
