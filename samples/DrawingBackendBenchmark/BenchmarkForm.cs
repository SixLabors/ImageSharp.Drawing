// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SkiaSharp.Views.Desktop;
using Color = SixLabors.ImageSharp.Color;
using PointF = SixLabors.ImageSharp.PointF;

namespace DrawingBackendBenchmark;

/// <summary>
/// Interactive benchmark window for comparing the CPU and WebGPU drawing backends.
/// </summary>
internal sealed class BenchmarkForm : Form
{
    private readonly ComboBox backendSelector;
    private readonly NumericUpDown iterationSelector;
    private readonly TextBox statusTextBox;
    private readonly Panel previewHost;
    private readonly PictureBox previewBox;
    private readonly SKGLControl glControl;
    private int lastLineCount;

    /// <summary>
    /// Initializes a new instance of the <see cref="BenchmarkForm"/> class.
    /// </summary>
    public BenchmarkForm()
    {
        this.Text = "Drawing Backend Benchmark";
        this.ClientSize = new System.Drawing.Size(1600, 1200);
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
        toolbar.Controls.Add(this.CreateRunButton("200k", 200_000));

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
            BackColor = System.Drawing.Color.FromArgb(50, 36, 56),
            Padding = new Padding(12),
        };

        this.previewBox = new PictureBox
        {
            Dock = DockStyle.Fill,
            BackColor = System.Drawing.Color.FromArgb(24, 36, 56),
            SizeMode = PictureBoxSizeMode.Normal
        };
        this.previewHost.Controls.Add(this.previewBox);

        this.Controls.Add(this.previewHost);
        this.Controls.Add(this.statusTextBox);
        this.Controls.Add(toolbar);

        // Fake 1x1 SKGLControl to create a GRContext
        this.glControl = new SKGLControl
        {
            Size = new System.Drawing.Size(1, 1),
            Visible = true,
        };
        this.Controls.Add(this.glControl);

        // Initialize backends
        this.backendSelector.Items.Add(new CpuBenchmarkBackend());
        this.backendSelector.Items.Add(new SkiaSharpBenchmarkBackend());

        if (WebGpuBenchmarkBackend.TryCreate(out WebGpuBenchmarkBackend? webGpuBackend, out string? error))
        {
            this.backendSelector.Items.Add(webGpuBackend);
        }
        else
        {
            this.statusTextBox.Text = $"WebGPU unavailable: {error}";
        }

        this.glControl.PaintSurface += (_, _) =>
        {
            bool hasGpuBackend = this.backendSelector.Items.OfType<SkiaSharpBenchmarkBackend>().Any(b => b.IsGpu);
            if (!hasGpuBackend)
            {
                this.backendSelector.Items.Add(new SkiaSharpBenchmarkBackend(this.glControl.GRContext));
            }
        };

        this.backendSelector.SelectedIndexChanged += (_, _) =>
        {
            if (this.lastLineCount > 0)
            {
                this.RunBenchmark(this.lastLineCount);
            }
        };

        if (this.backendSelector.Items.Count > 0)
        {
            this.backendSelector.SelectedIndex = 0;
        }

        this.Shown += (_, _) => this.backendSelector.Focus();
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            this.previewBox.Image?.Dispose();
            foreach (IDisposable backend in this.backendSelector.Items.OfType<IDisposable>())
            {
                backend.Dispose();
            }
        }

        base.Dispose(disposing);
    }

    private int BenchmarkWidth => this.previewBox.Width;

    private int BenchmarkHeight => this.previewBox.Height;

    /// <summary>
    /// Creates one toolbar button that runs the benchmark with the requested line count.
    /// </summary>
    private RadioButton CreateRunButton(string text, int lineCount)
    {
        RadioButton button = new()
        {
            AutoSize = true,
            Text = text,
            Appearance = Appearance.Button,
            Margin = new Padding(12, 0, 0, 0),
        };
        button.Click += (_, _) =>
        {
            this.lastLineCount = lineCount;
            this.RunBenchmark(lineCount);
        };
        return button;
    }

    /// <summary>
    /// Executes one benchmark run for the selected backend and updates the preview and status text.
    /// </summary>
    private void RunBenchmark(int lineCount)
    {
        int iterations = (int)this.iterationSelector.Value;

        if (this.backendSelector.SelectedItem is not IBenchmarkBackend backend)
        {
            return;
        }

        Random rng = new(0);
        List<double> samples = new(iterations);

        Cursor previousCursor = this.Cursor;
        this.Cursor = Cursors.WaitCursor;
        VisualLine[] lines = GenerateLines(lineCount, this.BenchmarkWidth, this.BenchmarkHeight, rng);

        try
        {
            for (int i = 0; i < iterations; i++)
            {
                bool capturePreview = i == iterations - 1;
                using BenchmarkRenderResult result = backend.Render(lines, this.BenchmarkWidth, this.BenchmarkHeight, capturePreview);

                samples.Add(result.RenderMilliseconds);
                this.UpdatePreview(result, capturePreview);
                BenchmarkStatistics statistics = BenchmarkStatistics.FromSamples(samples);
                this.statusTextBox.Text = FormatStatusText(backend.ToString(), result, lineCount, i + 1, iterations, statistics);

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
        string? backendName,
        BenchmarkRenderResult result,
        int lineCount,
        int iteration,
        int totalIterations,
        BenchmarkStatistics statistics)
    {
        string backendStatus = GetBackendStatusText(backendName ?? string.Empty, result);
        string backendFailure = result.BackendFailure is not null ? $" | {result.BackendFailure}" : string.Empty;

        return
            $"{backendName} ({backendStatus}) | Lines: {lineCount:N0} | Render {iteration:N0}/{totalIterations:N0} | " +
            $"Current: {result.RenderMilliseconds:0.000} ms | Mean: {statistics.MeanMilliseconds:0.000} ms | StdDev: {statistics.StdDevMilliseconds:0.000} ms{backendFailure}";
    }

    /// <summary>
    /// Converts the backend result into the short status label shown next to the backend name.
    /// </summary>
    private static string GetBackendStatusText(string backendName, BenchmarkRenderResult result)
    {
        if (result.BackendFailure is not null)
        {
            return $"Failed: {result.BackendFailure}";
        }

        return backendName;
    }

    /// <summary>
    /// Generates the random line set used by one benchmark iteration.
    /// </summary>
    private static VisualLine[] GenerateLines(int lineCount, int width, int height, Random rng)
    {
        VisualLine[] lines = new VisualLine[lineCount];
        for (int i = 0; i < lines.Length; i++)
        {
            lines[i] = new VisualLine(
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
