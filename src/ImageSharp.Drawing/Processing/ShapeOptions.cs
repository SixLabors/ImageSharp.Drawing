// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using SixLabors.ImageSharp.Drawing.Shapes;

namespace SixLabors.ImageSharp.Drawing.Processing
{
    /// <summary>
    /// Options for influencing the drawing functions.
    /// </summary>
    public class ShapeOptions : IDeepCloneable<ShapeOptions>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ShapeOptions"/> class.
        /// </summary>
        public ShapeOptions()
        {
        }

        private ShapeOptions(ShapeOptions source)
        {
            this.IntersectionRule = source.IntersectionRule;
            this.OrientationHandling = source.OrientationHandling;
        }

        /// <summary>
        /// Gets or sets a value indicating whether antialiasing should be applied.
        /// Defaults to true.
        /// </summary>
        public IntersectionRule IntersectionRule { get; set; } = IntersectionRule.OddEven;

        internal OrientationHandling OrientationHandling { get; set; } =
            OrientationHandling.ForcePositiveOrientationOnSimplePolygons;

        /// <inheritdoc/>
        public ShapeOptions DeepClone() => new ShapeOptions(this);
    }
}
