// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using Silk.NET.Core.Native;
using Silk.NET.Maths;
using Silk.NET.WebGPU;
using Silk.NET.Windowing;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Processing.Backends;
using SixLabors.ImageSharp.Drawing.Text;
using SixLabors.ImageSharp.PixelFormats;
using Color = SixLabors.ImageSharp.Color;
using Rectangle = SixLabors.ImageSharp.Rectangle;

namespace WebGPUWindowDemo;

/// <summary>
/// Demonstrates the ImageSharp.Drawing WebGPU backend rendering directly to a native
/// swap chain surface. Bouncing ellipses and vertically scrolling text are composited
/// each frame using the <see cref="DrawingCanvas{TPixel}"/> API backed by a WebGPU
/// compute compositor.
/// </summary>
public static unsafe class Program
{
    private const int WindowWidth = 800;
    private const int WindowHeight = 600;
    private const int BallCount = 500;

    // Silk.NET WebGPU API and windowing handles.
    private static WebGPU wgpu;
    private static IWindow window;

    // WebGPU device-level handles.
    private static Instance* instance;
    private static Surface* surface;
    private static SurfaceConfiguration surfaceConfiguration;
    private static Adapter* adapter;
    private static Device* device;
    private static Queue* queue;

    // ImageSharp.Drawing backend and configuration.
    private static WebGPUDrawingBackend backend;
    private static Configuration drawingConfiguration;

    // Bouncing ball simulation state.
    private static Ball[] balls;
    private static readonly Random Rng = new(42);

    // FPS counter state.
    private static int frameCount;
    private static double fpsElapsed;

    // Scrolling text state — glyph geometry is built once at startup via TextBuilder
    // and translated vertically each frame. Only glyphs whose bounds intersect the
    // visible viewport are submitted for rasterization.
    private static IPathCollection scrollPaths;
    private static float scrollOffset;
    private static float scrollTextHeight;
    private const string ScrollText =
        "ImageSharp.Drawing on WebGPU\n\n" +
        "Real-time GPU-accelerated 2D vector graphics " +
        "rendered directly to a native swap chain surface.\n\n" +
        "The canvas API provides a familiar drawing model: " +
        "Fill, Draw, DrawText, Clip, and Transform — " +
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
        "Built with Silk.NET WebGPU bindings.\n" +
        "Running on your GPU right now.";

    public static void Main()
    {
        // Create a window with no built-in graphics API — we manage WebGPU ourselves.
        WindowOptions options = WindowOptions.Default;
        options.API = GraphicsAPI.None;
        options.Size = new Vector2D<int>(WindowWidth, WindowHeight);
        options.Title = "ImageSharp.Drawing WebGPU Demo";
        options.ShouldSwapAutomatically = false;
        options.IsContextControlDisabled = true;

        window = Window.Create(options);
        window.Load += OnLoad;
        window.Update += OnUpdate;
        window.Render += OnRender;
        window.Closing += OnClosing;
        window.FramebufferResize += OnFramebufferResize;

        window.Run();
    }

