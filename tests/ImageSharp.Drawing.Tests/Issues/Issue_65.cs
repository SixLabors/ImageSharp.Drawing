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

        [Theory]
        [InlineData(-110)] //Crash
        [InlineData(-99)]  //Fine
        [InlineData(99)]   //Fine
        [InlineData(110)]  //Crash
        public void DrawCircleOutsideBoundsDrawingArea(int xpos)
        {
            int width = 100;
            int height = 100;

            using (var image = new Image<Rgba32>(width, height, Color.Red))
            {
                var circle = new EllipsePolygon(xpos, 0, width, height);

                image.Mutate(x => x.Fill(Color.Black, circle));
            }
        }
    }
}
