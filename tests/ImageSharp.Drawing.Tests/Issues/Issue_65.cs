using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Xunit;

namespace SixLabors.ImageSharp.Drawing.Tests.Issues
{
    [GroupOutput("Drawing")]
    public class Issue_65
    {
        [Theory]
        [InlineData(-100)] //Crash
        [InlineData(-99)]  //Fine
        [InlineData(99)]   //Fine
        [InlineData(100)]  //Crash
        public void DrawRectactangleOutsideBoundsDrawingArea(int xpos)
        {
            int width = 100;
            int height = 100;

            using (var image = new Image<Rgba32>(width, height, Color.Red))
            {
                var rectangle = new Rectangle(xpos, 0, width, height);

                image.Mutate(x => x.Fill(Color.Black, rectangle));
            }
        }

        public static TheoryData<int> DrawCircleOutsideBoundsDrawingArea_Data = new TheoryData<int>()
        {
            -110, -99, 0, 99, 110
        };

        [Theory]
        [WithSolidFilledImages(nameof(DrawCircleOutsideBoundsDrawingArea_Data), 100, 100, nameof(Color.Red), PixelTypes.Rgba32)]
        public void DrawCircleOutsideBoundsDrawingArea(TestImageProvider<Rgba32> provider, int xpos)
        {
            int width = 100;
            int height = 100;

            using var image = provider.GetImage();
            var circle = new EllipsePolygon(xpos, 0, width, height);

            provider.RunValidatingProcessorTest(x => x.Fill(Color.Black, circle),
                $"xpos({xpos})",
                appendPixelTypeToFileName: false,
                appendSourceFileOrDescription: false);
        }
    }
}
