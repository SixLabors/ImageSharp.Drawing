// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Text;
using SixLabors.Fonts;
using SixLabors.ImageSharp.Drawing.Processing;

namespace SixLabors.Fonts
{
    public class TextDrawingOptions : TextOptions
    {
        public TextDrawingOptions(Font font)
            : base(font)
        {
        }

        public TextDrawingOptions(TextDrawingOptions options)
            : base(options)
        {
        }

        public new IReadOnlyList<TextDrawingRun> TextRuns
        {
            get => (IReadOnlyList<TextDrawingRun>)base.TextRuns;
            set => base.TextRuns = value;
        }
    }

    public class TextDrawingRun : TextRun
    {
        public IBrush Brush { get; set; }

        public IPen Pen { get; set; }

        public TextDrawingRun()
        {
        }
    }
}
