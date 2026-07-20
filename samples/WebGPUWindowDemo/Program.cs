// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Diagnostics;
using System.Numerics;
using SixLabors.Fonts;
using SixLabors.Fonts.Unicode;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Processing.Backends;
using SixLabors.ImageSharp.PixelFormats;
using Color = SixLabors.ImageSharp.Color;

namespace WebGPUWindowDemo;

/// <summary>
/// Demonstrates the ImageSharp.Drawing WebGPU backend rendering directly to a native window.
/// </summary>
public static class Program
{
    /// <summary>
    /// Creates the demo window and runs the animated sample scene.
    /// </summary>
    public static void Main()
    {
        // FIFO is the safest sample default: it presents in display order with normal v-sync behavior.
        using WebGPUWindow window = new(new WebGPUWindowOptions
        {
            Title = "ImageSharp.Drawing WebGPU Demo",
            Size = new Size(800, 600),
            Format = WebGPUTextureFormat.Bgra8Unorm,
            PresentMode = WebGPUPresentMode.Fifo,
        });

        DemoApp app = new(window);
        app.Run();
    }

    /// <summary>
    /// Owns the sample scene, animation state, and render loop callbacks for the demo window.
    /// </summary>
    private sealed class DemoApp
    {
        private const int BallCount = 1000;
        private static readonly TimeSpan FpsUpdateInterval = TimeSpan.FromSeconds(1);
        private static readonly Brush BackgroundBrush = Brushes.Solid(Color.FromPixel(new Bgra32(30, 30, 40, 255)));
        private static readonly Brush TextBrush = Brushes.Solid(Color.FromPixel(new Bgra32(242, 247, 255, 255)));
        private static readonly Brush CyanBrush = Brushes.Solid(Color.FromPixel(new Bgra32(68, 236, 255, 255)));
        private static readonly Brush MagentaBrush = Brushes.Solid(Color.FromPixel(new Bgra32(255, 104, 216, 255)));
        private static readonly Brush GoldBrush = Brushes.Solid(Color.FromPixel(new Bgra32(255, 218, 92, 255)));
        private static readonly Brush MintBrush = Brushes.Solid(Color.FromPixel(new Bgra32(112, 255, 185, 255)));

        // The shader-accelerated acrylic effect blurs the animated scene beneath the document
        // before applying the dark tint, preserving visible motion without sacrificing contrast.
        private static readonly WebGPUBackdropAcrylicLayerEffect TextBackdropEffect = new(
            2F,
            Color.FromPixel(new Bgra32(8, 12, 24, 150)));

        private readonly WebGPUWindow window;
        private readonly DrawingOptions drawingOptions = new();
        private readonly DrawingTextCache textCache = new();
        private readonly TextBlock scrollTextBlock;
        private readonly Random rng = new(42);
        private readonly Stopwatch fpsWindow = Stopwatch.StartNew();
        private Ball[] balls = [];
        private int frameCount;
        private float scrollOffset;
        private float scrollTextHeight;
        private float scrollWrappingLength;

        private const string ScrollText =
            "ImageSharp.Drawing on WebGPU\n" +
            "RICH TEXT • SHARED CACHE • NATIVE PRESENTATION\n\n" +
            "One prepared TextBlock combines bold emphasis, italic asides, " +
            "outlined accents, large callouts, and small annotations.\n\n" +
            "Decorations can underline links, overline headings, and strike out revisions " +
            "without splitting the document into separate draw calls.\n\n" +
            "Unicode shaping and font fallback: العربية • עברית • हिंदी • 中文 • 日本語 • 😀\n\n" +
            "SixLabors.Fonts shapes the text once. A DrawingTextCache owned by " +
            "the application then preserves reusable glyph and run geometry as " +
            "new frame canvases scroll the document.\n\n" +
            "The ordinary DrawingCanvas API still supplies fills, paths, text, " +
            "clipping, transforms, and Porter-Duff compositing.\n\n" +
            "SixLabors ImageSharp.Drawing\n" +
            "github.com/SixLabors/ImageSharp.Drawing";

        /// <summary>
        /// Initializes a new instance of the <see cref="DemoApp"/> class.
        /// </summary>
        /// <param name="window">The demo window that supplies update and render callbacks.</param>
        public DemoApp(WebGPUWindow window)
        {
            this.window = window;
            this.window.Update += this.OnUpdate;
            this.scrollTextBlock = CreateScrollTextBlock();
            this.InitializeScene();
        }

