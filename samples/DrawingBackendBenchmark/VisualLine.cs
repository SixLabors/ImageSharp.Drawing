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
internal readonly record struct VisualLine(PointF Start, PointF End, Color Color, float Width)
{
    private static readonly Brush BackgroundBrush = Brushes.Solid(Color.ParseHex("#003366"));

    /// <summary>
    /// Gets the pre-converted SkiaSharp color for this line, avoiding per-frame conversion overhead.
    /// </summary>
    public SKColor SkiaColor { get; } = ToSkiaColor(Color);

    /// <summary>
    /// Gets the pre-created ImageSharp pen for this line, avoiding one per-frame allocation in the benchmark hot path.
    /// </summary>
    public SolidPen Pen { get; } = new(Color, Width);

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
