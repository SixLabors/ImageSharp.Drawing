// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Processing.Backends;
using SixLabors.ImageSharp.Drawing.Text;
using SixLabors.ImageSharp.PixelFormats;
using Color = SixLabors.ImageSharp.Color;

namespace WebGPUWindowDemo;

/// <summary>
/// Demonstrates the ImageSharp.Drawing WebGPU backend rendering directly to a native window.
/// </summary>
public static class Program
{
    public static void Main()
    {
        using WebGPUWindow<Bgra32> window = new(new WebGPUWindowOptions
        {
            Title = "ImageSharp.Drawing WebGPU Demo",
            Size = new Size(800, 600),
            PresentMode = WebGPUPresentMode.Fifo,
        });

        DemoApp app = new(window);
        app.Run();
    }

    private sealed class DemoApp
    {
        private const int BallCount = 1000;

        private readonly WebGPUWindow<Bgra32> window;
        private readonly Random rng = new(42);
        private Ball[] balls = [];
        private int frameCount;
        private double fpsElapsed;
        private double fpsSum;
        private double fpsSumSquares;
        private IPathCollection scrollPaths = new PathCollection();
        private float scrollOffset;
        private float scrollTextHeight;

        private const string ScrollText =
            "ImageSharp.Drawing on WebGPU\n\n" +
            "Real-time GPU-accelerated 2D vector graphics " +
            "rendered directly to a native window.\n\n" +
            "The canvas API provides a familiar drawing model: " +
            "Fill, Draw, DrawText, Clip, and Transform - " +
            "all composited on the GPU via compute shaders.\n\n" +
            "Text is shaped once using SixLabors.Fonts and " +
            "converted to vector paths via TextBuilder. " +
            "Each frame simply translates the cached geometry.\n\n" +
            "Shapes are rasterized into coverage masks on the " +
            "CPU, uploaded to GPU textures, then composited " +
            "using a WebGPU compute pipeline that evaluates " +
            "Porter-Duff blending per pixel.\n\n" +
            "The drawing backend automatically manages texture " +
            "atlases, bind groups, and pipeline state. It falls " +
            "back to the CPU backend for unsupported pixel " +
            "formats or when no GPU device is available.\n\n" +
            "SixLabors ImageSharp.Drawing\n" +
            "github.com/SixLabors/ImageSharp.Drawing\n\n" +
            "Built on the new WebGPUWindow<TPixel> wrapper.";

        public DemoApp(WebGPUWindow<Bgra32> window)
        {
            this.window = window;
            this.window.Update += this.OnUpdate;
            this.InitializeScene();
        }

        public void Run() => this.window.Run(this.OnRender);

        private void InitializeScene()
        {
            Font scrollFont = SystemFonts.CreateFont("Arial", 24);
            Size framebufferSize = this.window.FramebufferSize;
            TextOptions textOptions = new(scrollFont)
            {
                Origin = new Vector2(framebufferSize.Width / 2F, 0),
                WrappingLength = framebufferSize.Width - 80,
                HorizontalAlignment = HorizontalAlignment.Center,
                LineSpacing = 1.6F,
            };

            this.scrollPaths = TextBuilder.GeneratePaths(ScrollText, textOptions);
            FontRectangle bounds = TextMeasurer.MeasureSize(ScrollText, textOptions);
            this.scrollTextHeight = bounds.Height;

            Ball[] balls = new Ball[BallCount];
            for (int i = 0; i < balls.Length; i++)
            {
                balls[i] = Ball.CreateRandom(this.rng, framebufferSize.Width, framebufferSize.Height);
            }

            this.balls = balls;
        }

        private void OnUpdate(double deltaTime)
        {
            Size framebufferSize = this.window.FramebufferSize;
            float dt = (float)deltaTime;
            for (int i = 0; i < this.balls.Length; i++)
            {
                this.balls[i].Update(dt, framebufferSize.Width, framebufferSize.Height);
            }

            this.scrollOffset += 200F * dt;
        }

