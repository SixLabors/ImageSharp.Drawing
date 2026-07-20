// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Processing.Backends;
using WebGPUExternalSurfaceDemo.Scenes;

namespace WebGPUExternalSurfaceDemo;

/// <summary>
/// Hosts the external-surface sample scenes on tabs that share one WebGPU device session.
/// </summary>
internal sealed class MainForm : Form
{
    private readonly WebGPUSurfaceSession surfaceSession = new();
    private readonly RenderScene[] scenes;

    public MainForm()
    {
        this.Text = "ImageSharp.Drawing WebGPU - External Surface Demo";
        this.ClientSize = new Size(1280, 800);
        this.StartPosition = FormStartPosition.CenterScreen;
        this.BackColor = Color.FromArgb(11, 18, 32);

        this.scenes =
        [
            new ClockScene(),
            new ShaderEffectsScene(),
            new TigerViewerScene(),
            new ApplyReadbackScene(),
            new ManualTextFlowScene(),
            new RichTextEditorScene(),
        ];

        TabControl tabs = new() { Dock = DockStyle.Fill };
        foreach (RenderScene scene in this.scenes)
        {
            // Each scene owns its controls and behavior. The form supplies only the shared
            // device session and a tab in which the complete scene view can be hosted.
            TabPage tab = new(scene.DisplayName);
            tab.Controls.Add(scene.CreateView(this.surfaceSession));
            tabs.TabPages.Add(tab);
        }

        this.Controls.Add(tabs);
    }

    /// <inheritdoc />
    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);

        // Surface creation initializes the shared device while the form is loaded. Scenes
        // can now precompile pipelines without exposing their shader details to the host.
        foreach (RenderScene scene in this.scenes)
        {
            scene.OnHostLoaded(this.surfaceSession.DeviceContext);
        }
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        // Child controls release their surfaces during base disposal; the shared device
        // session must remain alive until every scene surface has been released.
        base.Dispose(disposing);

        if (disposing)
        {
            this.surfaceSession.Dispose();
        }
    }
}
