// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Silk.NET.WebGPU;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Processing.Backends;
using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.PixelFormats;
using Bitmap = System.Drawing.Bitmap;
using Brush = SixLabors.ImageSharp.Drawing.Processing.Brush;
using Brushes = SixLabors.ImageSharp.Drawing.Processing.Brushes;
using Color = SixLabors.ImageSharp.Color;
using PointF = SixLabors.ImageSharp.PointF;
using Rectangle = SixLabors.ImageSharp.Rectangle;

namespace DrawingBackendBenchmark;

/// <summary>
/// Entry point for the line-drawing backend benchmark sample.
/// </summary>
internal static class Program
{
    /// <summary>
    /// Starts the Windows Forms benchmark host.
    /// </summary>
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new BenchmarkForm());
    }
}

/// <summary>
/// Interactive benchmark window for comparing the CPU and WebGPU drawing backends.
/// </summary>
internal sealed class BenchmarkForm : Form
{
    private const int BenchmarkWidth = 600;
    private const int BenchmarkHeight = 400;

    private readonly ComboBox backendSelector;
    private readonly NumericUpDown iterationSelector;
    private readonly TextBox statusTextBox;
    private readonly Panel previewHost;
    private readonly PictureBox previewBox;
    private readonly WebGpuOffscreenHost webGpuHost;

    /// <summary>
    /// Initializes a new instance of the <see cref="BenchmarkForm"/> class.
    /// </summary>
    public BenchmarkForm()
    {
        this.Text = "Drawing Backend Benchmark";
        this.ClientSize = new System.Drawing.Size(780, 560);
        this.StartPosition = FormStartPosition.CenterScreen;

        FlowLayoutPanel toolbar = new()
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            Padding = new Padding(8),
        };

