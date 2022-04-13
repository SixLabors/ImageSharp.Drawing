// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace SixLabors.ImageSharp.Drawing.Tests.Shapes
{
    public class SvgPath
    {
        [Theory]
        [WithBlankImage(200, 100, PixelTypes.Rgba32, "M20,30 L40,5 L60,30 L80, 55 L100, 30", "zag")]
        [WithBlankImage(200, 100, PixelTypes.Rgba32, "M20,30 Q40,5 60,30 T100,30", "wave")]
        [WithBlankImage(500, 400, PixelTypes.Rgba32, @"M10,350 l 50,-25 a25,25 -30 0,1 50,-25 l 50,-25 a25,50 -30 0,1 50,-25 l 50,-25 a25,75 -30 0,1 50,-25 l 50,-25 a25,100 -30 0,1 50,-25 l 50,-25", "bumpy")]
        [WithBlankImage(500, 400, PixelTypes.Rgba32, @"M300,200 h-150 a150,150 0 1,0 150,-150 z", "pie_small")]
        [WithBlankImage(500, 400, PixelTypes.Rgba32, @"M275,175 v-150 a150,150 0 0,0 -150,150 z", "pie_big")]
        public void RenderSvgPath<TPixel>(TestImageProvider<TPixel> provider, string svgPath, string exampleImageKey)
            where TPixel : unmanaged, IPixel<TPixel>
        {
            var parsed = Path.TryParseSvgPath(svgPath, out var path);
            Assert.True(parsed);

            provider.RunValidatingProcessorTest(
                c => c.Fill(Color.White).Draw(Color.Red, 5, path),
                new { type = exampleImageKey });
        }
    }
}