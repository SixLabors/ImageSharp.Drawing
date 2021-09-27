// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System.Collections.Generic;
using System.Numerics;
using SixLabors.Fonts;

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
        private float lineSpacing = 1F;

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
            this.LineSpacing = source.LineSpacing;
            this.HorizontalAlignment = source.HorizontalAlignment;
            this.TabWidth = source.TabWidth;
            this.WrapTextWidth = source.WrapTextWidth;
            this.VerticalAlignment = source.VerticalAlignment;
            this.FallbackFonts.AddRange(source.FallbackFonts);
            this.RenderColorFonts = source.RenderColorFonts;
        }

        /// <summary>
        /// Gets or sets a value indicating whether the text should be drawing with kerning enabled.
        /// <para/>
        /// Defaults to true.
        /// </summary>
        public bool ApplyKerning { get; set; } = true;

        /// <summary>
        /// Gets or sets a value indicating the number of space widths a tab should lock to.
        /// <para/>
        /// Defaults to 4.
        /// </summary>
        public float TabWidth
        {
            get => this.tabWidth;

            set
            {
                Guard.MustBeGreaterThanOrEqualTo(value, 0, nameof(this.TabWidth));
                this.tabWidth = value;
            }
        }

        /// <summary>
        /// Gets or sets a value, if greater than 0, indicating the width at which text should wrap.
        /// <para/>
        /// Defaults to 0.
        /// </summary>
        public float WrapTextWidth { get; set; }

        /// <summary>
        /// Gets or sets a value indicating the DPI (Dots Per Inch) to render text along the X axis.
        /// <para/>
        /// Defaults to 72.
        /// </summary>
        public float DpiX
        {
            get => this.dpiX;

            set
            {
                Guard.MustBeGreaterThanOrEqualTo(value, 0, nameof(this.DpiX));
                this.dpiX = value;
            }
        }

        /// <summary>
        /// Gets or sets a value indicating the DPI (Dots Per Inch) to render text along the Y axis.
        /// <para/>
        /// Defaults to 72.
        /// </summary>
        public float DpiY
        {
            get => this.dpiY;

            set
            {
                Guard.MustBeGreaterThanOrEqualTo(value, 0, nameof(this.DpiY));
                this.dpiY = value;
            }
        }

        /// <summary>
        /// Gets or sets the line spacing. Applied as a multiple of the line height.
        /// <para/>
        /// Defaults to 1.
        /// </summary>
        public float LineSpacing
        {
            get => this.lineSpacing;

            set
            {
                Guard.IsTrue(value != 0, nameof(this.LineSpacing), "Value must not be equal to 0.");
                this.lineSpacing = value;
            }
        }

        /// <summary>
        /// Gets or sets a value indicating how to align the text relative to the rendering space.
        /// If <see cref="WrapTextWidth"/> is greater than zero it will align relative to the space
        /// defined by the location and width, if <see cref="WrapTextWidth"/> equals zero, and thus
        /// wrapping disabled, then the alignment is relative to the drawing location.
        /// <para/>
        /// Defaults to <see cref="HorizontalAlignment.Left"/>.
        /// </summary>
        public HorizontalAlignment HorizontalAlignment { get; set; } = HorizontalAlignment.Left;

        /// <summary>
        /// Gets or sets a value indicating how to align the text relative to the rendering space.
        /// <para/>
        /// Defaults to <see cref="VerticalAlignment.Top"/>.
        /// </summary>
        public VerticalAlignment VerticalAlignment { get; set; } = VerticalAlignment.Top;

        /// <summary>
        /// Gets or sets a value indicating what word breaking mode to use when wrapping text.
        /// <para/>
        /// Defaults to <see cref="WordBreaking.Normal"/>.
        /// </summary>
        public WordBreaking WordBreaking { get; set; } = WordBreaking.Normal;

        /// <summary>
        /// Gets the list of fallback font families to apply to the text drawing operation.
        /// <para/>
        /// Defaults to the empty list.
        /// </summary>
        public List<FontFamily> FallbackFonts { get; } = new List<FontFamily>();

        /// <summary>
        /// Gets or sets a value indicating whether we should render color(emoji) fonts.
        /// <para/>
        /// Defaults to true.
        /// </summary>
        public bool RenderColorFonts { get; set; } = true;

        /// <inheritdoc/>
        public TextOptions DeepClone() => new TextOptions(this);
    }
}
