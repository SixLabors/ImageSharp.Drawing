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
        /// Create a solid pen without any drawing patterns
        /// </summary>
        /// <param name="color">The color.</param>
        /// <returns>The Pen</returns>
        public static SolidPen Solid(Color color) => new(color);

        /// <summary>
        /// Create a solid pen without any drawing patterns
        /// </summary>
        /// <param name="brush">The brush.</param>
        /// <returns>The Pen</returns>
        public static SolidPen Solid(Brush brush) => new(brush);

        /// <summary>
        /// Create a solid pen without any drawing patterns
        /// </summary>
        /// <param name="color">The color.</param>
        /// <param name="width">The width.</param>
        /// <returns>The Pen</returns>
        public static SolidPen Solid(Color color, float width) => new(color, width);

        /// <summary>
        /// Create a solid pen without any drawing patterns
        /// </summary>
        /// <param name="brush">The brush.</param>
        /// <param name="width">The width.</param>
        /// <returns>The Pen</returns>
        public static SolidPen Solid(Brush brush, float width) => new(brush, width);

        /// <summary>
        /// Create a pen with a 'Dash' drawing patterns
        /// </summary>
        /// <param name="color">The color.</param>
        /// <param name="width">The width.</param>
        /// <returns>The Pen</returns>
        public static PatternPen Dash(Color color, float width) => new(color, width, DashedPattern);

        /// <summary>
        /// Create a pen with a 'Dash' drawing patterns
        /// </summary>
        /// <param name="brush">The brush.</param>
        /// <param name="width">The width.</param>
        /// <returns>The Pen</returns>
        public static PatternPen Dash(Brush brush, float width) => new(brush, width, DashedPattern);

        /// <summary>
        /// Create a pen with a 'Dot' drawing patterns
        /// </summary>
        /// <param name="color">The color.</param>
        /// <param name="width">The width.</param>
        /// <returns>The Pen</returns>
        public static PatternPen Dot(Color color, float width) => new(color, width, DottedPattern);

        /// <summary>
        /// Create a pen with a 'Dot' drawing patterns
        /// </summary>
        /// <param name="brush">The brush.</param>
        /// <param name="width">The width.</param>
        /// <returns>The Pen</returns>
        public static PatternPen Dot(Brush brush, float width) => new(brush, width, DottedPattern);

        /// <summary>
        /// Create a pen with a 'Dash Dot' drawing patterns
        /// </summary>
        /// <param name="color">The color.</param>
        /// <param name="width">The width.</param>
        /// <returns>The Pen</returns>
        public static PatternPen DashDot(Color color, float width) => new(color, width, DashDotPattern);

        /// <summary>
        /// Create a pen with a 'Dash Dot' drawing patterns
        /// </summary>
        /// <param name="brush">The brush.</param>
        /// <param name="width">The width.</param>
        /// <returns>The Pen</returns>
        public static PatternPen DashDot(Brush brush, float width) => new(brush, width, DashDotPattern);

        /// <summary>
        /// Create a pen with a 'Dash Dot Dot' drawing patterns
        /// </summary>
        /// <param name="color">The color.</param>
        /// <param name="width">The width.</param>
        /// <returns>The Pen</returns>
        public static PatternPen DashDotDot(Color color, float width) => new(color, width, DashDotDotPattern);

        /// <summary>
        /// Create a pen with a 'Dash Dot Dot' drawing patterns
        /// </summary>
        /// <param name="brush">The brush.</param>
        /// <param name="width">The width.</param>
        /// <returns>The Pen</returns>
        public static PatternPen DashDotDot(Brush brush, float width) => new(brush, width, DashDotDotPattern);
    }
}
