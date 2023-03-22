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
        protected override void BeginGlyph(in FontRectangle bounds, in GlyphRendererParameters parameters)
            => this.TransformGlyph(in bounds);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void TransformGlyph(in FontRectangle bounds)
        {
            // Find the point of this intersection along the given path.
            // We want to find the point on the path that is closest to the center-bottom side of the glyph.
            Vector2 half = new(bounds.Width * .5F, 0);
            SegmentInfo pathPoint = this.path.PointAlongPath(bounds.Left + half.X);

            // Now offset to our target point since we're aligning the top-left location of our glyph against the path.
            Vector2 translation = (Vector2)pathPoint.Point - bounds.Location - half + new Vector2(0, bounds.Top);
            Matrix3x2 matrix = Matrix3x2.CreateTranslation(translation) * Matrix3x2.CreateRotation(pathPoint.Angle - MathF.PI, (Vector2)pathPoint.Point);

            this.Builder.SetTransform(matrix);
        }
    }
}
