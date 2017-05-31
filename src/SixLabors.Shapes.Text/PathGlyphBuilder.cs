using System;
using System.Collections.Generic;
using System.Numerics;

using SixLabors.Fonts;
using SixLabors.Shapes;

namespace SixLabors.Shapes.Text
{

    /// <summary>
    /// rendering surface that Fonts can use to generate Shapes by following a path
    /// </summary>
    internal class PathGlyphBuilder : GlyphBuilder
    {
        private readonly IPath path;

        private float yOffset = 0;

        const float Pi = (float)Math.PI;
        const float HalfPi = Pi / 2f;

        /// <summary>
        /// Initializes a new instance of the <see cref="GlyphBuilder"/> class.
        /// </summary>
        /// <param name="path">The path to render the glyps along.</param>
        public PathGlyphBuilder(IPath path)
            : base()
        {
            this.path = path;
        }

        protected override void BeginText(Vector2 location, Fonts.Size size)
        {
            this.yOffset = size.Height;
        }

        protected override void BeginGlyph(Vector2 location, Fonts.Size size)
        {
            var point = this.path.PointAlongPath(location.X);

            var targetPoint = point.Point + new Vector2(0, (location.Y - this.yOffset));

            // due to how matrix combining works you have to combine thins in the revers order of operation
            // this one rotates the glype then moves it.
            var matrix = Matrix3x2.CreateTranslation(targetPoint - location) * Matrix3x2.CreateRotation(point.Angle - Pi, point.Point);
            this.builder.SetTransform(matrix);
        }
    }
}
