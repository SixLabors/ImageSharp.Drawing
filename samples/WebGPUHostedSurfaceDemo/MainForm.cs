// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using WebGPUHostedSurfaceDemo.Controls;
using WebGPUHostedSurfaceDemo.Scenes;

namespace WebGPUHostedSurfaceDemo;

/// <summary>
/// Main window for the sample. A tab control switches between two demo scenes, each hosted by its own
/// <see cref="WebGPURenderControl"/> instance. This demonstrates that multiple independent hosted surfaces
/// can coexist in the same WinForms process without managing surfaces or swapchains in user code.
/// </summary>
internal sealed class MainForm : Form
{
    private readonly WebGPURenderControl clockControl;
    private readonly WebGPURenderControl tigerControl;
    private readonly ClockScene clockScene;
    private readonly TigerViewerScene tigerScene;
    private readonly Label tigerStatusLabel;

    public MainForm()
    {
        this.Text = "ImageSharp.Drawing WebGPU - Hosted Surface Demo";
        this.ClientSize = new Size(1280, 800);
        this.StartPosition = FormStartPosition.CenterScreen;
        this.BackColor = Color.FromArgb(11, 18, 32);

        this.clockScene = new ClockScene();
        this.tigerScene = new TigerViewerScene();

        this.clockControl = new WebGPURenderControl { Dock = DockStyle.Fill };
        this.clockControl.PaintFrame += this.OnPaintClock;

        this.tigerControl = new WebGPURenderControl { Dock = DockStyle.Fill };
        this.tigerControl.PaintFrame += this.OnPaintTiger;
        this.tigerStatusLabel = new Label
        {
            AutoSize = false,
            Size = new Size(350, 100),
            BackColor = Color.FromArgb(160, 0, 0, 0),
            ForeColor = Color.White,
            Font = new Font(FontFamily.GenericMonospace, 9f),
            Padding = new Padding(6),
            Location = new Point(6, 6),
            Text = string.Empty,
        };
        this.tigerControl.Controls.Add(this.tigerStatusLabel);
        this.tigerControl.MouseDown += (_, e) =>
        {
            this.tigerScene.OnMouseDown(e);
            this.tigerControl.Invalidate();
        };
        this.tigerControl.MouseMove += (_, e) =>
        {
            this.tigerScene.OnMouseMove(e);
            this.tigerStatusLabel.Text = this.tigerScene.StatusText;
            this.tigerControl.Invalidate();
        };
        this.tigerControl.MouseUp += (_, e) =>
        {
            this.tigerScene.OnMouseUp(e);
            this.tigerControl.Invalidate();
        };
        this.tigerControl.MouseWheel += (_, e) =>
        {
            this.tigerScene.OnMouseWheel(e);
            this.tigerStatusLabel.Text = this.tigerScene.StatusText;
            this.tigerControl.Invalidate();
        };

        TabControl tabs = new() { Dock = DockStyle.Fill };

        TabPage clockTab = new(this.clockScene.DisplayName);
        clockTab.Controls.Add(this.clockControl);
        tabs.TabPages.Add(clockTab);

        TabPage tigerTab = new(this.tigerScene.DisplayName);
        tigerTab.Controls.Add(this.tigerControl);
        tabs.TabPages.Add(tigerTab);

        this.Controls.Add(tabs);
    }

    private void OnPaintClock(DrawingCanvas<Bgra32> canvas, TimeSpan delta)
    {
        Size s = this.clockControl.FramebufferSize;
        this.clockScene.Paint(canvas, new SixLabors.ImageSharp.Size(s.Width, s.Height), delta);
    }

    private void OnPaintTiger(DrawingCanvas<Bgra32> canvas, TimeSpan delta)
    {
        Size s = this.tigerControl.FramebufferSize;
        this.tigerScene.Paint(canvas, new SixLabors.ImageSharp.Size(s.Width, s.Height), delta);
        this.tigerStatusLabel.Text = this.tigerScene.StatusText;
    }
}
