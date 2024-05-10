// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SixLabors.Fonts;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Drawing.Tests.Issues;
public class Issue_330
{
    [Theory]
    [WithSolidFilledImages(2084, 2084, nameof(Color.BlueViolet), PixelTypes.Rgba32)]
    public void OffsetTextOutlines<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        FontFamily fontFamily = TestFontUtilities.GetFontFamily(TestFonts.OpenSans);

        Font bibfont = fontFamily.CreateFont(600, FontStyle.Bold);
        Font namefont = fontFamily.CreateFont(140, FontStyle.Bold);

        provider.RunValidatingProcessorTest(p =>
        {
            p.DrawText(
                new RichTextOptions(bibfont)
                {
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    TextAlignment = TextAlignment.Center,
                    TextDirection = TextDirection.LeftToRight,
                    Origin = new Point(1156, 1024),
                },
                "9999",
                Brushes.Solid(Color.White),
                Pens.Solid(Color.Black, 20));

            p.DrawText(
                new RichTextOptions(namefont)
                {
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    TextAlignment = TextAlignment.Center,
                    TextDirection = TextDirection.LeftToRight,
                    Origin = new Point(1156, 713),
                },
                "JOHAN",
                Brushes.Solid(Color.White),
                Pens.Solid(Color.Black, 5));

            p.DrawText(
                new RichTextOptions(namefont)
                {
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    TextAlignment = TextAlignment.Center,
                    TextDirection = TextDirection.LeftToRight,
                    Origin = new Point(1156, 1381),
                },
                "TIGERTECH",
                Brushes.Solid(Color.White),
                Pens.Solid(Color.Black, 5));
        });
    }
}
