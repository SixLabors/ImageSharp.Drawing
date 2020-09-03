using System.Text;
using SixLabors.Fonts;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Xunit;

namespace SixLabors.ImageSharp.Drawing.Tests
{
    public class ProfilingBenchmarks
    {
        [Theory]
        [WithSolidFilledImages(1300, 1300, 0, 0, 0, PixelTypes.Rgba32, 1, 20, 36)]
        public void RenderLongText<TPixel>(TestImageProvider<TPixel> provider, int times, int rows, int cols)
            where TPixel : unmanaged, IPixel<TPixel>
        {
            using Image<TPixel> image = provider.GetImage();
            Font font = CreateFont("Arial", 50);

            var text = CreateText(rows, cols);
            for (int i = 0; i < times; i++)
            {
                image.Mutate(ctx => ctx.DrawText(text, font, Color.White, default));
                // if (i == 0)
                // {
                //     image.DebugSave(provider);
                // }
            }
        }

        private static string CreateText(int rows, int cols)
        {
            StringBuilder bld = new StringBuilder();
            const char firstChar = '!';
            const char lastChar = 'z';
            int cnt = lastChar - firstChar;

            int currentChar = firstChar;
            for (int i = 0; i < rows; i++)
            {
                for (int j = 0; j < cols; j++)
                {
                    bld.Append((char)currentChar);
                    currentChar++;
                    if (currentChar > lastChar)
                    {
                        currentChar = firstChar;
                    }
                }

                bld.AppendLine();
            }

            string text = bld.ToString();
            return text;
        }


        private static Font CreateFont(string fontName, int size)
        {
            return SystemFonts.CreateFont(fontName, size);
            // var fontCollection = new FontCollection();
            // string fontPath = TestFontUtilities.GetPath(fontName);
            // Font font = fontCollection.Install(fontPath).CreateFont(size);
            // return font;
        }
    }
}