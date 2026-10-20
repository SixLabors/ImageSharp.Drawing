// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using SixLabors.Fonts;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The metrics of one shaped text line and the origin the layout gives it. The text block of the line shapes it one time, so the layout and the draw use
/// these values and do not measure the line again.
/// </summary>
internal struct MeasuredLine
{
    /// <summary>
    /// Initializes a new instance of the <see cref="MeasuredLine"/> struct.
    /// </summary>
    /// <param name="advance">The advance of the line, from <see cref="TextMetrics.Advance"/>.</param>
    /// <param name="bounds">The bounds of the line, from <see cref="TextMetrics.Bounds"/>.</param>
    /// <param name="renderableBounds">The renderable bounds of the line, from <see cref="TextMetrics.RenderableBounds"/>.</param>
    /// <param name="baseline">The baseline of the first line, from <see cref="LineMetrics.Baseline"/>, or 0 when the layout does not use it.</param>
    /// <param name="lineHeight">The line height of the first line, from <see cref="LineMetrics.LineHeight"/>, or 0 when the layout does not use it.</param>
    public MeasuredLine(FontRectangle advance, FontRectangle bounds, FontRectangle renderableBounds, float baseline, float lineHeight)
    {
        this.Advance = advance;
        this.Bounds = bounds;
        this.RenderableBounds = renderableBounds;
        this.Baseline = baseline;
        this.LineHeight = lineHeight;
    }

    /// <summary>
    /// Gets or sets the origin of the line, in pixels. The layout sets it once the symbol has its position.
    /// </summary>
    public Vector2 Origin { get; set; }

    /// <summary>
    /// Gets the advance of the line, from <see cref="TextMetrics.Advance"/>. It is relative to the origin of the line.
    /// </summary>
    public FontRectangle Advance { get; }

    /// <summary>
    /// Gets the bounds of the line, from <see cref="TextMetrics.Bounds"/>. They are relative to the origin of the line.
    /// </summary>
    public FontRectangle Bounds { get; }

    /// <summary>
    /// Gets the renderable bounds of the line, from <see cref="TextMetrics.RenderableBounds"/>. They are relative to the origin of the line.
    /// </summary>
    public FontRectangle RenderableBounds { get; }

    /// <summary>
    /// Gets the baseline of the first line, from <see cref="LineMetrics.Baseline"/>, or 0 when the layout does not use it.
    /// </summary>
    public float Baseline { get; }

    /// <summary>
    /// Gets the line height of the first line, from <see cref="LineMetrics.LineHeight"/>, or 0 when the layout does not use it.
    /// </summary>
    public float LineHeight { get; }
}
