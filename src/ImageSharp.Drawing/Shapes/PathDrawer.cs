// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System.Numerics;

namespace SixLabors.ImageSharp.Drawing
{
    /// <summary>
    /// Allow you to derivatively draw shapes and paths.
    /// </summary>
    public class PathDrawer
    {
        private readonly PathBuilder builder;
        private Vector2 currentPoint = default;

        /// <summary>
        /// Initializes a new instance of the <see cref="PathDrawer"/> class.
        /// </summary>
        /// <param name="pathBuilder">The path build to draw to.</param>
        public PathDrawer(PathBuilder pathBuilder) => this.builder = pathBuilder;

        /// <summary>
        /// Initializes a new instance of the <see cref="PathDrawer"/> class.
        /// </summary>
        public PathDrawer()
            : this(new PathBuilder())
        {
        }

        /// <summary>
        /// Clears all drawn paths, Leaving any applied transforms.
        /// </summary>
        public void Clear() => this.builder.Clear();

        /// <summary>
        /// Begins the figure.
        /// </summary>
        public void StartFigure() => this.builder.StartFigure();

        /// <summary>
        /// Draws a cubic bezier from the current point  to the <paramref name="point"/>
        /// </summary>
        /// <param name="secondControlPoint">The second control point.</param>
        /// <param name="thirdControlPoint">The third control point.</param>
        /// <param name="point">The point.</param>
        public void CubicBezierTo(Vector2 secondControlPoint, Vector2 thirdControlPoint, Vector2 point)
        {
            this.builder.AddBezier(this.currentPoint, secondControlPoint, thirdControlPoint, point);
            this.currentPoint = point;
        }

        /// <summary>
        /// Ends the glyph.
        /// </summary>
        /// <returns>The built path</returns>
        public IPath Build() => this.builder.Build();

        /// <summary>
        /// Ends the figure.
        /// </summary>
        public void CloseFigure() => this.builder.CloseFigure();

        /// <summary>
        /// Draws a line from the current point  to the <paramref name="point"/>.
        /// </summary>
        /// <param name="point">The point.</param>
        public void LineTo(Vector2 point)
        {
            this.builder.AddLine(this.currentPoint, point);
            this.currentPoint = point;
        }

        /// <summary>
        /// Moves to current point to the supplied vector.
        /// </summary>
        /// <param name="point">The point.</param>
        public void MoveTo(Vector2 point)
        {
            this.builder.StartFigure();
            this.currentPoint = point;
        }

        /// <summary>
        /// Draws a quadratics bezier from the current point  to the <paramref name="point"/>
        /// </summary>
        /// <param name="secondControlPoint">The second control point.</param>
        /// <param name="point">The point.</param>
        public void QuadraticBezierTo(Vector2 secondControlPoint, Vector2 point)
        {
            this.builder.AddBezier(this.currentPoint, secondControlPoint, point);
            this.currentPoint = point;
        }

        /// <summary>
        /// Sets the translation to be applied to all items to follow being applied to the <see cref="PathDrawer"/>.
        /// </summary>
        /// <param name="translation">The translation.</param>
        public void SetTransform(Matrix3x2 translation) => this.builder.SetTransform(translation);

        /// <summary>
        /// Sets the origin all subsequent point should be relative to.
        /// </summary>
        /// <param name="origin">The origin.</param>
        public void SetOrigin(Vector2 origin) => this.builder.SetOrigin(origin);
    }
}
