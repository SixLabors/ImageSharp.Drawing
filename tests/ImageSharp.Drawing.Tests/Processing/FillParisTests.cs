// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Processing.Backends;
using SixLabors.ImageSharp.Drawing.Tests.TestUtilities.Attributes;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace SixLabors.ImageSharp.Drawing.Tests.Processing;

#pragma warning disable xUnit1004 // Test methods should not be skipped
public class FillParisTests
{
    private const float Scale = 1f;
    private const int Width = 1096;
    private const int Height = 1060;

    private static readonly string SvgFilePath =
        TestFile.GetInputFileFullPath(TestImages.Svg.Paris30k);

    private static readonly List<SvgBenchmarkHelper.SvgElement> Elements = SvgBenchmarkHelper.ParseSvg(SvgFilePath);
    private static readonly List<(IPath Path, SolidBrush Fill, SolidPen Stroke)> IsElements =
        SvgBenchmarkHelper.BuildImageSharpElements(Elements, Scale);

    [Fact(Skip = "Benchmarking only")]
    public void FillParis_ImageSharp_CPU()
    {
        using Image<Rgba32> image = new(Width, Height);
        image.Mutate(c => c.Paint(canvas =>
        {
            foreach ((IPath path, SolidBrush fill, SolidPen stroke) in IsElements)
            {
                if (fill is not null)
                {
                    canvas.Fill(fill, path);
                }

                if (stroke is not null)
                {
                    canvas.Draw(stroke, path);
                }
            }
        }));
    }

    [WebGPUFact(Skip = "Benchmarking Only")]
    public void FillParis_ImageSharp_WebGPU()
    {
        using WebGPURenderTarget<Rgba32> target = new(Width, Height);
        using DrawingCanvas canvas = target.CreateCanvas();

        foreach ((IPath path, SolidBrush fill, SolidPen stroke) in IsElements)
        {
            if (fill is not null)
            {
                canvas.Fill(fill, path);
            }

            if (stroke is not null)
            {
                canvas.Draw(stroke, path);
            }
        }

        canvas.Flush();
        using Image<Rgba32> readbackImage = target.Readback();
        Assert.True(ContainsNonDefaultPixel(readbackImage));
    }

    private static bool ContainsNonDefaultPixel(Image<Rgba32> image)
    {
        for (int y = 0; y < image.Height; y++)
        {
            for (int x = 0; x < image.Width; x++)
            {
                if (image[x, y] != default)
                {
                    return true;
                }
            }
        }

        return false;
    }
}
