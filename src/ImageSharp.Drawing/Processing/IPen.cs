// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;

namespace SixLabors.ImageSharp.Drawing.Processing
{
    /// <summary>
    /// Interface representing the pattern and size of the stroke to apply with a Pen.
    /// </summary>
    public interface IPen : IEquatable<IPen>
    {
        /// <summary>
        /// Gets the stroke fill.
        /// </summary>
        IBrush StrokeFill { get; }

        ///// <summary>
        ///// Gets the width to apply to the stroke
        ///// </summary>
        //float StrokeWidth { get; }

        ///// <summary>
        ///// Gets the stoke pattern.
        ///// </summary>
        //ReadOnlySpan<float> StrokePattern { get; }

        ///// <summary>
        ///// Gets or sets the stroke joint style
        ///// </summary>
        //public JointStyle JointStyle { get; set; }

        ///// <summary>
        ///// Gets or sets the stroke endcap style
        ///// </summary>
        //public EndCapStyle EndCapStyle { get; set; }

        /// <summary>
        /// Applies the styleing from the pen to a path and generate a new path with the final vector.
        /// </summary>
        /// <param name="path">the source path</param>
        /// <param name="defaultWidth">the default width to apply if the pen does not have one.</param>
        /// <returns>the path withthe pen styleing applied</returns>
        public IPath GeneratePath(IPath path, float? defaultWidth = null);

    }
}
