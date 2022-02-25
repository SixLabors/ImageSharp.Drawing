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
        private float xOffset;
        private float yOffset;

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
        protected override void BeginText(FontRectangle bounds)
        {
            this.yOffset = bounds.Bottom;
            this.xOffset = bounds.Left;
        }

        /// <inheritdoc/>
        protected override void BeginGlyph(FontRectangle bounds) => this.TransformGlyph(bounds);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void TransformGlyph(FontRectangle bounds)
        {
            // Find the intersection point. This should be offset to ensure we rotate at the bottom-center of the glyph.
            float halfWidth = (bounds.Right - bounds.Left) * .5F;
            Vector2 intersectPoint = new(bounds.Left + halfWidth, bounds.Top);

            // Find the point of this intersection along the given path.
            SegmentInfo pathPoint = this.path.PointAlongPath(intersectPoint.X - this.xOffset);

            // Now offset our target point since we're aligning the bottom-left location of our glyph against the path.
            Vector2 targetPoint = pathPoint.Point + new PointF(-halfWidth, intersectPoint.Y - this.yOffset);

            // Due to how matrix combining works you have to combine this in the reverse order of operation.
            // First rotate the glyph then move it.
            Matrix3x2 matrix = Matrix3x2.CreateTranslation(targetPoint - bounds.Location) * Matrix3x2.CreateRotation(pathPoint.Angle - Pi, pathPoint.Point);
            this.Builder.SetTransform(matrix);
        }
    }
}
