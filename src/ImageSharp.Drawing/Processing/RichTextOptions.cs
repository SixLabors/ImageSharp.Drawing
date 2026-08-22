// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.Fonts;
using SixLabors.Fonts.Tables.AdvancedTypographic;

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
        // Copy each run into a fresh instance so later mutation of the source runs
        // cannot leak into this options instance (and vice versa). Every property of
        // RichTextRun and its TextRun base must appear here: a missing property
        // silently resets to its default on the clone DrawText renders from.
        List<RichTextRun> runs = new(options.TextRuns.Count);
        foreach (RichTextRun run in options.TextRuns)
        {
            runs.Add(new RichTextRun()
            {
                // Brushes, pens, fonts, and palettes copy by reference: each is immutable
                // once constructed (FontPalette snapshots its overrides in its own
                // constructor), so a shared reference cannot leak later mutation.
                Brush = run.Brush,
                Pen = run.Pen,
                StrikeoutPen = run.StrikeoutPen,
                UnderlinePen = run.UnderlinePen,
                OverlinePen = run.OverlinePen,
                Start = run.Start,
                End = run.End,
                Font = run.Font,
                FontWeight = run.FontWeight,
                Script = run.Script,
                Culture = run.Culture,

                // The feature tag list is the one caller-owned mutable collection on a
                // run; the read-only interface is only a view, so isolation needs a copy.
                FeatureTags = run.FeatureTags is null ? null : new List<Tag>(run.FeatureTags),
                TextAttributes = run.TextAttributes,
                TextDecorations = run.TextDecorations,
                ColorFontSupport = run.ColorFontSupport,
                FontPalette = run.FontPalette,
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
