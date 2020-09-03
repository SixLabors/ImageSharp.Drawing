using System.Drawing;
using System.Text;
using BenchmarkDotNet.Attributes;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Font = SixLabors.Fonts.Font;
using SDBitmap = System.Drawing.Bitmap;
using SDGraphics = System.Drawing.Graphics;
using SDFont = System.Drawing.Font;
using SDPixelFormat = System.Drawing.Imaging.PixelFormat;
using SDRectangleF = System.Drawing.RectangleF;
using SystemFonts = SixLabors.Fonts.SystemFonts;

namespace SixLabors.ImageSharp.Drawing.Benchmarks
{
    public class DrawLongText
    {
        private Image<Rgba32> imageSharpImage;
        private SDBitmap sdBitmap;
        private string text;
        private Font font;
        private SDFont sdFont;

        public const int Width = 1300;
        public const int Height = 1300;
        public const int Rows = 20;
        public const int Cols = 36;
        
        [GlobalSetup]
        public void Setup()
        {
            this.text = CreateText(Rows, Cols);
            this.imageSharpImage = new Image<Rgba32>(Width, Height, Color.Black.ToPixel<Rgba32>());
            this.font = SystemFonts.CreateFont("Arial", 50);
            
            this.sdBitmap = new SDBitmap(Width, Height, SDPixelFormat.Format32bppArgb);
            this.sdFont = new SDFont("Arial", 12, System.Drawing.GraphicsUnit.Point);
        }

        [GlobalCleanup]
        public void Cleanup()
        {
            this.sdFont.Dispose();
            this.imageSharpImage.Dispose();
            this.sdBitmap.Dispose();
        }

        [Benchmark]
        public void ImageSharp()
        {
            this.imageSharpImage.Mutate(ctx => ctx.DrawText(this.text, this.font, Color.White, default));
        }

        [Benchmark(Baseline = true)]
        public void SystemDrawing()
        {
            using SDGraphics graphics = SDGraphics.FromImage(this.sdBitmap);
            graphics.InterpolationMode =  System.Drawing.Drawing2D.InterpolationMode.Default;
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            SDRectangleF rect = new SDRectangleF(0, 0, Width, Height);
            graphics.DrawString(
                this.text,
                this.sdFont,
                System.Drawing.Brushes.HotPink,
                rect);
        }
        
        private static string CreateText(int rows, int cols)
        {
            StringBuilder bld = new StringBuilder();
            const char firstChar = '!';
            const char lastChar = 'z';

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
    }
}