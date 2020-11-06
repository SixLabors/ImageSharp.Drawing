// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

namespace SixLabors.ImageSharp.Drawing.Processing
{
    /// <summary>
    /// Options for influencing the drawing functions.
    /// </summary>
    public class TextGraphicsOptions
    {
        private GraphicsOptions graphicsOptions;
        private TextOptions textOptions;

        /// <summary>
        /// Initializes a new instance of the <see cref="TextGraphicsOptions"/> class.
        /// </summary>
        public TextGraphicsOptions()
        {
            this.graphicsOptions = new GraphicsOptions();
            this.textOptions = new TextOptions();
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="TextGraphicsOptions"/> class.
        /// </summary>
        /// <param name="graphicsOptions">The graphic options to use</param>
        /// <param name="textOptions">The text options to use</param>
        public TextGraphicsOptions(GraphicsOptions graphicsOptions, TextOptions textOptions)
        {
            Guard.NotNull(graphicsOptions, nameof(graphicsOptions));
            Guard.NotNull(textOptions, nameof(textOptions));
            this.graphicsOptions = graphicsOptions;
            this.textOptions = textOptions;
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
        public TextOptions TextOptions
        {
            get => this.textOptions;
            set
            {
                Guard.NotNull(value, nameof(this.TextOptions));
                this.textOptions = value;
            }
        }

        public bool UsePolygonScanner { get; set; } = true;
    }
}
