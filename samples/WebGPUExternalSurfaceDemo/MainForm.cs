// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using WebGPUExternalSurfaceDemo.Controls;
using WebGPUExternalSurfaceDemo.Scenes;

namespace WebGPUExternalSurfaceDemo;

/// <summary>
/// Main window for the sample. A tab control switches between demo scenes, each displayed in its own
/// <see cref="WebGPURenderControl"/> instance. This demonstrates that multiple independent external surfaces
/// can coexist in the same WinForms process without managing surfaces or swapchains in user code.
/// </summary>
internal sealed class MainForm : Form
{
    private readonly WebGPURenderControl clockControl;
    private readonly WebGPURenderControl tigerControl;
    private readonly WebGPURenderControl applyControl;
    private readonly ClockScene clockScene;
    private readonly TigerViewerScene tigerScene;
    private readonly ApplyReadbackScene applyScene;
    private readonly Label tigerStatusLabel;

    public MainForm()
    {
        this.Text = "ImageSharp.Drawing WebGPU - External Surface Demo";
        this.ClientSize = new Size(1280, 800);
        this.StartPosition = FormStartPosition.CenterScreen;
        this.BackColor = Color.FromArgb(11, 18, 32);

        this.clockScene = new ClockScene();
        this.tigerScene = new TigerViewerScene();
        this.applyScene = new ApplyReadbackScene();

        // Each tab gets its own render control and external surface. This mirrors real UI applications where
        // separate controls or panels own their own native drawable areas.
        this.clockControl = new WebGPURenderControl { Dock = DockStyle.Fill };
        this.clockControl.PaintFrame += this.OnPaintClock;

        this.tigerControl = new WebGPURenderControl { Dock = DockStyle.Fill };
        this.tigerControl.PaintFrame += this.OnPaintTiger;

        this.applyControl = new WebGPURenderControl { Dock = DockStyle.Fill };
        this.applyControl.PaintFrame += this.OnPaintApply;

        // The Apply scene reacts to pointer movement so readback cost can be assessed interactively.
        this.applyControl.MouseDown += (_, e) =>
        {
            this.applyScene.OnMouseDown(e);
            this.applyControl.Invalidate();
        };
        this.applyControl.MouseMove += (_, e) =>
        {
            this.applyScene.OnMouseMove(e);
            this.applyControl.Invalidate();
        };
        this.applyControl.MouseWheel += (_, e) =>
        {
            this.applyScene.OnMouseWheel(e);
            this.applyControl.Invalidate();
        };

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

        // Mouse input stays in WinForms coordinates. The scene converts it into its own world transform.
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

        TabPage applyTab = new(this.applyScene.DisplayName);
        applyTab.Controls.Add(this.applyControl);
        tabs.TabPages.Add(applyTab);

        this.Controls.Add(tabs);
    }

    private void OnPaintClock(DrawingCanvas canvas, TimeSpan delta)
        => this.clockScene.Paint(canvas, delta);

    private void OnPaintTiger(DrawingCanvas canvas, TimeSpan delta)
    {
        this.tigerScene.Paint(canvas, delta);
        this.tigerStatusLabel.Text = this.tigerScene.StatusText;
    }

    private void OnPaintApply(DrawingCanvas canvas, TimeSpan delta)
        => this.applyScene.Paint(canvas, delta);
}