    /// <summary>
    /// Called once when the window is ready. Bootstraps the WebGPU device, configures
    /// the swap chain, initializes the ImageSharp.Drawing backend, pre-builds the
    /// scrolling text geometry, and seeds the ball simulation.
    /// </summary>
    private static void OnLoad()
    {
        // Bootstrap WebGPU: instance → surface → adapter → device → queue.
        wgpu = WebGPU.GetApi();

        InstanceDescriptor instanceDescriptor = default;
        instance = wgpu.CreateInstance(&instanceDescriptor);

        surface = window.CreateWebGPUSurface(wgpu, instance);

        // Request an adapter compatible with our window surface.
        RequestAdapterOptions adapterOptions = new()
        {
            CompatibleSurface = surface
        };

        wgpu.InstanceRequestAdapter(
            instance,
            ref adapterOptions,
            new PfnRequestAdapterCallback((_, a, _, _) => adapter = a),
            null);

        Console.WriteLine($"Adapter: 0x{(nuint)adapter:X}");

        // Request a device with Bgra8UnormStorage — required by the compute compositor
        // to write storage textures in Bgra8Unorm format (the swap chain format).
        FeatureName requiredFeature = FeatureName.Bgra8UnormStorage;
        DeviceDescriptor deviceDescriptor = new()
        {
            DeviceLostCallback = new PfnDeviceLostCallback(DeviceLost),
            RequiredFeatureCount = 1,
            RequiredFeatures = &requiredFeature,
        };

        wgpu.AdapterRequestDevice(
            adapter,
            in deviceDescriptor,
            new PfnRequestDeviceCallback((_, d, _, _) => device = d),
            null);

        wgpu.DeviceSetUncapturedErrorCallback(device, new PfnErrorCallback(UncapturedError), null);

        queue = wgpu.DeviceGetQueue(device);

        Console.WriteLine($"Device: 0x{(nuint)device:X}, Queue: 0x{(nuint)queue:X}");

        // Configure the swap chain.
        ConfigureSwapchain();

        // Initialize the ImageSharp.Drawing WebGPU backend and attach it to a
        // cloned Configuration so it doesn't affect the global default.
        backend = new WebGPUDrawingBackend();
        drawingConfiguration = Configuration.Default.Clone();
        drawingConfiguration.SetDrawingBackend(backend);

        // Pre-build scrolling text geometry at the origin. TextBuilder converts the
        // shaped text into an IPathCollection of glyph outlines that can be cheaply
        // translated each frame without re-shaping or re-building outlines.
        Font scrollFont = SystemFonts.CreateFont("Arial", 24);
        TextOptions textOptions = new(scrollFont)
        {
            Origin = new Vector2(WindowWidth / 2f, 0),
            WrappingLength = WindowWidth - 80,
            HorizontalAlignment = HorizontalAlignment.Center,
            LineSpacing = 1.6f,
        };

        scrollPaths = TextBuilder.GeneratePaths(ScrollText, textOptions);
        FontRectangle bounds = TextMeasurer.MeasureSize(ScrollText, textOptions);
        scrollTextHeight = bounds.Height;

        // Seed the bouncing ball simulation with random positions, velocities, and colors.
        balls = new Ball[BallCount];
        for (int i = 0; i < BallCount; i++)
        {
            balls[i] = Ball.CreateRandom(Rng, WindowWidth, WindowHeight);
        }

        Console.WriteLine("WebGPU windowed demo initialized.");
    }

    /// <summary>
    /// Configures (or reconfigures) the swap chain for the current framebuffer size.
    /// Uses Bgra8Unorm to match the <see cref="Bgra32"/> canvas pixel format.
    /// CopyDst is required because the compositor copies from a transient output texture
    /// into the swap chain target. TextureBinding is needed for backdrop sampling.
    /// </summary>
    private static void ConfigureSwapchain()
    {
        surfaceConfiguration = new SurfaceConfiguration
        {
            Usage = TextureUsage.RenderAttachment | TextureUsage.CopyDst | TextureUsage.TextureBinding,
            Format = TextureFormat.Bgra8Unorm,
            PresentMode = PresentMode.Fifo,
            Device = device,
            Width = (uint)window.FramebufferSize.X,
            Height = (uint)window.FramebufferSize.Y,
        };

        wgpu.SurfaceConfigure(surface, ref surfaceConfiguration);
    }

    /// <summary>
    /// Reconfigures the swap chain when the window is resized.
    /// </summary>
    private static void OnFramebufferResize(Vector2D<int> size)
    {
        if (size.X > 0 && size.Y > 0)
        {
            ConfigureSwapchain();
        }
    }

