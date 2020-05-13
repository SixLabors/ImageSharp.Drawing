// Copyright (c) Six Labors and contributors.
// Licensed under the GNU Affero General Public License, Version 3.

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
        }

        /// <summary>
        /// Gets or sets a value indicating whether antialiasing should be applied.
        /// Defaults to true.
        /// </summary>
        public IntersectionRule IntersectionRule { get; set; } = IntersectionRule.OddEven;

        /// <inheritdoc/>
        public ShapeOptions DeepClone() => new ShapeOptions(this);
    }
}
