// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using SixLabors.Fonts;

namespace SixLabors.ImageSharp.Drawing.Text
{
    /// <summary>
    /// A rendering surface that Fonts can use to generate shapes by following a path.
    /// </summary>
    internal sealed class PathGlyphBuilder : GlyphBuilder
    {
        private readonly IPathInternals path;
        private Vector2 textOffset;
        private readonly TextOptions textOptions;

        /// <summary>
        /// Initializes a new instance of the <see cref="PathGlyphBuilder"/> class.
        /// </summary>
        /// <param name="path">The path to render the glyphs along.</param>
        /// <param name="textOptions">The text rendering options.</param>
        public PathGlyphBuilder(IPath path, TextOptions textOptions)
        {
            if (path is IPathInternals internals)
            {
                this.path = internals;
            }
            else
            {
                this.path = new ComplexPolygon(path);
            }

            this.textOptions = textOptions;
        }

        /// <inheritdoc/>
        protected override void BeginText(in FontRectangle bounds)
        {
            float yOffset = this.textOptions.VerticalAlignment switch
            {
                VerticalAlignment.Center => bounds.Bottom - (bounds.Height * .5F),
                VerticalAlignment.Bottom => bounds.Bottom,
                VerticalAlignment.Top => bounds.Top,
                _ => bounds.Top,
            };

            float xOffset = this.textOptions.HorizontalAlignment switch
            {
                HorizontalAlignment.Center => bounds.Right - (bounds.Width * .5F),
                HorizontalAlignment.Right => bounds.Right,
                HorizontalAlignment.Left => bounds.Left,
                _ => bounds.Left,
            };
            this.textOffset = new(xOffset, yOffset);
        }

        /// <inheritdoc/>
        protected override void BeginGlyph(in FontRectangle bounds, in GlyphRendererParameters parameters)
            => this.TransformGlyph(in bounds);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void TransformGlyph(in FontRectangle bounds)
        {
            // Find the intersection point.
            // This should be offset to ensure we rotate at the bottom-center of the glyph.
            float halfWidth = bounds.Width * .5F;

            // Find the point of this intersection along the given path.
            SegmentInfo pathPoint = this.path.PointAlongPath(bounds.Left + halfWidth);

            // Now offset our target point since we're aligning the bottom-left location of our glyph against the path.
            // This is good and accurate when we are vertically aligned to the path however the distance between
            // characters in multiline text scales with the angle and vertical offset.
            // This is expected and consistant with other libraries. Multiple line text should be rendered using multiple paths to avoid this behavior.
            Vector2 targetPoint = (Vector2)pathPoint.Point + new Vector2(-halfWidth, bounds.Top) - bounds.Location - this.textOffset;

            // Due to how matrix combining works you have to combine this in the reverse order of operation.
            Matrix3x2 matrix = Matrix3x2.CreateTranslation(targetPoint) * Matrix3x2.CreateRotation(pathPoint.Angle - MathF.PI, pathPoint.Point);
            this.Builder.SetTransform(matrix);
        }
    }
}
