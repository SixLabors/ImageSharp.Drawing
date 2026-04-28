// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Tests;
using SixLabors.ImageSharp.PixelFormats;
using Brushes = SixLabors.ImageSharp.Drawing.Processing.Brushes;
using Color = SixLabors.ImageSharp.Color;
using PointF = SixLabors.ImageSharp.PointF;
using RectangleF = SixLabors.ImageSharp.RectangleF;
using Size = SixLabors.ImageSharp.Size;
using SizeF = SixLabors.ImageSharp.SizeF;
using SolidBrush = SixLabors.ImageSharp.Drawing.Processing.SolidBrush;

namespace WebGPUExternalSurfaceDemo.Scenes;

/// <summary>
/// Loads the Ghostscript Tiger SVG via the shared <see cref="SvgBenchmarkHelper"/> and renders it with
/// drag-to-pan and wheel-to-zoom. Validates transform handling, curves, and fill+stroke quality at arbitrary zoom.
/// The parsed SVG geometry is reused every frame; only the canvas transform changes while interacting.
/// </summary>
internal sealed class TigerViewerScene : RenderScene
{
    private static readonly Color BackgroundColor = Color.ParseHex("#0B1220");

    private readonly List<(IPath Path, SolidBrush? Fill, SolidPen? Stroke)> elements;
    private readonly RectangleF sceneBounds;

    private Vector2 panXY;
    private float zoom = 1f;
    private bool needsInitialFit = true;
    private Size lastViewportSize;
    private PointF? lastDragPoint;
    private PointF lastMouseDevice;

    public TigerViewerScene()
    {
        // Parse and build the SVG once. The draw loop below reuses the built paths and brushes so pan/zoom
        // interaction measures rendering and transform work, not repeated SVG parsing.
        string svgPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "Ghostscript_Tiger.svg");
        List<SvgBenchmarkHelper.SvgElement> parsed = SvgBenchmarkHelper.ParseSvg(svgPath);
        List<(IPath Path, SolidBrush? Fill, SolidPen? Stroke)> built = [];
        foreach ((IPath p, SolidBrush? f, SolidPen? s) in SvgBenchmarkHelper.BuildImageSharpElements(parsed, scale: 1f))
        {
            built.Add((p, f, s));
        }