        this.backendSelector = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 140,
        };
        this.backendSelector.Items.AddRange(["CPU", "WebGPU"]);
        this.backendSelector.SelectedIndex = 0;

        this.iterationSelector = new NumericUpDown
        {
            Minimum = 1,
            Maximum = 100,
            Value = 1,
            Width = 70,
        };

        toolbar.Controls.Add(new Label { AutoSize = true, Text = "Backend:", Margin = new Padding(0, 8, 6, 0) });
        toolbar.Controls.Add(this.backendSelector);
        toolbar.Controls.Add(new Label { AutoSize = true, Text = "Iterations:", Margin = new Padding(12, 8, 6, 0) });
        toolbar.Controls.Add(this.iterationSelector);
        toolbar.Controls.Add(this.CreateRunButton("10", 10));
        toolbar.Controls.Add(this.CreateRunButton("1k", 1_000));
        toolbar.Controls.Add(this.CreateRunButton("10k", 10_000));
        toolbar.Controls.Add(this.CreateRunButton("100k", 100_000));

        this.statusTextBox = new TextBox
        {
            Dock = DockStyle.Top,
            Multiline = true,
            ReadOnly = true,
            BorderStyle = BorderStyle.None,
            BackColor = SystemColors.Control,
            Height = 56,
            ScrollBars = ScrollBars.Vertical,
            Text = "Select a backend and run a benchmark.",
        };

        this.previewHost = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BackColor = System.Drawing.Color.FromArgb(24, 36, 56),
            Padding = new Padding(12),
        };

        this.previewBox = new PictureBox
        {
            BackColor = System.Drawing.Color.FromArgb(24, 36, 56),
            SizeMode = PictureBoxSizeMode.Normal,
            Size = new System.Drawing.Size(BenchmarkWidth, BenchmarkHeight),
        };
        this.previewHost.Controls.Add(this.previewBox);

        this.Controls.Add(this.previewHost);
        this.Controls.Add(this.statusTextBox);
        this.Controls.Add(toolbar);
        this.Resize += (_, _) => this.LayoutPreview();

        this.webGpuHost = new WebGpuOffscreenHost();
        if (!this.webGpuHost.IsSupported)
        {
            this.backendSelector.Items.Remove("WebGPU");
            this.statusTextBox.Text = $"WebGPU unavailable: {this.webGpuHost.InitializationError}";
        }

        this.LayoutPreview();
        this.Shown += (_, _) => this.backendSelector.Focus();
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            this.previewBox.Image?.Dispose();
            this.webGpuHost.Dispose();
        }

        base.Dispose(disposing);
    }

    /// <summary>
    /// Creates one toolbar button that runs the benchmark with the requested line count.
    /// </summary>
    private Button CreateRunButton(string text, int lineCount)
    {
        Button button = new()
        {
            AutoSize = true,
            Text = text,
            Margin = new Padding(12, 0, 0, 0),
        };
        button.Click += (_, _) => this.RunBenchmark(lineCount);
        return button;
    }

    /// <summary>
    /// Executes one benchmark run for the selected backend and updates the preview and status text.
    /// </summary>
    private void RunBenchmark(int lineCount)
    {
        int iterations = (int)this.iterationSelector.Value;
        BenchmarkBackend backend = this.GetSelectedBackend();

        if (backend == BenchmarkBackend.WebGpu && !this.webGpuHost.IsSupported)
        {
            this.statusTextBox.Text = $"WebGPU unavailable: {this.webGpuHost.InitializationError}";
            return;
        }

        Random rng = new(0);
        List<double> samples = new(iterations);

        Cursor previousCursor = this.Cursor;
        this.Cursor = Cursors.WaitCursor;
        try
        {
            for (int i = 0; i < iterations; i++)
            {
                LineSpec[] lines = GenerateLines(lineCount, BenchmarkWidth, BenchmarkHeight, rng);
                bool capturePreview = i == iterations - 1;
                using BenchmarkRenderResult result = backend == BenchmarkBackend.WebGpu
                    ? this.webGpuHost.Render(lines, BenchmarkWidth, BenchmarkHeight, capturePreview)
                    : CpuRenderer.Render(lines, BenchmarkWidth, BenchmarkHeight, capturePreview);

                samples.Add(result.RenderMilliseconds);
                this.UpdatePreview(result, capturePreview);
                BenchmarkStatistics statistics = BenchmarkStatistics.FromSamples(samples);
                this.statusTextBox.Text = FormatStatusText(backend, result, lineCount, i + 1, iterations, statistics);

                Application.DoEvents();
            }
        }
        catch (Exception ex)
        {
            this.statusTextBox.Text = $"{backend} failed: {ex.Message}";
        }
        finally
        {
            this.Cursor = previousCursor;
        }
    }

    /// <summary>
    /// Lays out the fixed-size preview surface in the middle of the scroll host.
    /// </summary>
    private void LayoutPreview()
    {
        int x = Math.Max(this.previewHost.Padding.Left, (this.previewHost.ClientSize.Width - this.previewBox.Width) / 2);
        int y = Math.Max(this.previewHost.Padding.Top, (this.previewHost.ClientSize.Height - this.previewBox.Height) / 2);
        this.previewBox.Location = new System.Drawing.Point(x, y);
    }

    /// <summary>
    /// Returns the backend currently selected by the user.
    /// </summary>
    private BenchmarkBackend GetSelectedBackend()
        => (this.backendSelector.SelectedItem as string) == "WebGPU"
            ? BenchmarkBackend.WebGpu
            : BenchmarkBackend.Cpu;

    /// <summary>
    /// Replaces the preview image with the final captured frame from the current run.
    /// </summary>
    private void UpdatePreview(BenchmarkRenderResult result, bool capturePreview)
    {
        if (!capturePreview || result.Preview is null)
        {
            return;
        }

        this.previewBox.Image?.Dispose();
        this.previewBox.Image = ToBitmap(result.Preview);
    }

    /// <summary>
    /// Formats one status line describing the current sample, running statistics, and backend outcome.
    /// </summary>
    private static string FormatStatusText(
        BenchmarkBackend backend,
        BenchmarkRenderResult result,
        int lineCount,
        int iteration,
        int totalIterations,
        BenchmarkStatistics statistics)
    {
        string backendStatus = GetBackendStatusText(backend, result);
        string backendReason = backend == BenchmarkBackend.WebGpu
            ? $" | Reason: {result.BackendFailure ?? "none reported"}"
            : string.Empty;

        return
            $"{backend} ({backendStatus}) | Lines: {lineCount:N0} | Render {iteration:N0}/{totalIterations:N0} | " +
            $"Current: {result.RenderMilliseconds:0.000} ms | Mean: {statistics.MeanMilliseconds:0.000} ms | StdDev: {statistics.StdDevMilliseconds:0.000} ms{backendReason}";
    }

    /// <summary>
    /// Converts the backend result into the short status label shown next to the backend name.
    /// </summary>
    private static string GetBackendStatusText(BenchmarkBackend backend, BenchmarkRenderResult result)
    {
        if (result.BackendFailure is not null)
        {
            return $"Failed: {result.BackendFailure}";
        }

        if (result.UsedGpu)
        {
            return "GPU";
        }

        return backend == BenchmarkBackend.WebGpu ? "CPU fallback" : "CPU";
    }

    /// <summary>
    /// Generates the random line set used by one benchmark iteration.
    /// </summary>
    private static LineSpec[] GenerateLines(int lineCount, int width, int height, Random rng)
    {
        LineSpec[] lines = new LineSpec[lineCount];
        for (int i = 0; i < lines.Length; i++)
        {
            lines[i] = new LineSpec(
                new PointF((float)(rng.NextDouble() * width), (float)(rng.NextDouble() * height)),
                new PointF((float)(rng.NextDouble() * width), (float)(rng.NextDouble() * height)),
                Color.FromPixel(new Rgba32(
                    (byte)rng.Next(255),
                    (byte)rng.Next(255),
                    (byte)rng.Next(255),
                    (byte)rng.Next(255))),
                rng.Next(1, 10));
        }

        return lines;
    }

    /// <summary>
    /// Converts the ImageSharp preview image into a WinForms bitmap.
    /// </summary>
    private static Bitmap ToBitmap(Image<Bgra32> image)
    {
        using MemoryStream stream = new();
        image.SaveAsBmp(stream);
        stream.Position = 0;
        using Bitmap decoded = new(stream);
        return new Bitmap(decoded);
    }
}

