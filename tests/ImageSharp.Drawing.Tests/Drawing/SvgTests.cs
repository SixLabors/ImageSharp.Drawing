// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace SixLabors.ImageSharp.Drawing.Tests.Drawing;

public class SvgTests
{
    [Theory]
    [WithBlankImage(200, 200, PixelTypes.Rgba32, 1f)]
    public void Tiger<TPixel>(TestImageProvider<TPixel> provider, float scale)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        List<(IPath Path, SolidBrush Fill, SolidPen Stroke)> elements = SvgBenchmarkHelper.BuildImageSharpElements(
            SvgBenchmarkHelper.ParseSvg(TestFile.GetInputFileFullPath(TestImages.Svg.GhostscriptTiger)), scale);
        Image<TPixel> image = provider.GetImage();
        image.Mutate(c =>
        {
            foreach ((IPath path, SolidBrush fill, SolidPen stroke) in elements)
            {
                if (fill is not null)
                {
                    c.Fill(fill, path);
                }

                if (stroke is not null)
                {
                    c.Draw(stroke, path);
                }
            }
        });
    }
}
