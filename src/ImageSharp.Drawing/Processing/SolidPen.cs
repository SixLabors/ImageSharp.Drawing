// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System.Diagnostics.CodeAnalysis;

namespace SixLabors.ImageSharp.Drawing.Processing
{
    /// <summary>
    /// Provides a pen that can apply a pattern to a line with a set brush and thickness
    /// </summary>
    /// <remarks>
    /// The pattern will be in to the form of new float[]{ 1f, 2f, 0.5f} this will be
    /// converted into a pattern that is 3.5 times longer that the width with 3 sections
    /// section 1 will be width long (making a square) and will be filled by the brush
    /// section 2 will be width * 2 long and will be empty
    /// section 3 will be width/2 long and will be filled
    /// the pattern will immediately repeat without gap.
    /// </remarks>
    public class SolidPen : IPen
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SolidPen"/> class.
        /// </summary>
        /// <param name="color">The color.</param>
        /// <param name="width">The width.</param>
        public SolidPen(Color color, float width)
            : this(new SolidBrush(color), width)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="SolidPen"/> class.
        /// </summary>
        /// <param name="brush">The brush.</param>
        /// <param name="width">The width.</param>
        public SolidPen(IBrush brush, float width)
        {
            Guard.MustBeGreaterThan(width, 0, nameof(width));
            this.StrokeFill = brush;
            this.StrokeWidth = width;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="SolidPen"/> class.
        /// </summary>
        /// <param name="color">The color.</param>
        public SolidPen(Color color)
            : this(new SolidBrush(color), null)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="SolidPen"/> class.
        /// </summary>
        /// <param name="brush">The brush.</param>
        public SolidPen(IBrush brush)
            : this(brush, null)
        {
        }

        private SolidPen(IBrush brush, float? width)
        {
            this.StrokeFill = brush;
            this.StrokeWidth = width;
        }

        /// <inheritdoc/>
        public IBrush StrokeFill { get; }

        /// <summary>
        /// Gets the width to apply to the stroke
        /// </summary>
        public float? StrokeWidth { get; }

        /// <summary>
        /// Gets or sets the stroke joint style
        /// </summary>
        public JointStyle JointStyle { get; set; }

        /// <summary>
        /// Gets or sets the stroke endcap style
        /// </summary>
        public EndCapStyle EndCapStyle { get; set; }

        /// <inheritdoc/>
        public bool Equals(IPen other)
        {
            if (other is SolidPen p)
            {
                return p.StrokeWidth == this.StrokeWidth &&
                p.JointStyle == this.JointStyle &&
                p.EndCapStyle == this.EndCapStyle &&
                p.StrokeFill.Equals(this.StrokeFill);
            }

            return false;
        }

        /// <inheritdoc />
        public IPath GeneratePath(IPath path, float? defaultWidth = null)
            => path.GenerateOutline(this.StrokeWidth ?? defaultWidth ?? 1, this.JointStyle, this.EndCapStyle);
    }
}
