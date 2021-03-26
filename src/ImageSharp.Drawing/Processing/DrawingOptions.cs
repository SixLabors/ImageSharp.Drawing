// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Numerics;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace SixLabors.ImageSharp.Drawing.Processing
{
    /// <summary>
    /// Options for influencing the drawing functions.
    /// </summary>
    public class DrawingOptions
    {
        private GraphicsOptions graphicsOptions;
        private ShapeOptions shapeOptions;
        private TextOptions textOptions;

        /// <summary>
        /// Initializes a new instance of the <see cref="DrawingOptions"/> class.
        /// </summary>
        public DrawingOptions()
        {
            this.graphicsOptions = new GraphicsOptions();
            this.shapeOptions = new ShapeOptions();
            this.textOptions = new TextOptions();
            this.Transform = Matrix3x2.Identity;
        }

        internal DrawingOptions(
            GraphicsOptions graphicsOptions,
            ShapeOptions shapeOptions,
            TextOptions textOptions,
            Matrix3x2 transform)
        {
            DebugGuard.NotNull(graphicsOptions, nameof(graphicsOptions));
            DebugGuard.NotNull(shapeOptions, nameof(shapeOptions));
            DebugGuard.NotNull(textOptions, nameof(textOptions));

            this.graphicsOptions = graphicsOptions;
            this.shapeOptions = shapeOptions;
            this.textOptions = textOptions;
            this.Transform = transform;
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
        /// Gets or sets the Shape Options.
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

        /// <summary>
        /// Gets or sets the Text Options.
        /// </summary>
        public TextOptions TextOptions
        {
            get => this.textOptions;
            set
            {
                Guard.NotNull(value, nameof(this.TextOptions));
                this.textOptions = value;
            }
        }

        /// <summary>
        /// Gets or sets the Transform to apply during rasterization.
        /// </summary>
        public Matrix3x2 Transform { get; set; }
    }
}
