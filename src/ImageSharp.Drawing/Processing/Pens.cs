// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;

namespace SixLabors.ImageSharp.Drawing.Processing
{
    /// <summary>
    /// Contains a collection of common Pen styles
    /// </summary>
    public static class Pens
    {
        private static readonly float[] DashDotPattern = { 3f, 1f, 1f, 1f };
        private static readonly float[] DashDotDotPattern = { 3f, 1f, 1f, 1f, 1f, 1f };
        private static readonly float[] DottedPattern = { 1f, 1f };
        private static readonly float[] DashedPattern = { 3f, 1f };
        internal static readonly float[] EmptyPattern = Array.Empty<float>();

        /// <summary>
        /// Create a solid pen with out any drawing patterns
        /// </summary>
        /// <param name="color">The color.</param>
        /// <param name="width">The width.</param>
        /// <returns>The Pen</returns>
        public static Pen Solid(Color color, float width) => new Pen(new PenOptions(color, width));

        /// <summary>
        /// Create a solid pen with out any drawing patterns
        /// </summary>
        /// <param name="brush">The brush.</param>
        /// <param name="width">The width.</param>
        /// <returns>The Pen</returns>
        public static Pen Solid(Brush brush, float width) => new Pen(brush, width);

        /// <summary>
        /// Create a pen with a 'Dash' drawing patterns
        /// </summary>
        /// <param name="color">The color.</param>
        /// <param name="width">The width.</param>
        /// <returns>The Pen</returns>
        public static Pen Dash(Color color, float width) => new Pen(new PenOptions(color, width, DashedPattern));

        /// <summary>
        /// Create a pen with a 'Dash' drawing patterns
        /// </summary>
        /// <param name="brush">The brush.</param>
        /// <param name="width">The width.</param>
        /// <returns>The Pen</returns>
        public static Pen Dash(Brush brush, float width) => new Pen(new PenOptions(brush, width, DashedPattern));

        /// <summary>
        /// Create a pen with a 'Dot' drawing patterns
        /// </summary>
        /// <param name="color">The color.</param>
        /// <param name="width">The width.</param>
        /// <returns>The Pen</returns>
        public static Pen Dot(Color color, float width) => new Pen(new PenOptions(color, width, DottedPattern));

        /// <summary>
        /// Create a pen with a 'Dot' drawing patterns
        /// </summary>
        /// <param name="brush">The brush.</param>
        /// <param name="width">The width.</param>
        /// <returns>The Pen</returns>
        public static Pen Dot(Brush brush, float width) => new Pen(new PenOptions(brush, width, DottedPattern));

        /// <summary>
        /// Create a pen with a 'Dash Dot' drawing patterns
        /// </summary>
        /// <param name="color">The color.</param>
        /// <param name="width">The width.</param>
        /// <returns>The Pen</returns>
        public static Pen DashDot(Color color, float width) => new Pen(new PenOptions(color, width, DashDotPattern));

        /// <summary>
        /// Create a pen with a 'Dash Dot' drawing patterns
        /// </summary>
        /// <param name="brush">The brush.</param>
        /// <param name="width">The width.</param>
        /// <returns>The Pen</returns>
        public static Pen DashDot(Brush brush, float width) => new Pen(new PenOptions(brush, width, DashDotPattern));

        /// <summary>
        /// Create a pen with a 'Dash Dot Dot' drawing patterns
        /// </summary>
        /// <param name="color">The color.</param>
        /// <param name="width">The width.</param>
        /// <returns>The Pen</returns>
        public static Pen DashDotDot(Color color, float width) => new Pen(new PenOptions(color, width, DashDotDotPattern));

        /// <summary>
        /// Create a pen with a 'Dash Dot Dot' drawing patterns
        /// </summary>
        /// <param name="brush">The brush.</param>
        /// <param name="width">The width.</param>
        /// <returns>The Pen</returns>
        public static Pen DashDotDot(Brush brush, float width) => new Pen(new PenOptions(brush, width, DashDotDotPattern));
    }
}