/// <summary>
/// Running statistics for the render-time samples collected during one benchmark run.
/// </summary>
internal readonly record struct BenchmarkStatistics(double MeanMilliseconds, double StdDevMilliseconds)
{
    /// <summary>
    /// Computes the mean and standard deviation for the current sample window.
    /// </summary>
    public static BenchmarkStatistics FromSamples(IReadOnlyList<double> samples)
    {
        double mean = samples.Average();
        double variance = samples.Sum(x => Math.Pow(x - mean, 2)) / samples.Count;
        double stdDev = Math.Sqrt(variance);
        return new BenchmarkStatistics(mean, stdDev);
    }
}

/// <summary>
/// The benchmark backends exposed by the sample UI.
/// </summary>
internal enum BenchmarkBackend
{
    Cpu,
    WebGpu,
}

/// <summary>
/// One random line draw command used by the benchmark scene.
/// </summary>
internal readonly record struct LineSpec(PointF Start, PointF End, Color Color, float Width);

/// <summary>
/// One completed benchmark render, including timing, optional preview pixels, and backend diagnostics.
/// </summary>
internal sealed class BenchmarkRenderResult : IDisposable
{
    /// <summary>
    /// Initializes a new instance of the <see cref="BenchmarkRenderResult"/> class.
    /// </summary>
    public BenchmarkRenderResult(double renderMilliseconds, Image<Bgra32>? preview, bool usedGpu = false, string? backendFailure = null)
    {
        this.RenderMilliseconds = renderMilliseconds;
        this.Preview = preview;
        this.UsedGpu = usedGpu;
        this.BackendFailure = backendFailure;
    }

    /// <summary>
    /// Gets the elapsed render time for this iteration.
    /// </summary>
    public double RenderMilliseconds { get; }

    /// <summary>
    /// Gets the optional preview image captured for the UI.
    /// </summary>
    public Image<Bgra32>? Preview { get; }

    /// <summary>
    /// Gets a value indicating whether the WebGPU backend completed on the staged GPU path.
    /// </summary>
    public bool UsedGpu { get; }

    /// <summary>
    /// Gets the backend failure or fallback reason, when one was reported.
    /// </summary>
    public string? BackendFailure { get; }

    /// <inheritdoc />
    public void Dispose() => this.Preview?.Dispose();
}

/// <summary>
/// CPU implementation of the benchmark scene used as the baseline backend.
/// </summary>
internal static class CpuRenderer
{
    private static readonly Configuration CpuConfiguration = Configuration.Default.Clone();
    private static readonly Brush BackgroundBrush = Brushes.Solid(Color.ParseHex("#003366"));

    /// <summary>
    /// Renders the benchmark scene through the CPU backend.
    /// </summary>
    public static BenchmarkRenderResult Render(LineSpec[] lines, int width, int height, bool capturePreview)
    {
        using Image<Bgra32> image = new(width, height);
        Buffer2DRegion<Bgra32> region = new(image.Frames.RootFrame.PixelBuffer, image.Bounds);

        Stopwatch stopwatch = Stopwatch.StartNew();
        using (DrawingCanvas<Bgra32> canvas = new(CpuConfiguration, region, new DrawingOptions()))
        {
            DrawScene(canvas, lines);
            canvas.Flush();
        }

        stopwatch.Stop();

        Image<Bgra32>? preview = capturePreview ? image.Clone() : null;
        return new BenchmarkRenderResult(stopwatch.Elapsed.TotalMilliseconds, preview);
    }

