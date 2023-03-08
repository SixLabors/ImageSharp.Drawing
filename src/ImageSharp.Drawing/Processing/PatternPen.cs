// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

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
    public class PatternPen : IPen
    {
        private readonly float[] pattern;

        /// <summary>
        /// Initializes a new instance of the <see cref="PatternPen"/> class.
        /// </summary>
        /// <param name="color">The color.</param>
        /// <param name="width">The width.</param>
        /// <param name="pattern">The pattern.</param>
        public PatternPen(Color color, float width, float[] pattern)
            : this(new SolidBrush(color), width, pattern)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="PatternPen"/> class.
        /// </summary>
        /// <param name="brush">The brush.</param>
        /// <param name="width">The width.</param>
        /// <param name="pattern">The pattern.</param>
        public PatternPen(IBrush brush, float width, float[] pattern)
        {
            Guard.MustBeGreaterThan(width, 0, nameof(width));

            this.StrokeFill = brush;
            this.StrokeWidth = width;
            this.pattern = pattern;
        }

        /// <inheritdoc/>
        public IBrush StrokeFill { get; }

        /// <summary>
        /// Gets the width to apply to the stroke
        /// </summary>
        public float? StrokeWidth { get; }

        /// <summary>
        /// Gets the stoke pattern.
        /// </summary>
        public ReadOnlySpan<float> StrokePattern => this.pattern;

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
            if (other is PatternPen p)
            {
                return p.StrokeWidth == this.StrokeWidth &&
                p.JointStyle == this.JointStyle &&
                p.EndCapStyle == this.EndCapStyle &&
                p.StrokeFill.Equals(this.StrokeFill) &&
                Enumerable.SequenceEqual(p.pattern, this.pattern);
            }

            return false;
        }

        /// <inheritdoc />
        public IPath GeneratePath(IPath path, float? defaultWidth = null)
            => path.GenerateOutline(this.StrokeWidth ?? defaultWidth ?? 1, this.StrokePattern, this.JointStyle, this.EndCapStyle);
    }

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
    public sealed class Pen
    {
        private readonly float[] pattern;

        /// <summary>
        /// Initializes a new instance of the <see cref="PatternPen"/> class.
        /// </summary>
        /// <param name="strokeFill">The brush used to fill the stroke outline.</param>
        public Pen(Brush strokeFill)
            : this(strokeFill, 1)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="PatternPen"/> class.
        /// </summary>
        /// <param name="strokeFill">The brush used to fill the stroke outline.</param>
        /// <param name="strokeWidth">The stroke width in px units.</param>
        public Pen(Brush strokeFill, float strokeWidth)
            : this(strokeFill, strokeWidth, Pens.EmptyPattern)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Pen"/> class.
        /// </summary>
        /// <param name="strokeFill">The brush used to fill the stroke outline.</param>
        /// <param name="strokeWidth">The stroke width in px units.</param>
        /// <param name="strokePattern">The stroke pattern.</param>
        private Pen(Brush strokeFill, float strokeWidth, float[] strokePattern)
        {
            Guard.MustBeGreaterThan(strokeWidth, 0, nameof(strokeWidth));

            this.StrokeFill = strokeFill;
            this.StrokeWidth = strokeWidth;
            this.pattern = strokePattern;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Pen"/> class.
        /// </summary>
        /// <param name="options">The pen options.</param>
        public Pen(PenOptions options)
        {
            Guard.NotNull(options, nameof(options));

            this.StrokeFill = options.StrokeFill;
            this.StrokeWidth = options.StrokeWidth;
            this.pattern = options.StrokePattern;
            this.JointStyle = options.JointStyle;
            this.EndCapStyle = options.EndCapStyle;
        }

        /// <inheritdoc cref="PenOptions.StrokeFill"/>
        public Brush StrokeFill { get; }

        /// <inheritdoc cref="PenOptions.StrokeWidth"/>
        public float StrokeWidth { get; }

        /// <inheritdoc cref="PenOptions.StrokePattern"/>
        public ReadOnlySpan<float> StrokePattern => this.pattern;

        /// <inheritdoc cref="PenOptions.JointStyle"/>
        public JointStyle JointStyle { get; }

        /// <inheritdoc cref="PenOptions.EndCapStyle"/>
        public EndCapStyle EndCapStyle { get; }
    }
}