        /// <summary>
        /// Starts the window-owned render loop for the demo.
        /// </summary>
        public void Run() => this.window.Run(this.drawingOptions, this.textCache, this.OnRender);

        /// <summary>
        /// Builds the one-time scene state used by the demo.
        /// </summary>
        /// <remarks>
        /// The scrolling text is shaped once up front and reused by every frame canvas through one caller-owned cache.
        /// </remarks>
        private void InitializeScene()
        {
            Size framebufferSize = this.window.FramebufferSize;
            this.scrollWrappingLength = Math.Max(1F, framebufferSize.Width - 80F);
            this.scrollTextHeight = this.scrollTextBlock.MeasureAdvance(this.scrollWrappingLength).Height;

            Ball[] balls = new Ball[BallCount];
            for (int i = 0; i < balls.Length; i++)
            {
                balls[i] = Ball.CreateRandom(this.rng, framebufferSize.Width, framebufferSize.Height);
            }

            this.balls = balls;
        }

        /// <summary>
        /// Advances the animation state for the next frame.
        /// </summary>
        /// <param name="deltaTime">Elapsed time since the previous update.</param>
        private void OnUpdate(TimeSpan deltaTime)
        {
            Size framebufferSize = this.window.FramebufferSize;
            float dt = (float)deltaTime.TotalSeconds;
            for (int i = 0; i < this.balls.Length; i++)
            {
                this.balls[i].Update(dt, framebufferSize.Width, framebufferSize.Height);
            }

            this.scrollOffset += 200F * dt;
        }

        /// <summary>
        /// Draws one frame into the acquired WebGPU window surface.
        /// </summary>
        /// <param name="frame">The acquired frame that exposes the drawing canvas.</param>
        /// <remarks>
        /// The window loop disposes the frame after this callback returns, which renders queued canvas work,
        /// presents the swapchain texture, and releases the per-frame WebGPU handles.
        /// </remarks>
        private void OnRender(WebGPUSurfaceFrame frame)
        {
            DrawingCanvas canvas = frame.Canvas;
            Rectangle bounds = canvas.Bounds;
            canvas.Fill(BackgroundBrush);

            for (int i = 0; i < this.balls.Length; i++)
            {
                ref Ball ball = ref this.balls[i];
                ball.Draw(canvas);
            }

            this.DrawScrollingText(canvas, bounds.Width, bounds.Height);

            this.frameCount++;
            TimeSpan elapsed = this.fpsWindow.Elapsed;
            if (elapsed >= FpsUpdateInterval)
            {
                double fps = this.frameCount / elapsed.TotalSeconds;
                double frameTimeMs = elapsed.TotalMilliseconds / this.frameCount;
                this.window.Title = $"ImageSharp.Drawing WebGPU Demo - {frameTimeMs:F1} ms / {fps:F1} FPS";
                this.frameCount = 0;
                this.fpsWindow.Restart();
            }
        }

        /// <summary>
        /// Draws the prepared scrolling rich-text block.
        /// </summary>
        /// <param name="canvas">The destination canvas for the current frame.</param>
        /// <param name="width">The current framebuffer width.</param>
        /// <param name="height">The current framebuffer height.</param>
        private void DrawScrollingText(DrawingCanvas canvas, int width, int height)
        {
            float wrappingLength = Math.Max(1F, width - 80F);
            if (wrappingLength != this.scrollWrappingLength)
            {
                // TextBlock retains shaping, so a resize only recomputes line breaking and height;
                // it does not reshape the document or discard the shared glyph/run cache.
                this.scrollWrappingLength = wrappingLength;
                this.scrollTextHeight = this.scrollTextBlock.MeasureAdvance(wrappingLength).Height;
            }

            float totalCycle = height + this.scrollTextHeight;
            float wrappedOffset = this.scrollOffset % totalCycle;
            float y = height - wrappedOffset;

            // Limit the acrylic work to the visible part of the scrolling document. The extra
            // padding keeps blur samples around the first and last glyph rows inside the layer.
            Rectangle layerBounds = Rectangle.Intersect(
                canvas.Bounds,
                Rectangle.Round(new RectangleF(24F, y - 16F, width - 48F, this.scrollTextHeight + 32F)));

            canvas.SaveLayer(this.drawingOptions.GraphicsOptions, layerBounds, TextBackdropEffect);

            // Rich runs supply their own paint where specified; TextBrush paints the remaining body.
            // The cache keys run geometry in local space, so changing y does not invalidate it.
            canvas.DrawText(this.scrollTextBlock, new PointF(40F, y), wrappingLength, TextBrush, pen: null);
            canvas.Restore();
        }