    /// <summary>
    /// Draws the shared benchmark scene into the supplied canvas.
    /// </summary>
    public static void DrawScene(DrawingCanvas<Bgra32> canvas, LineSpec[] lines)
    {
        canvas.Fill(BackgroundBrush);
        for (int i = 0; i < lines.Length; i++)
        {
            LineSpec line = lines[i];
            canvas.DrawLine(new SolidPen(line.Color, line.Width), [line.Start, line.End]);
        }
    }
}

/// <summary>
/// Small offscreen WebGPU host used by the sample so the benchmark can drive the real backend without a windowed swapchain.
/// </summary>
internal sealed unsafe class WebGpuOffscreenHost : IDisposable
{
    private readonly WebGPUDrawingBackend backend;
    private readonly Configuration configuration;
    private readonly WebGPU api;
    private Instance* instance;
    private Adapter* adapter;
    private Device* device;
    private Queue* queue;
    private Texture* texture;
    private TextureView* textureView;
    private NativeSurface? surface;
    private Image<Bgra32>? cpuImage;
    private HybridCanvasFrame<Bgra32>? frame;
    private int textureWidth;
    private int textureHeight;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebGpuOffscreenHost"/> class.
    /// </summary>
    public WebGpuOffscreenHost()
    {
        this.backend = new WebGPUDrawingBackend();
        this.configuration = Configuration.Default.Clone();
        this.configuration.SetDrawingBackend(this.backend);

        if (!this.backend.IsSupported)
        {
            this.api = WebGPU.GetApi();
            this.InitializationError = this.backend.DiagnosticLastSceneFailure ?? "WebGPU is not supported on this system.";
            return;
        }

        this.api = WebGPU.GetApi();
        this.InitializeDevice();
    }

    /// <summary>
    /// Gets a value indicating whether the sample can use the WebGPU backend on this machine.
    /// </summary>
    public bool IsSupported => string.IsNullOrEmpty(this.InitializationError);

    /// <summary>
    /// Gets the initialization failure reason when WebGPU is unavailable.
    /// </summary>
    public string? InitializationError { get; private set; }

    /// <summary>
    /// Renders the benchmark scene through the WebGPU backend and optionally captures a readback preview.
    /// </summary>
    public BenchmarkRenderResult Render(LineSpec[] lines, int width, int height, bool capturePreview)
    {
        this.ThrowIfUnavailable();
        this.EnsureRenderTarget(width, height);

        Stopwatch stopwatch = Stopwatch.StartNew();
        using (DrawingCanvas<Bgra32> canvas = new(this.configuration, this.frame!, new DrawingOptions()))
        {
            CpuRenderer.DrawScene(canvas, lines);
            canvas.Flush();
        }

        stopwatch.Stop();

        Image<Bgra32>? preview = null;
        if (capturePreview)
        {
            preview = new Image<Bgra32>(width, height);
            Buffer2DRegion<Bgra32> destination = new(preview.Frames.RootFrame.PixelBuffer, preview.Bounds);
            if (!this.backend.TryReadRegion(this.configuration, this.frame!, new Rectangle(0, 0, width, height), destination))
            {
                preview.Dispose();
                throw new InvalidOperationException("WebGPU readback failed.");
            }
        }

        return new BenchmarkRenderResult(
            stopwatch.Elapsed.TotalMilliseconds,
            preview,
            this.backend.DiagnosticLastFlushUsedGPU,
            this.backend.DiagnosticLastSceneFailure);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        this.ReleaseRenderTarget();
        this.backend.Dispose();

        if (this.device is not null)
        {
            this.api.DeviceRelease(this.device);
        }

        if (this.adapter is not null)
        {
            this.api.AdapterRelease(this.adapter);
        }

        if (this.instance is not null)
        {
            this.api.InstanceRelease(this.instance);
        }

        this.api.Dispose();
    }

