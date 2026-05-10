// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.Fonts;

namespace SixLabors.ImageSharp.Drawing.Processing;

/// <summary>
/// Provides configuration options for rendering and shaping of rich text.
/// </summary>
public class RichTextOptions : TextOptions
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RichTextOptions" /> class.
    /// </summary>
    /// <param name="font">The font.</param>
    public RichTextOptions(Font font)
        : base(font)
        => this.TextRuns = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="RichTextOptions" /> class from properties
    /// copied from the given instance.
    /// </summary>
    /// <param name="options">The options whose properties are copied into this instance.</param>
    public RichTextOptions(RichTextOptions options)
        : base(options)
    {
        List<RichTextRun> runs = new(options.TextRuns.Count);
        foreach (RichTextRun run in options.TextRuns)
        {
            runs.Add(new RichTextRun()
            {
                Brush = run.Brush,
                Pen = run.Pen,
                StrikeoutPen = run.StrikeoutPen,
                UnderlinePen = run.UnderlinePen,
                OverlinePen = run.OverlinePen,
                Start = run.Start,
                End = run.End,
                Font = run.Font,
                TextAttributes = run.TextAttributes,
                TextDecorations = run.TextDecorations,
                Placeholder = run.Placeholder
            });
        }

        this.TextRuns = runs;
    }

    /// <summary>
    /// Gets or sets an optional collection of text runs to apply to the body of text.
    /// </summary>
    public new IReadOnlyList<RichTextRun> TextRuns
    {
        get => (IReadOnlyList<RichTextRun>)base.TextRuns;
        set => base.TextRuns = value;
    }
}