        /// <summary>
        /// Creates the prepared rich-text document reused throughout the render loop.
        /// </summary>
        /// <returns>The prepared text block.</returns>
        private static TextBlock CreateScrollTextBlock()
        {
            Font bodyFont = SystemFonts.CreateFont("Arial", 24);
            Font titleFont = bodyFont.Family.CreateFont(44, FontStyle.Bold);
            Font boldFont = bodyFont.Family.CreateFont(24, FontStyle.Bold);
            Font italicFont = bodyFont.Family.CreateFont(24, FontStyle.Italic);
            Font calloutFont = bodyFont.Family.CreateFont(30, FontStyle.Bold);
            Font annotationFont = bodyFont.Family.CreateFont(18, FontStyle.Italic);

            List<FontFamily> fallbackFontFamilies = [];
            AddFallbackFont(fallbackFontFamilies, bodyFont.Family, new CodePoint('ع'));
            AddFallbackFont(fallbackFontFamilies, bodyFont.Family, new CodePoint('ע'));
            AddFallbackFont(fallbackFontFamilies, bodyFont.Family, new CodePoint('ह'));
            AddFallbackFont(fallbackFontFamilies, bodyFont.Family, new CodePoint('中'));
            AddFallbackFont(fallbackFontFamilies, bodyFont.Family, new CodePoint(0x1F600));

            RichTextRun title = CreateTextRun("ImageSharp.Drawing on WebGPU", titleFont, CyanBrush);
            title.Pen = Pens.Solid(Color.FromPixel(new Bgra32(5, 24, 42, 255)), 2.25F);

            RichTextRun subtitle = CreateTextRun("RICH TEXT • SHARED CACHE • NATIVE PRESENTATION", boldFont, GoldBrush);
            subtitle.TextDecorations = TextDecorations.Overline;
            subtitle.OverlinePen = Pens.Solid(Color.FromPixel(new Bgra32(255, 218, 92, 255)), 2F);

            RichTextRun bold = CreateTextRun("bold emphasis", boldFont, CyanBrush);
            RichTextRun italic = CreateTextRun("italic asides", italicFont, MagentaBrush);
            RichTextRun outlined = CreateTextRun("outlined accents", boldFont, BackgroundBrush);
            outlined.Pen = Pens.Solid(Color.FromPixel(new Bgra32(255, 218, 92, 255)), 2.25F);

            RichTextRun callout = CreateTextRun("large callouts", calloutFont, GoldBrush);
            RichTextRun annotation = CreateTextRun("small annotations", annotationFont, MagentaBrush);

            RichTextRun underline = CreateTextRun("underline links", boldFont, CyanBrush);
            underline.TextDecorations = TextDecorations.Underline;
            underline.UnderlinePen = Pens.Solid(Color.FromPixel(new Bgra32(68, 236, 255, 255)), 2F);

            RichTextRun overline = CreateTextRun("overline headings", boldFont, GoldBrush);
            overline.TextDecorations = TextDecorations.Overline;
            overline.OverlinePen = Pens.Solid(Color.FromPixel(new Bgra32(255, 218, 92, 255)), 2F);

            RichTextRun strikeout = CreateTextRun("strike out revisions", italicFont, MagentaBrush);
            strikeout.TextDecorations = TextDecorations.Strikeout;
            strikeout.StrikeoutPen = Pens.Solid(Color.FromPixel(new Bgra32(255, 104, 216, 255)), 2F);

            RichTextRun fallback = CreateTextRun("العربية • עברית • हिंदी • 中文 • 日本語 • 😀", boldFont, MintBrush);

            RichTextRun link = CreateTextRun("github.com/SixLabors/ImageSharp.Drawing", boldFont, CyanBrush);
            link.TextDecorations = TextDecorations.Underline;
            link.UnderlinePen = Pens.Solid(Color.FromPixel(new Bgra32(86, 225, 255, 255)), 2F);

            RichTextOptions options = new(bodyFont)
            {
                FallbackFontFamilies = fallbackFontFamilies,
                LineSpacing = 1.5F,
                TextRuns = [title, subtitle, bold, italic, outlined, callout, annotation, underline, overline, strikeout, fallback, link],
            };

            return new TextBlock(ScrollText, options);
        }