    /// <summary>
    /// Creates the WebGPU instance, adapter, device, and queue used by the offscreen benchmark host.
    /// </summary>
    private void InitializeDevice()
    {
        InstanceDescriptor instanceDescriptor = default;
        this.instance = this.api.CreateInstance(&instanceDescriptor);
        if (this.instance is null)
        {
            this.InitializationError = "WebGPU instance creation failed.";
            return;
        }

        if (!TryRequestAdapter(this.api, this.instance, out this.adapter, out string? adapterError))
        {
            this.InitializationError = adapterError;
            return;
        }

        if (!TryRequestDevice(this.api, this.adapter, out this.device, out string? deviceError))
        {
            this.InitializationError = deviceError;
            return;
        }

        this.queue = this.api.DeviceGetQueue(this.device);
        if (this.queue is null)
        {
            this.InitializationError = "WebGPU queue acquisition failed.";
        }
    }

    /// <summary>
    /// Ensures the offscreen render target matches the current benchmark size.
    /// </summary>
    /// <remarks>
    /// The sample keeps a native texture for the real GPU render path and a CPU image only so the
    /// hybrid frame can satisfy the canvas abstraction while previews are read back from the texture.
    /// </remarks>
    private void EnsureRenderTarget(int width, int height)
    {
        if (this.frame is not null && this.textureWidth == width && this.textureHeight == height)
        {
            return;
        }

        this.ReleaseRenderTarget();

        TextureDescriptor descriptor = new()
        {
            Usage = TextureUsage.RenderAttachment | TextureUsage.CopySrc | TextureUsage.CopyDst | TextureUsage.TextureBinding | TextureUsage.StorageBinding,
            Dimension = TextureDimension.Dimension2D,
            Size = new Extent3D((uint)width, (uint)height, 1),
            Format = TextureFormat.Bgra8Unorm,
            MipLevelCount = 1,
            SampleCount = 1,
        };

        this.texture = this.api.DeviceCreateTexture(this.device, in descriptor);
        if (this.texture is null)
        {
            throw new InvalidOperationException("WebGPU texture allocation failed.");
        }

        TextureViewDescriptor viewDescriptor = new()
        {
            Format = TextureFormat.Bgra8Unorm,
            Dimension = TextureViewDimension.Dimension2D,
            BaseMipLevel = 0,
            MipLevelCount = 1,
            BaseArrayLayer = 0,
            ArrayLayerCount = 1,
            Aspect = TextureAspect.All,
        };

        this.textureView = this.api.TextureCreateView(this.texture, in viewDescriptor);
        if (this.textureView is null)
        {
            throw new InvalidOperationException("WebGPU texture view allocation failed.");
        }

        this.surface = WebGPUNativeSurfaceFactory.Create<Bgra32>(
            (nint)this.device,
            (nint)this.queue,
            (nint)this.texture,
            (nint)this.textureView,
            WebGPUTextureFormatId.Bgra8Unorm,
            width,
            height);
        this.cpuImage = new Image<Bgra32>(width, height);
        Buffer2DRegion<Bgra32> cpuRegion = new(this.cpuImage.Frames.RootFrame.PixelBuffer, this.cpuImage.Bounds);
        this.frame = new HybridCanvasFrame<Bgra32>(new Rectangle(0, 0, width, height), cpuRegion, this.surface);
        this.textureWidth = width;
        this.textureHeight = height;
    }

    /// <summary>
    /// Releases the current offscreen texture, view, surface, and CPU-side frame wrapper.
    /// </summary>
    private void ReleaseRenderTarget()
    {
        this.frame = null;
        this.surface = null;
        this.cpuImage?.Dispose();
        this.cpuImage = null;
        this.textureWidth = 0;
        this.textureHeight = 0;

        if (this.textureView is not null)
        {
            this.api.TextureViewRelease(this.textureView);
            this.textureView = null;
        }

        if (this.texture is not null)
        {
            this.api.TextureRelease(this.texture);
            this.texture = null;
        }
    }

    /// <summary>
    /// Throws when the host failed to initialize WebGPU.
    /// </summary>
    private void ThrowIfUnavailable()
    {
        if (!this.IsSupported || this.device is null || this.queue is null)
        {
            throw new InvalidOperationException(this.InitializationError ?? "WebGPU is unavailable.");
        }
    }

