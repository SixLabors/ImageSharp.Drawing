// Copyright (c) Six Labors and contributors.
// Licensed under the GNU Affero General Public License, Version 3.

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
        IBrush StrokeFill { get; }

        /// <summary>
        /// Gets the width to apply to the stroke
        /// </summary>
        float StrokeWidth { get; }

        /// <summary>
        /// Gets the stoke pattern.
        /// </summary>
        ReadOnlySpan<float> StrokePattern { get; }
    }
}