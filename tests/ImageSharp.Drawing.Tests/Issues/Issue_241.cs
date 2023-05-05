// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System.Numerics;
using SixLabors.Fonts;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Xunit;

namespace SixLabors.ImageSharp.Drawing.Tests.Issues
{
    public class Issue_241
    {
        [Fact]
        public void DoesNotThrowArgumentOutOfRangeException()
        {
            if (!TestEnvironment.IsWindows)
            {
                return;
            }

            FontFamily fontFamily = SystemFonts.Get("Arial");
            RichTextOptions opt = new(fontFamily.CreateFont(100, FontStyle.Regular))
            {
                Origin = new Vector2(159, 0)
            };
            const string content = "TEST";

            using Image image = new Image<Rgba32>(512, 256, Color.Black);
            image.Mutate(x => x.DrawText(opt, content, Brushes.Horizontal(Color.Orange)));
        }
    }
}
