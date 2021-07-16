// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Numerics;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Xunit;

namespace SixLabors.ImageSharp.Drawing.Tests.Drawing
{
    [GroupOutput("Drawing")]
    public class ClipTests
    {
        [Theory]
        [WithBasicTestPatternImages(250, 350, PixelTypes.Rgba32, 0, 0)]
        [WithBasicTestPatternImages(250, 350, PixelTypes.Rgba32, -20, -20)]
        [WithBasicTestPatternImages(250, 350, PixelTypes.Rgba32, 20, 20)]
        public void Clip<TPixel>(TestImageProvider<TPixel> provider, float dx, float dy)
            where TPixel : unmanaged, IPixel<TPixel>
        {
            FormattableString testDetails = $"offset_x{dx}_y{dy}";
            provider.RunValidatingProcessorTest(
                x =>
                {
                    Size size = x.GetCurrentSize();
                    int outerRadii = Math.Min(size.Width, size.Height) / 2;
                    var star = new Star(new PointF(size.Width / 2, size.Height / 2), 5, outerRadii / 2, outerRadii);

                    var builder = Matrix3x2.CreateTranslation(new Vector2(dx, dy));
                    x.Clip(star.Transform(builder), x => x.DetectEdges());
                },
                testOutputDetails: testDetails,
                appendPixelTypeToFileName: false,
                appendSourceFileOrDescription: false);
        }
    }
}
