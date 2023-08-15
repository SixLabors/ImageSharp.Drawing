// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

#nullable disable

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
    public Brush Brush { get; set; }

    /// <summary>
    /// Gets or sets the pen used for outlining this run.
    /// </summary>
    public Pen Pen { get; set; }

    /// <summary>
    /// Gets or sets the pen used for drawing strikeout features for this run.
    /// </summary>
    public Pen StrikeoutPen { get; set; }

    /// <summary>
    /// Gets or sets the pen used for drawing underline features for this run.
    /// </summary>
    public Pen UnderlinePen { get; set; }

    /// <summary>
    /// Gets or sets the pen used for drawing overline features for this run.
    /// </summary>
    public Pen OverlinePen { get; set; }
}
