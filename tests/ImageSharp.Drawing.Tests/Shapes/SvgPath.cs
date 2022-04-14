// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Tests.TestUtilities.ImageComparison;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace SixLabors.ImageSharp.Drawing.Tests.Shapes
{
    public class SvgPath
    {
        [Theory]
        [WithBlankImage(110, 70, PixelTypes.Rgba32, "M20,30 L40,5 L60,30 L80, 55 L100, 30", "zag")]
        [WithBlankImage(110, 50, PixelTypes.Rgba32, "M20,30 Q40,5 60,30 T100,30", "wave")]
        [WithBlankImage(500, 400, PixelTypes.Rgba32, @"M10,350 l 50,-25 a25,25 -30 0,1 50,-25 l 50,-25 a25,50 -30 0,1 50,-25 l 50,-25 a25,75 -30 0,1 50,-25 l 50,-25 a25,100 -30 0,1 50,-25 l 50,-25", "bumpy")]
        [WithBlankImage(500, 400, PixelTypes.Rgba32, @"M300,200 h-150 a150,150 0 1,0 150,-150 z", "pie_small")]
        [WithBlankImage(500, 400, PixelTypes.Rgba32, @"M275,175 v-150 a150,150 0 0,0 -150,150 z", "pie_big")]
        [WithBlankImage(100, 100, PixelTypes.Rgba32, @"M50,50 L50,20 L80,50 z M40,60 L40,90 L10,60 z", "arrows")]
        [WithBlankImage(500, 400, PixelTypes.Rgba32, @"M 10 315 L 110 215 A 30 50 0 0 1 162.55 162.45 L 172.55 152.45 A 30 50 -45 0 1 215.1 109.9 L 315 10", "chopped_oval")]
        public void RenderSvgPath<TPixel>(TestImageProvider<TPixel> provider, string svgPath, string exampleImageKey)
            where TPixel : unmanaged, IPixel<TPixel>
        {
            var parsed = Path.TryParseSvgPath(svgPath, out var path);
            Assert.True(parsed);

            provider.RunValidatingProcessorTest(
                c => c.Fill(Color.White).Draw(Color.Red, 5, path),
                new { type = exampleImageKey },
                comparer: ImageComparer.TolerantPercentage(0.002f));
        }
    }
}
