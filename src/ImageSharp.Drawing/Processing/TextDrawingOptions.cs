// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System.Collections.Generic;
using SixLabors.Fonts;

namespace SixLabors.ImageSharp.Drawing.Processing
{
    /// <summary>
    /// Provides configuration options for rendering and shaping of drawable text.
    /// </summary>
    public class TextDrawingOptions : TextOptions
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="TextDrawingOptions" /> class.
        /// </summary>
        /// <param name="font">The font.</param>
        public TextDrawingOptions(Font font)
            : base(font)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="TextDrawingOptions" /> class from properties
        /// copied from the given instance.
        /// </summary>
        /// <param name="options">The options whose properties are copied into this instance.</param>
        public TextDrawingOptions(TextDrawingOptions options)
            : base(options)
            => this.Path = options.Path;

        /// <summary>
        /// Gets or sets an optional collection of text runs to apply to the body of text.
        /// </summary>
        public new IReadOnlyList<TextDrawingRun> TextRuns
        {
            get => (IReadOnlyList<TextDrawingRun>)base.TextRuns;
            set => base.TextRuns = value;
        }

        /// <summary>
        /// Gets or sets an optional path to draw the text along.
        /// </summary>
        public IPath Path { get; set; }
    }
}
