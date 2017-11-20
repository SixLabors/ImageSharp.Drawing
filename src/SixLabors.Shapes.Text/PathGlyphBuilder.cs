// Copyright (c) Six Labors and contributors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Numerics;
using SixLabors.Primitives;

namespace SixLabors.Shapes.Text
{
    /// <summary>
    /// rendering surface that Fonts can use to generate Shapes by following a path
    /// </summary>
    internal class PathGlyphBuilder : GlyphBuilder
    {
        // TODO: Change to MathF on next release. AssemblyInfo.cs in Core did not list this project
        private const float Pi = (float)Math.PI;
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
        protected override void BeginText(RectangleF rect)
        {
            this.yOffset = rect.Height;
        }

        /// <inheritdoc/>
        protected override void BeginGlyph(RectangleF rect)
        {
            var point = this.path.PointAlongPath(rect.Left);

            PointF targetPoint = point.Point + new PointF(0, rect.Top - this.yOffset);

            // due to how matrix combining works you have to combine thins in the revers order of operation
            // this one rotates the glype then moves it.
            var matrix = Matrix3x2.CreateTranslation(targetPoint - rect.Location) * Matrix3x2.CreateRotation(point.Angle - Pi, point.Point);
            this.builder.SetTransform(matrix);
        }
    }
}
