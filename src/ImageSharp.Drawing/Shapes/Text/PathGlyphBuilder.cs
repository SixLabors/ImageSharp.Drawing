// Copyright (c) Six Labors and contributors.
// Licensed under the GNU Affero General Public License, Version 3.

using System;
using System.Numerics;
using SixLabors.Fonts;

namespace SixLabors.ImageSharp.Drawing.Text
{
    /// <summary>
    /// rendering surface that Fonts can use to generate Shapes by following a path
    /// </summary>
    internal class PathGlyphBuilder : GlyphBuilder
    {
        private const float Pi = MathF.PI;
        private readonly IPath path;
        private float yOffset;

        /// <summary>
        /// Initializes a new instance of the <see cref="PathGlyphBuilder"/> class.
        /// </summary>
        /// <param name="path">The path to render the glyps along.</param>
        public PathGlyphBuilder(IPath path)
            : base()
        {
            this.path = path;
        }

        /// <inheritdoc/>
        protected override void BeginText(FontRectangle rect)
        {
            this.yOffset = rect.Height;
        }

        /// <inheritdoc/>
        protected override void BeginGlyph(FontRectangle rect)
        {
            SegmentInfo point = this.path.PointAlongPath(rect.Left);

            Vector2 targetPoint = point.Point + new PointF(0, rect.Top - this.yOffset);

            // due to how matrix combining works you have to combine thins in the revers order of operation
            // this one rotates the glype then moves it.
            Matrix3x2 matrix = Matrix3x2.CreateTranslation(targetPoint - rect.Location) * Matrix3x2.CreateRotation(point.Angle - Pi, point.Point);
            this.builder.SetTransform(matrix);
        }
    }
}