        /// <summary>
        /// Adds the installed system family selected by the platform text matcher for a representative character.
        /// </summary>
        /// <param name="fallbackFontFamilies">The ordered fallback list being built.</param>
        /// <param name="primaryFamily">The primary family already used by the document.</param>
        /// <param name="codePoint">A character requiring coverage from the selected fallback family.</param>
        private static void AddFallbackFont(List<FontFamily> fallbackFontFamilies, FontFamily primaryFamily, CodePoint codePoint)
        {
            // TextOptions consumes an explicit fallback list; it does not invoke the platform
            // matcher while shaping. Resolve representative characters once so DirectWrite,
            // CoreText, or Fontconfig can choose installed families appropriate to this machine.
            if (SystemFonts.TryMatchCharacter(codePoint, FontStyle.Regular, primaryFamily.Name, culture: null, out FontMatch match) &&
                !fallbackFontFamilies.Contains(match.Family))
            {
                fallbackFontFamilies.Add(match.Family);
            }
        }

        /// <summary>
        /// Creates a rich-text run for the unique marker text within <see cref="ScrollText"/>.
        /// </summary>
        /// <param name="marker">The text covered by the run.</param>
        /// <param name="font">The font applied to the run.</param>
        /// <param name="brush">The brush applied to the run.</param>
        /// <returns>The configured rich-text run.</returns>
        private static RichTextRun CreateTextRun(string marker, Font font, Brush brush)
        {
            int stringStart = ScrollText.IndexOf(marker, StringComparison.Ordinal);

            // RichTextRun uses grapheme indices rather than UTF-16 offsets. Computing both bounds
            // through Fonts keeps the markers correct when the sample contains bullets or other
            // multi-code-unit user-perceived characters.
            int start = ScrollText.AsSpan(0, stringStart).GetGraphemeCount();

            return new RichTextRun
            {
                Start = start,
                End = start + marker.GetGraphemeCount(),
                Font = font,
                Brush = brush,
            };
        }
    }

    /// <summary>
    /// Mutable physics state for one animated ball in the sample scene.
    /// </summary>
    private struct Ball
    {
        public float X;
        public float Y;
        public float VelocityX;
        public float VelocityY;
        public float Radius;
        private readonly Brush brush;
        private readonly EllipsePolygon shape;
        private readonly DrawingOptions drawingOptions;

        private Ball(
            float x,
            float y,
            float velocityX,
            float velocityY,
            float radius,
            Brush brush)
        {
            this.X = x;
            this.Y = y;
            this.VelocityX = velocityX;
            this.VelocityY = velocityY;
            this.Radius = radius;
            this.brush = brush;
            this.shape = new EllipsePolygon(0, 0, radius);
            this.drawingOptions = new DrawingOptions();
        }

        /// <summary>
        /// Creates a random ball that fits within the current framebuffer bounds.
        /// </summary>
        /// <param name="rng">The deterministic random source used by the sample.</param>
        /// <param name="width">The framebuffer width.</param>
        /// <param name="height">The framebuffer height.</param>
        /// <returns>The initialized ball.</returns>
        public static Ball CreateRandom(Random rng, int width, int height)
        {
            float radius = 20F + (rng.NextSingle() * 40F);
            Color color = Color.FromPixel(new Bgra32(
                (byte)(80 + rng.Next(176)),
                (byte)(80 + rng.Next(176)),
                (byte)(80 + rng.Next(176)),
                200));

            return new Ball(
                radius + (rng.NextSingle() * (width - (2 * radius))),
                radius + (rng.NextSingle() * (height - (2 * radius))),
                (100F + (rng.NextSingle() * 200F)) * (rng.Next(2) == 0 ? -1 : 1),
                (100F + (rng.NextSingle() * 200F)) * (rng.Next(2) == 0 ? -1 : 1),
                radius,
                Brushes.Solid(color));
        }

        /// <summary>
        /// Draws the retained ball shape at its current animated location.
        /// </summary>
        /// <remarks>
        /// The ellipse is centered at the origin and moved through per-ball drawing options so the path geometry
        /// remains cached across frames without allocating a new shape each render.
        /// </remarks>
        /// <param name="canvas">The destination canvas for the current frame.</param>
        public readonly void Draw(DrawingCanvas canvas)
        {
            this.drawingOptions.Transform = Matrix4x4.CreateTranslation(this.X, this.Y, 0);

            canvas.Save(this.drawingOptions);
            canvas.Fill(this.brush, this.shape);
            canvas.Restore();
        }

        /// <summary>
        /// Advances the ball and reflects it off the framebuffer edges.
        /// </summary>
        /// <param name="dt">Elapsed time since the previous update, in seconds.</param>
        /// <param name="width">The framebuffer width.</param>
        /// <param name="height">The framebuffer height.</param>
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
