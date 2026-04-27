// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SkiaSharp;
using Brush = SixLabors.ImageSharp.Drawing.Processing.Brush;
using Brushes = SixLabors.ImageSharp.Drawing.Processing.Brushes;
using Color = SixLabors.ImageSharp.Color;
using PointF = SixLabors.ImageSharp.PointF;

namespace DrawingBackendBenchmark;

/// <summary>
/// One random line draw command used by the benchmark scene.
/// </summary>
internal readonly struct VisualLine
{
    private static readonly Brush BackgroundBrush = Brushes.Solid(Color.ParseHex("#003366"));

    /// <summary>
    /// Initializes a new instance of the <see cref="VisualLine"/> struct.
    /// </summary>
    public VisualLine(PointF start, PointF end, Color color, float width)
    {
        this.Start = start;
        this.End = end;
        this.Color = color;
        this.Width = width;
        this.SkiaColor = ToSkiaColor(color);
        this.Pen = new SolidPen(color, width);
    }

    /// <summary>
    /// Gets the line start point.
    /// </summary>
    public PointF Start { get; }

    /// <summary>
    /// Gets the line end point.
    /// </summary>
    public PointF End { get; }

    /// <summary>
    /// Gets the line color.
    /// </summary>
    public Color Color { get; }

    /// <summary>
    /// Gets the line width.
    /// </summary>
    public float Width { get; }

    /// <summary>
    /// Gets the pre-converted SkiaSharp color for this line, avoiding per-frame conversion overhead.
    /// </summary>
    public SKColor SkiaColor { get; }

    /// <summary>
    /// Gets the pre-created ImageSharp pen for this line, avoiding one per-frame allocation in the benchmark hot path.
    /// </summary>
    public SolidPen Pen { get; }

    private static SKColor ToSkiaColor(Color color)
    {
        Rgba32 rgba = color.ToPixel<Rgba32>();
        return new SKColor(rgba.R, rgba.G, rgba.B, rgba.A);
    }

    /// <summary>
    /// Draws the shared benchmark scene into the supplied canvas.
    /// </summary>
    public static void RenderLinesToCanvas(DrawingCanvas<Bgra32> canvas, ReadOnlySpan<VisualLine> lines)
    {
        canvas.Restore();
        canvas.Fill(BackgroundBrush);
        foreach (VisualLine visualLine in lines)
        {
            canvas.DrawLine(visualLine.Pen, visualLine.Start, visualLine.End);
        }
    }
}
