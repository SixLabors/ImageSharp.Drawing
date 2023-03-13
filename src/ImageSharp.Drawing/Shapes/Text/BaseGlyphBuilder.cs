// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System.Collections.Generic;
using System.Numerics;
using SixLabors.Fonts;

namespace SixLabors.ImageSharp.Drawing.Text
{
    /// <summary>
    /// Defines a rendering surface that Fonts can use to generate Shapes.
    /// </summary>
    internal class BaseGlyphBuilder : IGlyphRenderer
    {
        private readonly List<IPath> paths = new();
        private Vector2 currentPoint;
        private GlyphRendererParameters parameters;

        /// <summary>
        /// Initializes a new instance of the <see cref="BaseGlyphBuilder"/> class.
        /// </summary>
        public BaseGlyphBuilder() => this.Builder = new PathBuilder();

        /// <summary>
        /// Gets the paths that have been rendered by the current instance.
        /// </summary>
        public IPathCollection Paths => new PathCollection(this.paths);

        /// <summary>
        /// Gets the path builder for the current instance.
        /// </summary>
        protected PathBuilder Builder { get; }

        /// <inheritdoc/>
        void IGlyphRenderer.EndText()
        {
        }

        /// <inheritdoc/>
        void IGlyphRenderer.BeginText(in FontRectangle bounds) => this.BeginText(bounds);

        /// <inheritdoc/>
        bool IGlyphRenderer.BeginGlyph(in FontRectangle bounds, in GlyphRendererParameters paramaters)
        {
            this.parameters = paramaters;
            this.Builder.Clear();
            this.BeginGlyph(bounds);
            return true;
        }

        /// <summary>
        /// Begins the figure.
        /// </summary>
        void IGlyphRenderer.BeginFigure() => this.Builder.StartFigure();

        /// <summary>
        /// Draws a cubic bezier from the current point  to the <paramref name="point"/>
        /// </summary>
        /// <param name="secondControlPoint">The second control point.</param>
        /// <param name="thirdControlPoint">The third control point.</param>
        /// <param name="point">The point.</param>
        void IGlyphRenderer.CubicBezierTo(Vector2 secondControlPoint, Vector2 thirdControlPoint, Vector2 point)
        {
            this.Builder.AddCubicBezier(this.currentPoint, secondControlPoint, thirdControlPoint, point);
            this.currentPoint = point;
        }

        /// <summary>
        /// Ends the glyph.
        /// </summary>
        void IGlyphRenderer.EndGlyph() => this.paths.Add(this.Builder.Build());

        /// <summary>
        /// Ends the figure.
        /// </summary>
        void IGlyphRenderer.EndFigure() => this.Builder.CloseFigure();

        /// <summary>
        /// Draws a line from the current point  to the <paramref name="point"/>.
        /// </summary>
        /// <param name="point">The point.</param>
        void IGlyphRenderer.LineTo(Vector2 point)
        {
            this.Builder.AddLine(this.currentPoint, point);
            this.currentPoint = point;
        }

        /// <summary>
        /// Moves to current point to the supplied vector.
        /// </summary>
        /// <param name="point">The point.</param>
        void IGlyphRenderer.MoveTo(Vector2 point)
        {
            this.Builder.StartFigure();
            this.currentPoint = point;
        }

        /// <summary>
        /// Draws a quadratics bezier from the current point  to the <paramref name="point"/>
        /// </summary>
        /// <param name="secondControlPoint">The second control point.</param>
        /// <param name="point">The point.</param>
        void IGlyphRenderer.QuadraticBezierTo(Vector2 secondControlPoint, Vector2 point)
        {
            this.Builder.AddQuadraticBezier(this.currentPoint, secondControlPoint, point);
            this.currentPoint = point;
        }

        /// <summary>Called before any glyphs have been rendered.</summary>
        /// <param name="bounds">The bounds the text will be rendered at and at what size.</param>
        protected virtual void BeginText(in FontRectangle bounds)
        {
        }

        /// <summary>Begins the glyph.</summary>
        /// <param name="bounds">The bounds the glyph will be rendered at and at what size.</param>
        protected virtual void BeginGlyph(in FontRectangle bounds)
        {
        }

        public virtual TextDecorations EnabledDecorations()
            => this.parameters.TextRun.TextDecorations;

        public virtual void SetDecoration(TextDecorations textDecorations, Vector2 start, Vector2 end, float thickness)
        {
            if (thickness == 0)
            {
                return;
            }

            var renderer = (IGlyphRenderer)this;

            Vector2 height = new(0, thickness);
            Vector2 tl = start;
            Vector2 tr = end;
            Vector2 bl = start + height;
            Vector2 br = end + height;

            // MoveTo calls StartFigure();
            renderer.MoveTo(tl);
            renderer.LineTo(bl);
            renderer.LineTo(br);
            renderer.LineTo(tr);
            renderer.EndFigure();
        }
    }
}
