// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using SixLabors.Fonts;

namespace SixLabors.ImageSharp.Drawing.Processing
{
    /// <summary>
    /// Represents a run of drawable text spanning a series of graphemes within a string.
    /// </summary>
    public class TextDrawingRun : TextRun
    {
        /// <summary>
        /// Gets or sets the brush used for filling this run.
        /// </summary>
        public IBrush Brush { get; set; }

        /// <summary>
        /// Gets or sets the pen used for outlining this run.
        /// </summary>
        public IPen Pen { get; set; }

        /// <summary>
        /// Gets or sets the pen used for drawing strikeout features for this run.
        /// </summary>
        public IPen StrikeoutPen { get; set; }

        /// <summary>
        /// Gets or sets the pen used for drawing underline features for this run.
        /// </summary>
        public IPen UnderlinePen { get; set; }

        /// <summary>
        /// Gets or sets the pen used for drawing overline features for this run.
        /// </summary>
        public IPen OverlinePen { get; set; }
    }
}