    /// <summary>
    /// Fixed-timestep update: advances ball positions and the scroll offset.
    /// </summary>
    private static void OnUpdate(double deltaTime)
    {
        int w = window.FramebufferSize.X;
        int h = window.FramebufferSize.Y;
        float dt = (float)deltaTime;

        // Integrate ball positions and bounce off walls.
        for (int i = 0; i < balls.Length; i++)
        {
            balls[i].Update(dt, w, h);
        }

        // Advance scrolling text vertically (pixels per second).
        scrollOffset += 200f * dt;
    }

    /// <summary>
    /// Per-frame render callback. Acquires a swap chain texture, wraps it as a
    /// <see cref="NativeSurface"/>, creates a <see cref="DrawingCanvas{TPixel}"/>,
    /// draws all content, flushes the GPU composition, and presents.
    /// </summary>
    private static void OnRender(double deltaTime)
    {
        int w = window.FramebufferSize.X;
        int h = window.FramebufferSize.Y;
        if (w <= 0 || h <= 0)
        {
            return;
        }

        // Acquire the next swap chain texture from the surface.
        SurfaceTexture surfaceTexture;
        wgpu.SurfaceGetCurrentTexture(surface, &surfaceTexture);
        switch (surfaceTexture.Status)
        {
            case SurfaceGetCurrentTextureStatus.Timeout:
            case SurfaceGetCurrentTextureStatus.Outdated:
            case SurfaceGetCurrentTextureStatus.Lost:
                wgpu.TextureRelease(surfaceTexture.Texture);
                ConfigureSwapchain();
                return;
            case SurfaceGetCurrentTextureStatus.OutOfMemory:
            case SurfaceGetCurrentTextureStatus.DeviceLost:
                throw new InvalidOperationException($"Surface texture error: {surfaceTexture.Status}");
        }

        TextureView* textureView = wgpu.TextureCreateView(surfaceTexture.Texture, null);

        try
        {
            // Wrap the swap chain texture as a NativeSurface so the drawing backend
            // can composite directly into it. The format must match the swap chain
            // configuration (Bgra8Unorm) and the canvas pixel type (Bgra32).
            NativeSurface nativeSurface = WebGPUNativeSurfaceFactory.Create<Bgra32>(
                (nint)device,
                (nint)queue,
                (nint)surfaceTexture.Texture,
                (nint)textureView,
                WebGPUTextureFormatId.Bgra8Unorm,
                w,
                h);

            // NativeCanvasFrame exposes only the GPU surface (no CPU region),
            // so the backend always takes the GPU composition path.
            NativeCanvasFrame<Bgra32> frame = new(new Rectangle(0, 0, w, h), nativeSurface);

            // Create a drawing canvas targeting the swap chain frame.
            using DrawingCanvas<Bgra32> canvas = new(drawingConfiguration, frame, new DrawingOptions());

            // Clear to a dark background.
            canvas.Fill(Brushes.Solid(Color.FromPixel(new Bgra32(30, 30, 40, 255))));

            // Draw vertically scrolling text behind the balls.
            DrawScrollingText(canvas, w, h);

            // Draw each ball as a filled ellipse.
            for (int i = 0; i < balls.Length; i++)
            {
                ref Ball ball = ref balls[i];
                EllipsePolygon ellipse = new(ball.X, ball.Y, ball.Radius);
                canvas.Fill(Brushes.Solid(ball.Color), ellipse);
            }

            // Flush submits all queued draw operations to the GPU compositor and
            // copies the composited result into the swap chain texture.
            canvas.Flush();
        }
        finally
        {
            // Present the frame and release per-frame WebGPU resources.
            wgpu.SurfacePresent(surface);
            wgpu.TextureViewRelease(textureView);
            wgpu.TextureRelease(surfaceTexture.Texture);
        }

        // Update FPS counter in the window title once per second.
        frameCount++;
        fpsElapsed += deltaTime;
        if (fpsElapsed >= 1.0)
        {
            window.Title = $"ImageSharp.Drawing WebGPU Demo — {frameCount / fpsElapsed:F1} FPS | GPU: {backend.DiagnosticGpuCompositeCount} Fallback: {backend.DiagnosticFallbackCompositeCount}";
            frameCount = 0;
            fpsElapsed = 0;
        }
    }

