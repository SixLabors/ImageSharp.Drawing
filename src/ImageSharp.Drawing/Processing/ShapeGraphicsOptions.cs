// Copyright (c) Six Labors and contributors.
// Licensed under the GNU Affero General Public License, Version 3.

using System;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace SixLabors.ImageSharp.Drawing.Processing
{
    /// <summary>
    /// Options for influencing the drawing functions.
    /// </summary>
    public class ShapeGraphicsOptions
    {
        private GraphicsOptions graphicsOptions;
        private ShapeOptions shapeOptions;

        /// <summary>
        /// Initializes a new instance of the <see cref="ShapeGraphicsOptions"/> class.
        /// </summary>
        public ShapeGraphicsOptions()
        {
            this.graphicsOptions = new GraphicsOptions();
            this.shapeOptions = new ShapeOptions();
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ShapeGraphicsOptions"/> class.
        /// </summary>
        /// <param name="graphicsOptions">The graphic options to use</param>
        /// <param name="shapeOptions">The text options to use</param>
        public ShapeGraphicsOptions(GraphicsOptions graphicsOptions, ShapeOptions shapeOptions)
        {
            Guard.NotNull(graphicsOptions, nameof(graphicsOptions));
            Guard.NotNull(shapeOptions, nameof(shapeOptions));
            this.graphicsOptions = graphicsOptions;
            this.shapeOptions = shapeOptions;
        }

        /// <summary>
        /// Gets or sets the Graphics Options.
        /// </summary>
        public GraphicsOptions GraphicsOptions
        {
            get => this.graphicsOptions;
            set
            {
                Guard.NotNull(value, nameof(this.GraphicsOptions));
                this.graphicsOptions = value;
            }
        }

        /// <summary>
        /// Gets or sets the Text Options.
        /// </summary>
        public ShapeOptions ShapeOptions
        {
            get => this.shapeOptions;
            set
            {
                Guard.NotNull(value, nameof(this.ShapeOptions));
                this.shapeOptions = value;
            }
        }
    }
}
