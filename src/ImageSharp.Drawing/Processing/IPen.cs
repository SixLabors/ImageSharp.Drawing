// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;

namespace SixLabors.ImageSharp.Drawing.Processing
{
    /// <summary>
    /// Interface representing the pattern and size of the stroke to apply with a Pen.
    /// </summary>
    public interface IPen
    {
        /// <summary>
        /// Gets the stroke fill.
        /// </summary>
        Brush StrokeFill { get; }

        /// <summary>
        /// Gets the width to apply to the stroke
        /// </summary>
        float StrokeWidth { get; }

        /// <summary>
        /// Gets the stoke pattern.
        /// </summary>
        ReadOnlySpan<float> StrokePattern { get; }

        /// <summary>
        /// Gets or sets the stroke joint style
        /// </summary>
        public JointStyle JointStyle { get; set; }

        /// <summary>
        /// Gets or sets the stroke endcap style
        /// </summary>
        public EndCapStyle EndCapStyle { get; set; }
    }
}