    /// <summary>
    /// Draws the pre-built scrolling text geometry. The full text block scrolls upward
    /// and loops when it passes above the window. Each glyph path is bounds-tested
    /// against the viewport so only visible glyphs are rasterized.
    /// </summary>
    private static void DrawScrollingText(DrawingCanvas<Bgra32> canvas, int w, int h)
    {
        if (scrollTextHeight <= 0)
        {
            return;
        }

        // Total cycle: text enters from the bottom, scrolls up, exits the top, then loops.
        float totalCycle = h + scrollTextHeight;
        float wrappedOffset = scrollOffset % totalCycle;
        float y = h - wrappedOffset;

        Matrix3x2 translation = Matrix3x2.CreateTranslation(0, y);
        RectangleF viewport = new(0, 0, w, h);
        Brush textBrush = Brushes.Solid(Color.FromPixel(new Bgra32(70, 70, 100, 255)));

        // Each IPath in scrollPaths is one glyph outline. Skip any whose translated
        // bounding box doesn't intersect the visible area.
        foreach (IPath path in scrollPaths)
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

            canvas.Fill(textBrush, path.Transform(new Matrix4x4(translation)));
        }
    }

    /// <summary>
    /// Tears down the drawing backend and releases all WebGPU resources in reverse
    /// creation order.
    /// </summary>
    private static void OnClosing()
    {
        backend.Dispose();

        wgpu.DeviceRelease(device);
        wgpu.AdapterRelease(adapter);
        wgpu.SurfaceRelease(surface);
        wgpu.InstanceRelease(instance);
        wgpu.Dispose();
    }

    /// <summary>WebGPU device-lost callback — logs the reason to the console.</summary>
    private static void DeviceLost(DeviceLostReason reason, byte* message, void* userData)
        => Console.WriteLine($"Device lost ({reason}): {SilkMarshal.PtrToString((nint)message)}");

    /// <summary>WebGPU uncaptured error callback — logs validation errors to the console.</summary>
    private static void UncapturedError(ErrorType type, byte* message, void* userData)
        => Console.WriteLine($"WebGPU {type}: {SilkMarshal.PtrToString((nint)message)}");

    /// <summary>
    /// A simple bouncing ball with position, velocity, radius, and color.
    /// Reflects off the window edges each frame.
    /// </summary>
    private struct Ball
    {
        public float X;
        public float Y;
        public float VelocityX;
        public float VelocityY;
        public float Radius;
        public Color Color;

        /// <summary>
        /// Creates a ball with a random position inside the window bounds, a random
        /// velocity between 100-300 px/s in each axis, a random radius between 20-60 px,
        /// and a random semi-transparent color.
        /// </summary>
        public static Ball CreateRandom(Random rng, int width, int height)
        {
            float radius = 20f + (rng.NextSingle() * 40f);
            return new Ball
            {
                X = radius + (rng.NextSingle() * (width - (2 * radius))),
                Y = radius + (rng.NextSingle() * (height - (2 * radius))),
                VelocityX = (100f + (rng.NextSingle() * 200f)) * (rng.Next(2) == 0 ? -1 : 1),
                VelocityY = (100f + (rng.NextSingle() * 200f)) * (rng.Next(2) == 0 ? -1 : 1),
                Radius = radius,
                Color = Color.FromPixel(new Bgra32(
                    (byte)(80 + rng.Next(176)),
                    (byte)(80 + rng.Next(176)),
                    (byte)(80 + rng.Next(176)),
                    200)),
            };
        }

        /// <summary>
        /// Integrates position by velocity and reflects off the window edges.
        /// </summary>
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
