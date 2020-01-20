// Copyright (c) Six Labors and contributors.
// Licensed under the Apache License, Version 2.0.

using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Processing
{
    /// <summary>
    /// Options for influencing the drawing functions.
    /// </summary>
    public class ShapeGraphicsOptions : IDeepCloneable<ShapeGraphicsOptions>
    {
        private int antialiasSubpixelDepth = 16;
        private float blendPercentage = 1F;

        /// <summary>
        /// Initializes a new instance of the <see cref="ShapeGraphicsOptions"/> class.
        /// </summary>
        public ShapeGraphicsOptions()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ShapeGraphicsOptions"/> class.
        /// </summary>
        /// <param name="source">The source to clone from.</param>
        public ShapeGraphicsOptions(GraphicsOptions source)
        {
            this.AlphaCompositionMode = source.AlphaCompositionMode;
            this.Antialias = source.Antialias;
            this.AntialiasSubpixelDepth = source.AntialiasSubpixelDepth;
            this.BlendPercentage = source.BlendPercentage;
            this.ColorBlendingMode = source.ColorBlendingMode;
        }

        private ShapeGraphicsOptions(ShapeGraphicsOptions source)
        {
            this.AlphaCompositionMode = source.AlphaCompositionMode;
            this.Antialias = source.Antialias;
            this.AntialiasSubpixelDepth = source.AntialiasSubpixelDepth;
            this.BlendPercentage = source.BlendPercentage;
            this.ColorBlendingMode = source.ColorBlendingMode;
            this.IntersectionRule = source.IntersectionRule;
        }

        /// <summary>
        /// Gets or sets a value indicating whether antialiasing should be applied.
        /// Defaults to true.
        /// </summary>
        public IntersectionRule IntersectionRule { get; set; } = IntersectionRule.OddEven;

        /// <summary>
        /// Gets or sets a value indicating whether antialiasing should be applied.
        /// Defaults to true.
        /// </summary>
        public bool Antialias { get; set; } = true;

        /// <summary>
        /// Gets or sets a value indicating the number of subpixels to use while rendering with antialiasing enabled.
        /// </summary>
        public int AntialiasSubpixelDepth
        {
            get
            {
                return this.antialiasSubpixelDepth;
            }

            set
            {
                Guard.MustBeGreaterThanOrEqualTo(value, 0, nameof(this.AntialiasSubpixelDepth));
                this.antialiasSubpixelDepth = value;
            }
        }

        /// <summary>
        /// Gets or sets a value indicating the blending percentage to apply to the drawing operation.
        /// </summary>
        public float BlendPercentage
        {
            get
            {
                return this.blendPercentage;
            }

            set
            {
                Guard.MustBeBetweenOrEqualTo(value, 0, 1F, nameof(this.BlendPercentage));
                this.blendPercentage = value;
            }
        }

        /// <summary>
        /// Gets or sets a value indicating the color blending percentage to apply to the drawing operation.
        /// Defaults to <see cref= "PixelColorBlendingMode.Normal" />.
        /// </summary>
        public PixelColorBlendingMode ColorBlendingMode { get; set; } = PixelColorBlendingMode.Normal;

        /// <summary>
        /// Gets or sets a value indicating the color blending percentage to apply to the drawing operation
        /// Defaults to <see cref= "PixelAlphaCompositionMode.SrcOver" />.
        /// </summary>
        public PixelAlphaCompositionMode AlphaCompositionMode { get; set; } = PixelAlphaCompositionMode.SrcOver;

        /// <summary>
        /// Performs an implicit conversion from <see cref="GraphicsOptions"/> to <see cref="TextGraphicsOptions"/>.
        /// </summary>
        /// <param name="options">The options.</param>
        /// <returns>
        /// The result of the conversion.
        /// </returns>
        public static implicit operator ShapeGraphicsOptions(GraphicsOptions options)
        {
            return new ShapeGraphicsOptions(options);
        }

        /// <summary>
        /// Performs an explicit conversion from <see cref="TextGraphicsOptions"/> to <see cref="GraphicsOptions"/>.
        /// </summary>
        /// <param name="options">The options.</param>
        /// <returns>
        /// The result of the conversion.
        /// </returns>
        public static explicit operator GraphicsOptions(ShapeGraphicsOptions options)
        {
            return new GraphicsOptions()
            {
                Antialias = options.Antialias,
                AntialiasSubpixelDepth = options.AntialiasSubpixelDepth,
                ColorBlendingMode = options.ColorBlendingMode,
                AlphaCompositionMode = options.AlphaCompositionMode,
                BlendPercentage = options.BlendPercentage
            };
        }

        /// <inheritdoc/>
        public ShapeGraphicsOptions DeepClone() => new ShapeGraphicsOptions(this);
    }
}
