// Copyright (c) Six Labors and contributors.
// Licensed under the GNU Affero General Public License, Version 3.

using System.Collections.Generic;

namespace SixLabors.ImageSharp.Drawing
{
    /// <summary>
    /// Represents a logic path that can be drawn
    /// </summary>
    public interface ISimplePath
    {
        /// <summary>
        /// Gets a value indicating whether this instance is a closed path.
        /// </summary>
        bool IsClosed { get; }

        /// <summary>
        /// Gets the points that make this up as a simple linear path.
        /// </summary>
        IReadOnlyList<PointF> Points { get; }
    }
}