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
        private const float Pi = MathF.PI;
        private readonly IPathInternals path;
        private Vector2 textOffset;

        /// <summary>
        /// Initializes a new instance of the <see cref="PathGlyphBuilder"/> class.
        /// </summary>
        /// <param name="path">The path to render the glyphs along.</param>
        public PathGlyphBuilder(IPath path)
        {
            if (path is IPathInternals internals)
            {
                this.path = internals;
            }
            else
            {
                this.path = new ComplexPolygon(path);
            }
        }

        /// <inheritdoc/>
        protected override void BeginText(FontRectangle bounds) => this.textOffset = new(bounds.Left, bounds.Bottom);

        /// <inheritdoc/>
        protected override void BeginGlyph(FontRectangle bounds) => this.TransformGlyph(bounds);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void TransformGlyph(FontRectangle bounds)
        {
            // Find the intersection point.
            // This should be offset to ensure we rotate at the bottom-center of the glyph.
            float halfWidth = bounds.Width * .5F;

            // Find the point of this intersection along the given path.
            SegmentInfo pathPoint = this.path.PointAlongPath(bounds.Left + halfWidth);

            // Now offset our target point since we're aligning the bottom-left location of our glyph against the path.
            // TODO: This is good and accurate when we are vertically alligned to the path however the distance between
            // characters in multiline text scales with the angle and vertical offset.
            // It would be good to be able to fix this.
            Vector2 targetPoint = (Vector2)pathPoint.Point + new Vector2(-halfWidth, bounds.Top) - bounds.Location - this.textOffset;

            // Due to how matrix combining works you have to combine this in the reverse order of operation.
            Matrix3x2 matrix = Matrix3x2.CreateTranslation(targetPoint) * Matrix3x2.CreateRotation(pathPoint.Angle - Pi, pathPoint.Point);
            this.Builder.SetTransform(matrix);
        }
    }
}