    /// <summary>
    /// Requests the high-performance adapter used by the offscreen benchmark host.
    /// </summary>
    private static bool TryRequestAdapter(WebGPU api, Instance* instance, out Adapter* adapter, out string? error)
    {
        RequestAdapterStatus callbackStatus = RequestAdapterStatus.Unknown;
        Adapter* callbackAdapter = null;
        using ManualResetEventSlim callbackReady = new(false);

        void Callback(RequestAdapterStatus status, Adapter* adapterPtr, byte* message, void* userData)
        {
            _ = message;
            _ = userData;
            callbackStatus = status;
            callbackAdapter = adapterPtr;
            callbackReady.Set();
        }

        using PfnRequestAdapterCallback callbackPtr = PfnRequestAdapterCallback.From(Callback);
        RequestAdapterOptions options = new()
        {
            PowerPreference = PowerPreference.HighPerformance,
        };

        api.InstanceRequestAdapter(instance, in options, callbackPtr, null);
        if (!callbackReady.Wait(10_000))
        {
            adapter = null;
            error = "Timed out while waiting for the WebGPU adapter request callback.";
            return false;
        }

        adapter = callbackAdapter;
        if (callbackStatus != RequestAdapterStatus.Success || callbackAdapter is null)
        {
            error = $"WebGPU adapter request failed with status '{callbackStatus}'.";
            return false;
        }

        error = null;
        return true;
    }

    /// <summary>
    /// Requests the logical device used by the offscreen benchmark host.
    /// </summary>
    private static bool TryRequestDevice(WebGPU api, Adapter* adapter, out Device* device, out string? error)
    {
        RequestDeviceStatus callbackStatus = RequestDeviceStatus.Unknown;
        Device* callbackDevice = null;
        using ManualResetEventSlim callbackReady = new(false);

        void Callback(RequestDeviceStatus status, Device* devicePtr, byte* message, void* userData)
        {
            _ = message;
            _ = userData;
            callbackStatus = status;
            callbackDevice = devicePtr;
            callbackReady.Set();
        }

        using PfnRequestDeviceCallback callbackPtr = PfnRequestDeviceCallback.From(Callback);

        Span<FeatureName> requestedFeatures = stackalloc FeatureName[1];
        int requestedCount = 0;
        if (api.AdapterHasFeature(adapter, FeatureName.Bgra8UnormStorage))
        {
            requestedFeatures[requestedCount++] = FeatureName.Bgra8UnormStorage;
        }

        DeviceDescriptor descriptor;
        if (requestedCount > 0)
        {
            fixed (FeatureName* featuresPtr = requestedFeatures)
            {
                descriptor = new DeviceDescriptor
                {
                    RequiredFeatureCount = (uint)requestedCount,
                    RequiredFeatures = featuresPtr,
                };

                api.AdapterRequestDevice(adapter, in descriptor, callbackPtr, null);
            }
        }
        else
        {
            descriptor = default;
            api.AdapterRequestDevice(adapter, in descriptor, callbackPtr, null);
        }

        if (!callbackReady.Wait(10_000))
        {
            device = null;
            error = "Timed out while waiting for the WebGPU device request callback.";
            return false;
        }

        device = callbackDevice;
        if (callbackStatus != RequestDeviceStatus.Success || callbackDevice is null)
        {
            error = $"WebGPU device request failed with status '{callbackStatus}'.";
            return false;
        }

        error = null;
        return true;
    }
}

/// <summary>
/// Small frame wrapper that exposes both a CPU pixel region and a native surface for the sample host.
/// </summary>
/// <remarks>
/// The benchmark backend renders to the native surface. The CPU region exists only to satisfy the
/// canvas frame contract expected by the drawing APIs used in the sample.
/// </remarks>
internal sealed class HybridCanvasFrame<TPixel> : ICanvasFrame<TPixel>
    where TPixel : unmanaged, IPixel<TPixel>
{
    private readonly Buffer2DRegion<TPixel> cpuRegion;
    private readonly NativeSurface surface;

    /// <summary>
    /// Initializes a new instance of the <see cref="HybridCanvasFrame{TPixel}"/> class.
    /// </summary>
    public HybridCanvasFrame(Rectangle bounds, Buffer2DRegion<TPixel> cpuRegion, NativeSurface surface)
    {
        this.Bounds = bounds;
        this.cpuRegion = cpuRegion;
        this.surface = surface;
    }

    /// <inheritdoc />
    public Rectangle Bounds { get; }

    /// <inheritdoc />
    public bool TryGetCpuRegion(out Buffer2DRegion<TPixel> region)
    {
        region = this.cpuRegion;
        return true;
    }

    /// <inheritdoc />
    public bool TryGetNativeSurface([NotNullWhen(true)] out NativeSurface? surface)
    {
        surface = this.surface;
        return true;
    }
}
