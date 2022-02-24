// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Text;
using SixLabors.Fonts;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Shapes.Rasterization;
using SixLabors.ImageSharp.Drawing.Tests.TestUtilities.ImageComparison;
using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Xunit;

namespace SixLabors.ImageSharp.Drawing.Tests.Shapes.Scan
{
    public class RasterizerExtensionsTests
    {
        [Fact]
        public void DoesNotOverwriteIsDirtyFlagWhenOnlyFillingSubpixels()
        {
            var scanner = PolygonScanner.Create(new RectangularPolygon(0.3f, 0.2f, 0.7f, 1.423f), 0, 20, 1, IntersectionRule.OddEven, MemoryAllocator.Default);

            float[] buffer = new float[12];

            scanner.MoveToNextPixelLine(); // offset

            bool isDirty = scanner.ScanCurrentPixelLineInto(0, 0, buffer.AsSpan());

            Assert.True(isDirty);
        }

        [Theory]
        [WithSolidFilledImages(400, 75, "White", PixelTypes.Rgba32)]
        public void AntialiasingIsAntialiased<TPixel>(TestImageProvider<TPixel> provider)
            where TPixel : unmanaged, IPixel<TPixel>
        {
            Font font36 = TestFontUtilities.GetFont(TestFonts.OpenSans, 20);
            var textOpt = new TextOptions(font36)
            {
                Dpi = 96,
                Origin = new PointF(0, 0)
            };

            provider.RunValidatingProcessorTest(x => x
                 .SetGraphicsOptions(o => o.Antialias = false)
                 .DrawText(textOpt, "Hello, World!", Color.Black));
        }
    }
}
