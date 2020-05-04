// Copyright (c) Six Labors and contributors.
// Licensed under the Apache License, Version 2.0.

using System.Collections.Generic;
using SixLabors.Fonts;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Drawing.Processing
{
    /// <summary>
    /// Options for influencing text parts of the drawing functions.
    /// </summary>
    public class TextOptions : IDeepCloneable<TextOptions>
    {
        private float tabWidth = 4F;
        private float dpiX = 72F;
        private float dpiY = 72F;

        /// <summary>
        /// Initializes a new instance of the <see cref="TextOptions"/> class.
        /// </summary>
        public TextOptions()
        {
        }

        private TextOptions(TextOptions source)
        {
            this.ApplyKerning = source.ApplyKerning;
            this.DpiX = source.DpiX;
            this.DpiY = source.DpiY;
            this.HorizontalAlignment = source.HorizontalAlignment;
            this.TabWidth = source.TabWidth;
            this.WrapTextWidth = source.WrapTextWidth;
            this.VerticalAlignment = source.VerticalAlignment;
            this.FallbackFonts.AddRange(source.FallbackFonts);
            this.RenderColorFonts = source.RenderColorFonts;
        }

        /// <summary>
        /// Gets or sets a value indicating whether the text should be drawing with kerning enabled.
        /// Defaults to true;
        /// </summary>
        public bool ApplyKerning { get; set; } = true;

        /// <summary>
        /// Gets or sets a value indicating the number of space widths a tab should lock to.
        /// Defaults to 4.
        /// </summary>
        public float TabWidth
        {
            get
            {
                return this.tabWidth;
            }

            set
            {
                Guard.MustBeGreaterThanOrEqualTo(value, 0, nameof(this.TabWidth));
                this.tabWidth = value;
            }
        }

        /// <summary>
        /// Gets or sets a value, if greater than 0, indicating the width at which text should wrap.
        /// Defaults to 0.
        /// </summary>
        public float WrapTextWidth { get; set; }

        /// <summary>
        /// Gets or sets a value indicating the DPI (Dots Per Inch) to render text along the X axis.
        /// Defaults to 72.
        /// </summary>
        public float DpiX
        {
            get
            {
                return this.dpiX;
            }

            set
            {
                Guard.MustBeGreaterThanOrEqualTo(value, 0, nameof(this.DpiX));
                this.dpiX = value;
            }
        }

        /// <summary>
        /// Gets or sets a value indicating the DPI (Dots Per Inch) to render text along the Y axis.
        /// Defaults to 72.
        /// </summary>
        public float DpiY
        {
            get
            {
                return this.dpiY;
            }

            set
            {
                Guard.MustBeGreaterThanOrEqualTo(value, 0, nameof(this.DpiY));
                this.dpiY = value;
            }
        }

        /// <summary>
        /// Gets or sets a value indicating how to align the text relative to the rendering space.
        /// If <see cref="WrapTextWidth"/> is greater than zero it will align relative to the space
        /// defined by the location and width, if <see cref="WrapTextWidth"/> equals zero, and thus
        /// wrapping disabled, then the alignment is relative to the drawing location.
        /// Defaults to <see cref="HorizontalAlignment.Left"/>.
        /// </summary>
        public HorizontalAlignment HorizontalAlignment { get; set; } = HorizontalAlignment.Left;

        /// <summary>
        /// Gets or sets a value indicating how to align the text relative to the rendering space.
        /// Defaults to <see cref="VerticalAlignment.Top"/>.
        /// </summary>
        public VerticalAlignment VerticalAlignment { get; set; } = VerticalAlignment.Top;

        /// <summary>
        /// Gets the list of fallback font families to apply to the text drawing operation.
        /// Defaults to <see cref="VerticalAlignment.Top"/>.
        /// </summary>
        public List<FontFamily> FallbackFonts { get; } = new List<FontFamily>();

        /// <summary>
        /// Gets or sets a value indicating whether we should render color(emoji) fonts.
        /// Defaults to true.
        /// </summary>
        public bool RenderColorFonts { get; set; } = true;

        /// <inheritdoc/>
        public TextOptions DeepClone() => new TextOptions(this);
    }
}
