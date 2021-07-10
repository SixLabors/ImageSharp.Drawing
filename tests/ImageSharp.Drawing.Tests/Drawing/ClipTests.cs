// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
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
        [WithBasicTestPatternImages(250, 350, PixelTypes.Rgba32)]
        public void Clip<TPixel>(TestImageProvider<TPixel> provider)
            where TPixel : unmanaged, IPixel<TPixel>
            => provider.RunValidatingProcessorTest(
            x =>
            {
                Size size = x.GetCurrentSize();
                int outerRadii = Math.Min(size.Width, size.Height) / 2;
                var star = new Star(new PointF(size.Width / 2, size.Height / 2), 5, outerRadii / 2, outerRadii);
                x.Clip(star, x => x.DetectEdges());
            },
            appendPixelTypeToFileName: false,
            appendSourceFileOrDescription: false);
    }
}
