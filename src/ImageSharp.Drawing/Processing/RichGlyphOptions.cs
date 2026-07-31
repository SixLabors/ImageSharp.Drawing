// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.Fonts;

namespace SixLabors.ImageSharp.Drawing.Processing;

/// <summary>
/// Provides <see cref="GlyphOptions"/> for rendering a glyph by id with per-glyph
/// <see cref="Brush"/> and <see cref="Pen"/> support. The paint is carried to the renderer via a
/// <see cref="RichTextRun"/>, so it can vary per glyph across a run.
/// </summary>
public class RichGlyphOptions : GlyphOptions
{
    /// <inheritdoc cref="RichTextRun.Brush"/>
    public Brush? Brush { get; set; }

    /// <inheritdoc cref="RichTextRun.Pen"/>
    public Pen? Pen { get; set; }

    /// <inheritdoc cref="RichTextRun.StrikeoutPen"/>
    public Pen? StrikeoutPen { get; set; }

    /// <inheritdoc cref="RichTextRun.UnderlinePen"/>
    public Pen? UnderlinePen { get; set; }

    /// <inheritdoc cref="RichTextRun.OverlinePen"/>
    public Pen? OverlinePen { get; set; }

    /// <inheritdoc/>
    protected override TextRun CreateTextRun()
        => new RichTextRun
        {
            Start = this.GraphemeIndex,
            End = this.GraphemeIndex + 1,
            Font = this.Font,
            TextAttributes = this.TextAttributes,
            TextDecorations = this.TextDecorations,
            Brush = this.Brush,
            Pen = this.Pen,
            StrikeoutPen = this.StrikeoutPen,
            UnderlinePen = this.UnderlinePen,
            OverlinePen = this.OverlinePen
        };
}
