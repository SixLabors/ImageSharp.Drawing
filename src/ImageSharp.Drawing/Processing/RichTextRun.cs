// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.Fonts;

namespace SixLabors.ImageSharp.Drawing.Processing;

/// <summary>
/// Represents a run of drawable text spanning a series of graphemes within a string.
/// </summary>
public class RichTextRun : TextRun
{
    /// <summary>
    /// Gets or sets the brush used for filling this run.
    /// </summary>
    public Brush? Brush { get; set; }

    /// <summary>
    /// Gets or sets the pen used for outlining this run.
    /// </summary>
    public Pen? Pen { get; set; }

    /// <summary>
    /// Gets or sets the pen used for drawing strikeout features for this run.
    /// </summary>
    public Pen? StrikeoutPen { get; set; }

    /// <summary>
    /// Gets or sets the pen used for drawing underline features for this run.
    /// </summary>
    public Pen? UnderlinePen { get; set; }

    /// <summary>
    /// Gets or sets the pen used for drawing overline features for this run.
    /// </summary>
    public Pen? OverlinePen { get; set; }

    /// <inheritdoc/>
    public override TextDecorationOptions? GetDecorationOptions(TextDecorations decoration)
    {
        Pen? pen = decoration switch
        {
            TextDecorations.Underline => this.UnderlinePen,
            TextDecorations.Strikeout => this.StrikeoutPen,
            TextDecorations.Overline => this.OverlinePen,
            _ => null,
        };

        if (pen is null)
        {
            return null;
        }

        // Report the same whole-pixel stroke width the renderer paints so the skip-ink gaps and
        // measurement band clear the width that is actually drawn, not the font-metric thickness.
        return new TextDecorationOptions
        {
            Thickness = MathF.Max(1F, (float)Math.Round(pen.StrokeWidth)),
        };
    }
}
