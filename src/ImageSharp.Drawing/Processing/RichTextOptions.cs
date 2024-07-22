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
        => this.TextRuns = Array.Empty<RichTextRun>();

    /// <summary>
    /// Initializes a new instance of the <see cref="RichTextOptions" /> class from properties
    /// copied from the given instance.
    /// </summary>
    /// <param name="options">The options whose properties are copied into this instance.</param>
    public RichTextOptions(RichTextOptions options)
        : base(options)
        => this.Path = options.Path;

    /// <summary>
    /// Gets or sets an optional collection of text runs to apply to the body of text.
    /// </summary>
    public new IReadOnlyList<RichTextRun> TextRuns
    {
        get => (IReadOnlyList<RichTextRun>)base.TextRuns;
        set => base.TextRuns = value;
    }

    /// <summary>
    /// Gets or sets an optional path to draw the text along.
    /// </summary>
    /// <remarks>
    /// When this property is not <see langword="null"/> the <see cref="TextOptions.Origin"/>
    /// property is automatically applied as a translation to a copy of the path for processing.
    /// </remarks>
    public IPath? Path { get; set; }
}
