// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Numerics;
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
        protected override void BeginText(FontRectangle rect) => this.yOffset = rect.Height;

        /// <inheritdoc/>
        protected override void BeginGlyph(FontRectangle rect)
        {
            SegmentInfo point = this.path.PointAlongPath(rect.Left);

            Vector2 targetPoint = point.Point + new PointF(0, rect.Top - this.yOffset);

            // Due to how matrix combining works you have to combine this in the reverse order of operation
            // this one rotates the glype then moves it.
            Matrix3x2 matrix = Matrix3x2.CreateTranslation(targetPoint - rect.Location) * Matrix3x2.CreateRotation(point.Angle - Pi, point.Point);
            this.builder.SetTransform(matrix);
        }
    }
}
