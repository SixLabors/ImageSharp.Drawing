// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Processing;
using WebGPUExternalSurfaceDemo.Controls;
using WebGPUExternalSurfaceDemo.Scenes;
using ImageSharpColor = SixLabors.ImageSharp.Color;

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
    private readonly WebGPURenderControl manualTextFlowControl;
    private readonly WebGPURenderControl richTextControl;
    private readonly ClockScene clockScene;
    private readonly TigerViewerScene tigerScene;
    private readonly ApplyReadbackScene applyScene;
    private readonly ManualTextFlowScene manualTextFlowScene;
    private readonly RichTextEditorScene richTextScene;
    private readonly Label tigerStatusLabel;
    private readonly ComboBox manualTextShapeComboBox;
    private readonly CheckBox boldButton;
    private readonly CheckBox italicButton;
    private readonly CheckBox underlineButton;
    private readonly CheckBox strikeoutButton;
    private readonly ComboBox fontFamilyComboBox;
    private readonly Label fontSizeLabel;
    private readonly Label selectionStatusLabel;

    public MainForm()
    {
        this.Text = "ImageSharp.Drawing WebGPU - External Surface Demo";
        this.ClientSize = new Size(1280, 800);
        this.StartPosition = FormStartPosition.CenterScreen;
        this.BackColor = Color.FromArgb(11, 18, 32);

        this.clockScene = new ClockScene();
        this.tigerScene = new TigerViewerScene();
        this.applyScene = new ApplyReadbackScene();
        this.manualTextFlowScene = new ManualTextFlowScene();
        this.richTextScene = new RichTextEditorScene();

        // Each tab gets its own render control and external surface. This mirrors real UI applications where
        // separate controls or panels own their own native drawable areas.
        this.clockControl = new WebGPURenderControl { Dock = DockStyle.Fill, RenderMode = WebGPURenderMode.Continuous };
        this.clockControl.PaintFrame += this.OnPaintClock;

        this.tigerControl = new WebGPURenderControl { Dock = DockStyle.Fill };
        this.tigerControl.PaintFrame += this.OnPaintTiger;

        this.applyControl = new WebGPURenderControl { Dock = DockStyle.Fill };
        this.applyControl.PaintFrame += this.OnPaintApply;

        this.manualTextFlowControl = new WebGPURenderControl { Dock = DockStyle.Fill };
        this.manualTextFlowControl.PaintFrame += this.OnPaintManualTextFlow;

        this.richTextControl = new WebGPURenderControl { Dock = DockStyle.Fill, TabStop = true };
        this.richTextControl.PaintFrame += this.OnPaintRichText;

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

        // The manual flow scene is intentionally controlled from ordinary WinForms
        // widgets. Changing this dropdown swaps the obstacle path, while the scene
        // keeps using the same retained TextBlock and per-line layout enumerator.
        this.manualTextShapeComboBox = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 140,
            Margin = new Padding(0, 0, 8, 0),
        };

        foreach (ManualTextFlowObstacleShape shape in Enum.GetValues<ManualTextFlowObstacleShape>())
        {
            this.manualTextShapeComboBox.Items.Add(shape);
        }

        this.manualTextShapeComboBox.SelectedItem = this.manualTextFlowScene.ObstacleShape;
        this.manualTextShapeComboBox.SelectedIndexChanged += (_, _) =>
        {
            if (this.manualTextShapeComboBox.SelectedItem is ManualTextFlowObstacleShape shape)
            {
                this.manualTextFlowScene.ObstacleShape = shape;
                this.manualTextFlowControl.Invalidate();
            }
        };

        FlowLayoutPanel manualTextFlowToolbar = new()
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            Padding = new Padding(8),
        };

        manualTextFlowToolbar.Controls.Add(new Label
        {
            AutoSize = true,
            Margin = new Padding(0, 5, 8, 0),
            Text = "Shape",
        });
        manualTextFlowToolbar.Controls.Add(this.manualTextShapeComboBox);

        Panel manualTextFlowPanel = new() { Dock = DockStyle.Fill };
        manualTextFlowPanel.Controls.Add(this.manualTextFlowControl);
        manualTextFlowPanel.Controls.Add(manualTextFlowToolbar);

        this.boldButton = this.CreateEditorToggleButton("B", () => this.richTextScene.ToggleBold());
        this.italicButton = this.CreateEditorToggleButton("I", () => this.richTextScene.ToggleItalic());
        this.underlineButton = this.CreateEditorToggleButton("U", () => this.richTextScene.ToggleUnderline());
        this.strikeoutButton = this.CreateEditorToggleButton("S", () => this.richTextScene.ToggleStrikeout());
        this.fontFamilyComboBox = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 240,
            Margin = new Padding(0, 0, 8, 0),
        };

        foreach (string name in SixLabors.Fonts.SystemFonts.Collection.Families.Select(x => x.Name).Order())
        {
            this.fontFamilyComboBox.Items.Add(name);
        }

        this.fontFamilyComboBox.SelectedItem = this.richTextScene.FontFamilyName;
        this.fontFamilyComboBox.SelectedIndexChanged += (_, _) =>
        {
            if (this.fontFamilyComboBox.SelectedItem is string name)
            {
                this.richTextScene.SetFontFamily(name);
                this.richTextControl.Focus();
                this.richTextControl.Invalidate();
            }
        };

        this.selectionStatusLabel = new Label
        {
            AutoSize = true,
            Margin = new Padding(10, 5, 0, 0),
        };
        this.fontSizeLabel = new Label
        {
            AutoSize = true,
            Margin = new Padding(0, 5, 8, 0),
        };

        FlowLayoutPanel editorToolbar = new()
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            Padding = new Padding(8),
        };

        editorToolbar.Controls.Add(this.fontFamilyComboBox);
        editorToolbar.Controls.Add(this.boldButton);
        editorToolbar.Controls.Add(this.italicButton);
        editorToolbar.Controls.Add(this.underlineButton);
        editorToolbar.Controls.Add(this.strikeoutButton);
        editorToolbar.Controls.Add(this.CreateEditorButton("A-", () => this.richTextScene.ChangeFontSize(-2F)));
        editorToolbar.Controls.Add(this.fontSizeLabel);
        editorToolbar.Controls.Add(this.CreateEditorButton("A+", () => this.richTextScene.ChangeFontSize(2F)));
        editorToolbar.Controls.Add(this.CreateEditorColorButton(Color.Black, ImageSharpColor.ParseHex("#17212B")));
        editorToolbar.Controls.Add(this.CreateEditorColorButton(Color.RoyalBlue, ImageSharpColor.ParseHex("#145DA0")));
        editorToolbar.Controls.Add(this.CreateEditorColorButton(Color.Firebrick, ImageSharpColor.ParseHex("#B33A3A")));
        editorToolbar.Controls.Add(this.CreateEditorColorButton(Color.SeaGreen, ImageSharpColor.ParseHex("#2B7A4B")));
        editorToolbar.Controls.Add(this.selectionStatusLabel);

        Panel richTextPanel = new() { Dock = DockStyle.Fill };
        richTextPanel.Controls.Add(this.richTextControl);
        richTextPanel.Controls.Add(editorToolbar);

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

        // The manual text-flow scene keeps prepared text static and lets the mouse
        // move only the obstacle that determines each per-line wrapping width.
        this.manualTextFlowControl.MouseDown += (_, e) =>
        {
            this.manualTextFlowScene.OnMouseDown(e);
            this.manualTextFlowControl.Invalidate();
        };
        this.manualTextFlowControl.MouseMove += (_, e) =>
        {
            this.manualTextFlowScene.OnMouseMove(e);
            this.manualTextFlowControl.Invalidate();
        };

        this.richTextControl.MouseDown += (_, e) =>
        {
            this.richTextControl.Focus();
            if (e.Button == MouseButtons.Left)
            {
                this.richTextControl.Capture = true;
            }

            this.richTextScene.OnMouseDown(e);
            this.UpdateEditorToolbar();
            this.richTextControl.Invalidate();
        };

        this.richTextControl.MouseMove += (_, e) =>
        {
            this.richTextScene.OnMouseMove(e);
            this.UpdateEditorToolbar();
            this.richTextControl.Invalidate();
        };

        this.richTextControl.MouseUp += (_, e) =>
        {
            this.richTextScene.OnMouseUp(e);
            if (e.Button == MouseButtons.Left)
            {
                this.richTextControl.Capture = false;
            }

            this.UpdateEditorToolbar();
            this.richTextControl.Invalidate();
        };

        this.richTextControl.PreviewKeyDown += (_, e) =>
        {
            e.IsInputKey = e.KeyCode is Keys.Left or Keys.Right or Keys.Up or Keys.Down or Keys.Home or Keys.End;
        };

        this.richTextControl.KeyDown += this.OnRichTextKeyDown;
        this.richTextControl.KeyPress += this.OnRichTextKeyPress;

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

        TabPage manualTextFlowTab = new(this.manualTextFlowScene.DisplayName);
        manualTextFlowTab.Controls.Add(manualTextFlowPanel);
        tabs.TabPages.Add(manualTextFlowTab);

        TabPage richTextTab = new(this.richTextScene.DisplayName);
        richTextTab.Controls.Add(richTextPanel);
        tabs.TabPages.Add(richTextTab);

        this.Controls.Add(tabs);
        this.UpdateEditorToolbar();
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

    private void OnPaintManualTextFlow(DrawingCanvas canvas, TimeSpan delta)
        => this.manualTextFlowScene.Paint(canvas, delta);

    private void OnPaintRichText(DrawingCanvas canvas, TimeSpan delta)
        => this.richTextScene.Paint(canvas, delta);

    private void OnRichTextKeyDown(object? sender, KeyEventArgs e)
    {
        if (!this.richTextScene.OnKeyDown(e))
        {
            return;
        }

        e.SuppressKeyPress = true;
        this.UpdateEditorToolbar();
        this.richTextControl.Invalidate();
    }

    private void OnRichTextKeyPress(object? sender, KeyPressEventArgs e)
    {
        if (!this.richTextScene.OnKeyPress(e.KeyChar))
        {
            return;
        }

        e.Handled = true;
        this.UpdateEditorToolbar();
        this.richTextControl.Invalidate();
    }

    private CheckBox CreateEditorToggleButton(string text, Action action)
    {
        CheckBox button = new()
        {
            Appearance = Appearance.Button,
            AutoSize = true,
            Text = text,
            Font = new Font(FontFamily.GenericSansSerif, 9F, FontStyle.Bold),
            Margin = new Padding(0, 0, 6, 0),
        };

        button.Click += (_, _) => this.InvokeEditorCommand(action);
        return button;
    }

    private Button CreateEditorButton(string text, Action action)
    {
        Button button = new()
        {
            AutoSize = true,
            Text = text,
            Margin = new Padding(0, 0, 6, 0),
        };

        button.Click += (_, _) => this.InvokeEditorCommand(action);
        return button;
    }

    private Button CreateEditorColorButton(Color color, ImageSharpColor textColor)
    {
        Button button = new()
        {
            BackColor = color,
            FlatStyle = FlatStyle.Flat,
            Margin = new Padding(0, 1, 6, 0),
            Size = new Size(28, 24),
            UseVisualStyleBackColor = false,
        };

        button.Click += (_, _) => this.InvokeEditorCommand(() => this.richTextScene.SetFillColor(textColor));
        return button;
    }

    private void InvokeEditorCommand(Action action)
    {
        action();
        this.UpdateEditorToolbar();
        this.richTextControl.Focus();
        this.richTextControl.Invalidate();
    }

    private void UpdateEditorToolbar()
    {
        this.boldButton.Checked = this.richTextScene.IsBold;
        this.italicButton.Checked = this.richTextScene.IsItalic;
        this.underlineButton.Checked = this.richTextScene.IsUnderline;
        this.strikeoutButton.Checked = this.richTextScene.IsStrikeout;
        this.fontSizeLabel.Text = $"{this.richTextScene.CurrentFontSize:0.#} pt";
        this.selectionStatusLabel.Text = $"Selected: {this.richTextScene.SelectionLength}";
    }
}