        private void OnRender(WebGPUWindowFrame<Bgra32> frame)
        {
            DrawingCanvas<Bgra32> canvas = frame.Canvas;
            canvas.Fill(Brushes.Solid(Color.FromPixel(new Bgra32(30, 30, 40, 255))));

            this.DrawScrollingText(canvas, frame.FramebufferSize.Width, frame.FramebufferSize.Height);

            for (int i = 0; i < this.balls.Length; i++)
            {
                ref Ball ball = ref this.balls[i];
                EllipsePolygon ellipse = new(ball.X, ball.Y, ball.Radius);
                canvas.Fill(Brushes.Solid(ball.Color), ellipse);
            }

            this.frameCount++;
            double frameSeconds = frame.DeltaTime.TotalSeconds;
            this.fpsElapsed += frameSeconds;
            double frameFps = frameSeconds > 0D ? 1D / frameSeconds : 0D;
            this.fpsSum += frameFps;
            this.fpsSumSquares += frameFps * frameFps;
            if (this.fpsElapsed >= 1D)
            {
                double meanFps = this.fpsSum / this.frameCount;
                double variance = Math.Max(0D, (this.fpsSumSquares / this.frameCount) - (meanFps * meanFps));
                double stdDevFps = Math.Sqrt(variance);
                double frameTimeMs = frame.DeltaTime.TotalMilliseconds;
                this.window.Title = $"ImageSharp.Drawing WebGPU Demo - Current: {frameTimeMs:F1} ms / {frameFps:F1} FPS | Mean: {meanFps:F1} FPS | StdDev: {stdDevFps:F1}";
                this.frameCount = 0;
                this.fpsElapsed = 0;
                this.fpsSum = 0;
                this.fpsSumSquares = 0;
            }
        }

        private void DrawScrollingText(DrawingCanvas<Bgra32> canvas, int width, int height)
        {
            if (this.scrollTextHeight <= 0)
            {
                return;
            }

            float totalCycle = height + this.scrollTextHeight;
            float wrappedOffset = this.scrollOffset % totalCycle;
            float y = height - wrappedOffset;

            Matrix3x2 translation = Matrix3x2.CreateTranslation(0, y);
            RectangleF viewport = new(0, 0, width, height);
            Brush textBrush = Brushes.Solid(Color.FromPixel(new Bgra32(70, 70, 100, 255)));
            DrawingOptions translatedOptions = new()
            {
                Transform = new Matrix4x4(translation),
            };

            canvas.Save(translatedOptions);
            foreach (IPath path in this.scrollPaths)
            {
                RectangleF pathBounds = path.Bounds;
                RectangleF translated = new(
                    pathBounds.X + translation.M31,
                    pathBounds.Y + translation.M32,
                    pathBounds.Width,
                    pathBounds.Height);

                if (!viewport.IntersectsWith(translated))
                {
                    continue;
                }

                canvas.Fill(textBrush, path);
            }

            canvas.Restore();
        }
    }

    private struct Ball
    {
        public float X;
        public float Y;
        public float VelocityX;
        public float VelocityY;
        public float Radius;
        public Color Color;

        public static Ball CreateRandom(Random rng, int width, int height)
        {
            float radius = 20F + (rng.NextSingle() * 40F);
            return new Ball
            {
                X = radius + (rng.NextSingle() * (width - (2 * radius))),
                Y = radius + (rng.NextSingle() * (height - (2 * radius))),
                VelocityX = (100F + (rng.NextSingle() * 200F)) * (rng.Next(2) == 0 ? -1 : 1),
                VelocityY = (100F + (rng.NextSingle() * 200F)) * (rng.Next(2) == 0 ? -1 : 1),
                Radius = radius,
                Color = Color.FromPixel(new Bgra32(
                    (byte)(80 + rng.Next(176)),
                    (byte)(80 + rng.Next(176)),
                    (byte)(80 + rng.Next(176)),
                    200)),
            };
        }

        public void Update(float dt, int width, int height)
        {
            this.X += this.VelocityX * dt;
            this.Y += this.VelocityY * dt;

            if (this.X - this.Radius < 0)
            {
                this.X = this.Radius;
                this.VelocityX = MathF.Abs(this.VelocityX);
            }
            else if (this.X + this.Radius > width)
            {
                this.X = width - this.Radius;
                this.VelocityX = -MathF.Abs(this.VelocityX);
            }

            if (this.Y - this.Radius < 0)
            {
                this.Y = this.Radius;
                this.VelocityY = MathF.Abs(this.VelocityY);
            }
            else if (this.Y + this.Radius > height)
            {
                this.Y = height - this.Radius;
                this.VelocityY = -MathF.Abs(this.VelocityY);
            }
        }
    }
}
