// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Xunit;

namespace SixLabors.ImageSharp.Drawing.Tests.Issues
{
    public class Issue_37
    {
        [Fact]
        public void CanRenderLargeFont()
        {
            if (!TestEnvironment.IsWindows)
            {
                return;
            }

            using (var image = new Image<Rgba32>(300, 200))
            {
                string text = "TEST text foiw|\\";

                Fonts.Font font = Fonts.SystemFonts.CreateFont("Arial", 40, Fonts.FontStyle.Regular);
                var graphicsOptions = new GraphicsOptions { Antialias = false };
                image.Mutate(x =>
                {
                    x.BackgroundColor(Color.White)
                    .DrawLines(
                        new ShapeGraphicsOptions { GraphicsOptions = graphicsOptions },
                        Color.Black,
                        1,
                        new PointF(0, 50),
                        new PointF(150, 50))
                    .DrawText(
                        new TextGraphicsOptions { GraphicsOptions = graphicsOptions },
                        text,
                        font,
                        Color.Black,
                        new PointF(50, 50));
                });
            }
        }
    }
}
