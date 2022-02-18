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
        private readonly bool isVerticalLayout;
        private float xOffset;
        private float yOffset;

        /// <summary>
        /// Initializes a new instance of the <see cref="PathGlyphBuilder"/> class.
        /// </summary>
        /// <param name="path">The path to render the glyphs along.</param>
        /// <param name="layoutMode">The mode to determine the layout of the text.</param>
        public PathGlyphBuilder(IPath path, LayoutMode layoutMode)
        {
            if (path is IPathInternals internals)
            {
                this.path = internals;
            }
            else
            {
                this.path = new ComplexPolygon(path);
            }

            this.isVerticalLayout = IsVertical(layoutMode);
        }

        /// <inheritdoc/>
        protected override void BeginText(FontRectangle rect)
        {
            // TODO: This uses the baseline of the text, should it be the bottom?
            this.yOffset = rect.Height;
            this.xOffset = rect.Left;
        }

        /// <inheritdoc/>
        protected override void BeginGlyph(FontRectangle rect)
        {
            // https://svgwg.org/svg2-draft/text.html#TextpathLayoutRules
            if (this.isVerticalLayout)
            {
                this.TransformGlyphVertical(rect);
            }
            else
            {
                this.TransformGlyphHorizontal(rect);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void TransformGlyphHorizontal(FontRectangle rect)
        {
            // Find the intersection point. Bottom-centre for horizontal text.
            float halfWidth = rect.Width * .5F;
            Vector2 intersectionPoint = new(rect.Left + halfWidth, rect.Bottom);

            // Find the point of this intersection along the given path. This ensures the correct rotation.
            SegmentInfo point = this.path.PointAlongPath(intersectionPoint.X - this.xOffset);

            // Now offset our target point since we're aligning top-left.
            Vector2 targetPoint = point.Point + new PointF(-halfWidth, intersectionPoint.Y - rect.Height - this.yOffset);

            // Due to how matrix combining works you have to combine this in the reverse order of operation
            // this one rotates the glype then moves it.
            Matrix3x2 matrix = Matrix3x2.CreateTranslation(targetPoint - rect.Location) * Matrix3x2.CreateRotation(point.Angle - Pi, point.Point);
            this.builder.SetTransform(matrix);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void TransformGlyphVertical(FontRectangle rect)
        {
            // TODO: Fix this.
            // Find the intersection point. Centre-centre for vertical text.
            float halfWidth = rect.Width * .5F;
            float halfHeight = rect.Height * .5F;
            Vector2 intersectionPoint = new(rect.Left + halfWidth, rect.Top + halfHeight);

            SegmentInfo point = this.path.PointAlongPath(intersectionPoint.X - this.xOffset);

            // Now offset our target point since we're aligning top-left.
            Vector2 targetPoint = point.Point + new PointF(-halfWidth, intersectionPoint.Y - halfHeight - this.yOffset);

            // Due to how matrix combining works you have to combine this in the reverse order of operation
            // this one rotates the glype then moves it.
            Matrix3x2 matrix = Matrix3x2.CreateTranslation(targetPoint - rect.Location) * Matrix3x2.CreateRotation(point.Angle - Pi, point.Point);
            this.builder.SetTransform(matrix);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsVertical(LayoutMode mode)
        {
            const LayoutMode vertical = LayoutMode.VerticalLeftRight | LayoutMode.VerticalRightLeft;
            return (mode & vertical) > 0;
        }
    }
}