        this.elements = built;
        this.sceneBounds = ComputeBounds(built);
    }

    public override string DisplayName => "Tiger Viewer";

    /// <summary>
    /// Gets a multi-line diagnostic string describing the current zoom, pan, and mouse-in-world values
    /// so the host can overlay it for debugging.
    /// </summary>
    public string StatusText
    {
        get
        {
            SizeF center = this.lastViewportSize / 2f;
            Vector2 cursorFromCenter = new(this.lastMouseDevice.X - center.Width, this.lastMouseDevice.Y - center.Height);
            Vector2 world = this.panXY + (cursorFromCenter / this.zoom);
            return
                $"zoom : {this.zoom:0.####}×\n" +
                $"pan  : ({this.panXY.X:0.##}, {this.panXY.Y:0.##})\n" +
                $"mouse: device=({this.lastMouseDevice.X:0},{this.lastMouseDevice.Y:0}) world=({world.X:0.##},{world.Y:0.##})";
        }
    }

    public override void Paint(DrawingCanvas<Bgra32> canvas, TimeSpan deltaTime)
    {
        Size viewportSize = canvas.Bounds.Size;

        if (this.needsInitialFit || viewportSize != this.lastViewportSize)
        {
            // The first frame and every resize recenter the artwork. User pan/zoom then operates from
            // that fitted world-space view.
            this.FitToView(viewportSize);
            this.needsInitialFit = false;
        }

        this.lastViewportSize = viewportSize;

        canvas.Fill(Brushes.Solid(BackgroundColor), canvas.Bounds);

        SizeF screenCenter = viewportSize / 2f;

        // The tiger paths remain in SVG/world coordinates. The canvas transform maps them into
        // framebuffer pixels each frame, so pan and zoom are cheap matrix changes.
        Matrix4x4 worldToScreen =
            Matrix4x4.CreateTranslation(new Vector3(-this.panXY, 0)) *
            Matrix4x4.CreateScale(this.zoom, this.zoom, 1f) *
            Matrix4x4.CreateTranslation(screenCenter.Width, screenCenter.Height, 0);

        DrawingOptions options = new() { Transform = worldToScreen };
        canvas.Save(options);

        // Save/Restore confines the world transform to the SVG draw calls. Any UI overlay drawn later can
        // stay in device coordinates without undoing the matrix manually.
        foreach ((IPath path, SolidBrush? fill, SolidPen? stroke) in this.elements)
        {
            if (fill is not null)
            {
                canvas.Fill(fill, path);
            }

            if (stroke is not null)
            {
                canvas.Draw(stroke, path);
            }
        }

        canvas.Restore();
    }

    public override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            this.lastDragPoint = new PointF(e.X, e.Y);
        }
    }

    public override void OnMouseMove(MouseEventArgs e)
    {
        this.lastMouseDevice = new PointF(e.X, e.Y);
        if (this.lastDragPoint is PointF last && (e.Button & MouseButtons.Left) != 0)
        {
            // Mouse movement arrives in device pixels. Dividing by zoom converts the drag distance back
            // into world units so pan speed is stable at every zoom level.
            this.panXY -= (new Vector2(e.X, e.Y) - (Vector2)last) / this.zoom;
            this.lastDragPoint = new PointF(e.X, e.Y);
        }
    }

    public override void OnMouseUp(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            this.lastDragPoint = null;
        }
    }

    public override void OnMouseWheel(MouseEventArgs e)
    {
        if (this.lastViewportSize.Width <= 0 || this.lastViewportSize.Height <= 0)
        {
            return;
        }

        SizeF screenCenter = this.lastViewportSize * .5f;
        Vector2 cursorFromCenter = new(e.X - screenCenter.Width, e.Y - screenCenter.Height);

        // Zoom around the cursor by preserving the world-space point beneath it before and after scaling.
        Vector2 worldUnderCursor = this.panXY + (cursorFromCenter / this.zoom);

        float factor = e.Delta > 0 ? 1.15f : 1f / 1.15f;
        this.zoom = Math.Clamp(this.zoom * factor, 0.1f, 200f);

        this.panXY = worldUnderCursor - (cursorFromCenter / this.zoom);
    }

    private void FitToView(Size viewportSize)
    {
        if (viewportSize.Width <= 0 || viewportSize.Height <= 0 || this.sceneBounds.Width <= 0 || this.sceneBounds.Height <= 0)
        {
            return;
        }

        float padding = 40f;
        float zoomX = (viewportSize.Width - padding) / this.sceneBounds.Width;
        float zoomY = (viewportSize.Height - padding) / this.sceneBounds.Height;
        this.zoom = MathF.Max(0.1f, MathF.Min(zoomX, zoomY));

        // panXY stores the world coordinate placed at the center of the framebuffer.
        this.panXY = new Vector2(
            this.sceneBounds.X + (this.sceneBounds.Width * 0.5f),
            this.sceneBounds.Y + (this.sceneBounds.Height * 0.5f));
    }

    private static RectangleF ComputeBounds(List<(IPath Path, SolidBrush? Fill, SolidPen? Stroke)> items)
    {
        if (items.Count == 0)
        {
            return new RectangleF(0, 0, 200, 200);
        }

        // The bounds are used only for initial fit-to-view; rendering still uses the original paths.
        RectangleF union = items[0].Path.Bounds;
        for (int i = 1; i < items.Count; i++)
        {
            union = RectangleF.Union(union, items[i].Path.Bounds);
        }

        return union;
    }
}
