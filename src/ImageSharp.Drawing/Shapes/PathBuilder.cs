// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace SixLabors.ImageSharp.Drawing
{
    /// <summary>
    /// Allow you to derivatively build shapes and paths.
    /// </summary>
    public class PathBuilder
    {
        private readonly List<Figure> figures = new();
        private readonly Matrix3x2 defaultTransform;
        private Figure currentFigure = null;
        private Matrix3x2 currentTransform;
        private Matrix3x2 setTransform;
        private Vector2 currentPoint = default;

        /// <summary>
        /// Initializes a new instance of the <see cref="PathBuilder" /> class.
        /// </summary>
        public PathBuilder()
            : this(Matrix3x2.Identity)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="PathBuilder"/> class.
        /// </summary>
        /// <param name="defaultTransform">The default transform.</param>
        public PathBuilder(Matrix3x2 defaultTransform)
        {
            this.defaultTransform = defaultTransform;
            this.Clear();
            this.ResetTransform();
        }

        /// <summary>
        /// Sets the translation to be applied to all items to follow being applied to the <see cref="PathBuilder"/>.
        /// </summary>
        /// <param name="translation">The translation.</param>
        /// <returns>The <see cref="PathBuilder"/>.</returns>
        public PathBuilder SetTransform(Matrix3x2 translation)
        {
            this.setTransform = translation;
            this.currentTransform = this.setTransform * this.defaultTransform;
            return this;
        }

        /// <summary>
        /// Sets the origin all subsequent point should be relative to.
        /// </summary>
        /// <param name="origin">The origin.</param>
        /// <returns>The <see cref="PathBuilder"/>.</returns>
        public PathBuilder SetOrigin(PointF origin)
        {
            // the new origin should be transofrmed based on the default transform
            this.setTransform.Translation = origin;
            this.currentTransform = this.setTransform * this.defaultTransform;

            return this;
        }

        /// <summary>
        /// Resets the translation to the default.
        /// </summary>
        /// <returns>The <see cref="PathBuilder"/>.</returns>
        public PathBuilder ResetTransform()
        {
            this.setTransform = Matrix3x2.Identity;
            this.currentTransform = this.setTransform * this.defaultTransform;

            return this;
        }

        /// <summary>
        /// Resets the origin to the default.
        /// </summary>
        /// <returns>The <see cref="PathBuilder"/>.</returns>
        public PathBuilder ResetOrigin()
        {
            this.setTransform.Translation = Vector2.Zero;
            this.currentTransform = this.setTransform * this.defaultTransform;

            return this;
        }

        /// <summary>
        /// Moves to current point to the supplied vector.
        /// </summary>
        /// <param name="point">The point.</param>
        /// <returns>The <see cref="PathBuilder"/>.</returns>
        public PathBuilder MoveTo(PointF point)
        {
            this.StartFigure();
            this.currentPoint = PointF.Transform(point, this.currentTransform);
            return this;
        }

        /// <summary>
        /// Draws the line connecting the current the current point to the new point.
        /// </summary>
        /// <param name="point">The point.</param>
        /// <returns>The <see cref="PathBuilder"/>.</returns>
        public PathBuilder LineTo(PointF point)
            => this.AddLine(this.currentPoint, point);

        /// <summary>
        /// Draws the line connecting the current the current point to the new point.
        /// </summary>
        /// <param name="x">The x.</param>
        /// <param name="y">The y.</param>
        /// <returns>The <see cref="PathBuilder"/></returns>
        public PathBuilder LineTo(float x, float y)
            => this.LineTo(new PointF(x, y));

        /// <summary>
        /// Adds the line connecting the current point to the new point.
        /// </summary>
        /// <param name="start">The start.</param>
        /// <param name="end">The end.</param>
        /// <returns>The <see cref="PathBuilder"/>.</returns>
        public PathBuilder AddLine(PointF start, PointF end)
            => this.AddSegment(new LinearLineSegment(start, end));

        /// <summary>
        /// Adds the line connecting the current point to the new point.
        /// </summary>
        /// <param name="x1">The x1.</param>
        /// <param name="y1">The y1.</param>
        /// <param name="x2">The x2.</param>
        /// <param name="y2">The y2.</param>
        /// <returns>The <see cref="PathBuilder"/>.</returns>
        public PathBuilder AddLine(float x1, float y1, float x2, float y2)
            => this.AddLine(new PointF(x1, y1), new PointF(x2, y2));

        /// <summary>
        /// Adds a series of line segments connecting the current point to the new points.
        /// </summary>
        /// <param name="points">The points.</param>
        /// <returns>The <see cref="PathBuilder"/>.</returns>
        public PathBuilder AddLines(IEnumerable<PointF> points)
        {
            Guard.NotNull(points, nameof(points));
            return this.AddLines(points.ToArray());
        }

        /// <summary>
        /// Adds a series of line segments connecting the current point to the new points.
        /// </summary>
        /// <param name="points">The points.</param>
        /// <returns>The <see cref="PathBuilder"/>.</returns>
        public PathBuilder AddLines(params PointF[] points)
        {
            Guard.NotNull(points, nameof(points));
            return this.AddSegment(new LinearLineSegment(points));
        }

        /// <summary>
        /// Adds the segment.
        /// </summary>
        /// <param name="segment">The segment.</param>
        /// <returns>The <see cref="PathBuilder"/>.</returns>
        public PathBuilder AddSegment(ILineSegment segment)
        {
            Guard.NotNull(segment, nameof(segment));

            segment = segment.Transform(this.currentTransform);
            this.currentFigure.AddSegment(segment);
            this.currentPoint = segment.EndPoint;
            return this;
        }

        /// <summary>
        /// Draws a quadratics bezier from the current point to the <paramref name="point"/>
        /// </summary>
        /// <param name="secondControlPoint">The second control point.</param>
        /// <param name="point">The point.</param>
        /// <returns>The <see cref="PathBuilder"/>.</returns>
        public PathBuilder QuadraticBezierTo(Vector2 secondControlPoint, Vector2 point)
            => this.AddQuadraticBezier(this.currentPoint, secondControlPoint, point);

        /// <summary>
        /// Draws a quadratics bezier from the current point to the <paramref name="point"/>
        /// </summary>
        /// <param name="secondControlPoint">The second control point.</param>
        /// <param name="thirdControlPoint">The third control point.</param>
        /// <param name="point">The point.</param>
        /// <returns>The <see cref="PathBuilder"/>.</returns>
        public PathBuilder CubicBezierTo(Vector2 secondControlPoint, Vector2 thirdControlPoint, Vector2 point)
            => this.AddCubicBezier(this.currentPoint, secondControlPoint, thirdControlPoint, point);

        /// <summary>
        /// Adds a quadratic bezier curve to the current figure joining the <paramref name="startPoint"/> point to the <paramref name="endPoint"/>.
        /// </summary>
        /// <param name="startPoint">The start point.</param>
        /// <param name="controlPoint">The control point1.</param>
        /// <param name="endPoint">The end point.</param>
        /// <returns>The <see cref="PathBuilder"/>.</returns>
        public PathBuilder AddQuadraticBezier(PointF startPoint, PointF controlPoint, PointF endPoint)
        {
            Vector2 startPointVector = startPoint;
            Vector2 controlPointVector = controlPoint;
            Vector2 endPointVector = endPoint;

            Vector2 c1 = ((controlPointVector - startPointVector) * 2 / 3) + startPointVector;
            Vector2 c2 = ((controlPointVector - endPointVector) * 2 / 3) + endPointVector;

            return this.AddCubicBezier(startPointVector, c1, c2, endPoint);
        }

        /// <summary>
        /// Adds a cubic bezier curve to the current figure joining the <paramref name="startPoint"/> point to the <paramref name="endPoint"/>.
        /// </summary>
        /// <param name="startPoint">The start point.</param>
        /// <param name="controlPoint1">The control point1.</param>
        /// <param name="controlPoint2">The control point2.</param>
        /// <param name="endPoint">The end point.</param>
        /// <returns>The <see cref="PathBuilder"/>.</returns>
        public PathBuilder AddCubicBezier(PointF startPoint, PointF controlPoint1, PointF controlPoint2, PointF endPoint)
            => this.AddSegment(new CubicBezierLineSegment(startPoint, controlPoint1, controlPoint2, endPoint));

        /// <summary>
        /// <para>
        /// Adds an elliptical arc to the current figure. The arc curves from the last point to <paramref name="point"/>,
        /// choosing one of four possible routes: clockwise or counterclockwise, and smaller or larger.
        /// </para>
        /// <para>
        /// The arc sweep is always less than 360 degrees. The method appends a line
        /// to the last point if either radii are zero, or if last point is equal to <paramref name="point"/>.
        /// In addition the method scales the radii to fit last point and <paramref name="point"/> if both
        /// are greater than zero but too small to describe an arc.
        /// </para>
        /// </summary>
        /// <param name="radiusX">The x-radius of the ellipsis.</param>
        /// <param name="radiusY">The y-radius of the ellipsis.</param>
        /// <param name="rotation">The rotation along the X-axis; measured in degrees clockwise.</param>
        /// <param name="largeArc">
        /// The large arc flag, and is <see langword="false"/> if an arc spanning less than or equal to 180 degrees
        /// is chosen, or <see langword="true"/> if an arc spanning greater than 180 degrees is chosen.
        /// </param>
        /// <param name="sweep">
        /// The sweep flag, and is <see langword="false"/> if the line joining center to arc sweeps through decreasing
        /// angles, or <see langword="true"/> if it sweeps through increasing angles.
        /// </param>
        /// <param name="point">The end point of the arc.</param>
        /// <returns>The <see cref="PathBuilder"/>.</returns>
        public PathBuilder ArcTo(float radiusX, float radiusY, float rotation, bool largeArc, bool sweep, PointF point)
            => this.AddArc(this.currentPoint, radiusX, radiusY, rotation, largeArc, sweep, point);

        /// <summary>
        /// <para>
        /// Adds an elliptical arc to the current figure. The arc curves from the <paramref name="startPoint"/> to <paramref name="endPoint"/>,
        /// choosing one of four possible routes: clockwise or counterclockwise, and smaller or larger.
        /// </para>
        /// <para>
        /// The arc sweep is always less than 360 degrees. The method appends a line
        /// to the last point if either radii are zero, or if last point is equal to <paramref name="endPoint"/>.
        /// In addition the method scales the radii to fit last point and <paramref name="endPoint"/> if both
        /// are greater than zero but too small to describe an arc.
        /// </para>
        /// </summary>
        /// <param name="startPoint">The start point of the arc.</param>
        /// <param name="radiusX">The x-radius of the ellipsis.</param>
        /// <param name="radiusY">The y-radius of the ellipsis.</param>
        /// <param name="rotation">The rotation along the X-axis; measured in degrees clockwise.</param>
        /// <param name="largeArc">
        /// The large arc flag, and is <see langword="false"/> if an arc spanning less than or equal to 180 degrees
        /// is chosen, or <see langword="true"/> if an arc spanning greater than 180 degrees is chosen.
        /// </param>
        /// <param name="sweep">
        /// The sweep flag, and is <see langword="false"/> if the line joining center to arc sweeps through decreasing
        /// angles, or <see langword="true"/> if it sweeps through increasing angles.
        /// </param>
        /// <param name="endPoint">The end point of the arc.</param>
        /// <returns>The <see cref="PathBuilder"/>.</returns>
        public PathBuilder AddArc(PointF startPoint, float radiusX, float radiusY, float rotation, bool largeArc, bool sweep, PointF endPoint)
            => this.AddSegment(new ArcLineSegment(startPoint, endPoint, new(radiusX, radiusY), rotation, largeArc, sweep));

        /// <summary>
        /// Adds an elliptical arc to the current figure.
        /// </summary>
        /// <param name="rectangle">A <see cref="RectangleF"/> that represents the rectangular bounds of the ellipse from which the arc is taken.</param>
        /// <param name="rotation">The angle, in degrees, from the x-axis of the current coordinate system to the x-axis of the ellipse.</param>
        /// <param name="startAngle">
        /// The start angle of the elliptical arc prior to the stretch and rotate operations. (0 is at the 3 o'clock position of the arc's circle).
        /// </param>
        /// <param name="sweepAngle">The angle between <paramref name="startAngle"/> and the end of the arc.</param>
        /// <returns>The <see cref="PathBuilder"/>.</returns>
        public PathBuilder AddArc(RectangleF rectangle, float rotation, float startAngle, float sweepAngle)
            => this.AddArc((rectangle.Right + rectangle.Left) / 2, (rectangle.Bottom + rectangle.Top) / 2, rectangle.Width / 2, rectangle.Height / 2, rotation, startAngle, sweepAngle);

        /// <summary>
        /// Adds an elliptical arc to the current figure.
        /// </summary>
        /// <param name="rectangle">A <see cref="Rectangle"/> that represents the rectangular bounds of the ellipse from which the arc is taken.</param>
        /// <param name="rotation">The angle, in degrees, from the x-axis of the current coordinate system to the x-axis of the ellipse.</param>
        /// <param name="startAngle">
        /// The start angle of the elliptical arc prior to the stretch and rotate operations. (0 is at the 3 o'clock position of the arc's circle).
        /// </param>
        /// <param name="sweepAngle">The angle between <paramref name="startAngle"/> and the end of the arc.</param>
        /// <returns>The <see cref="PathBuilder"/>.</returns>
        public PathBuilder AddArc(Rectangle rectangle, int rotation, int startAngle, int sweepAngle)
            => this.AddArc((RectangleF)rectangle, rotation, startAngle, sweepAngle);

        /// <summary>
        /// Adds an elliptical arc to the current figure.
        /// </summary>
        /// <param name="center">The center <see cref="PointF"/> of the ellipse from which the arc is taken.</param>
        /// <param name="radiusX">The x-radius of the ellipsis.</param>
        /// <param name="radiusY">The y-radius of the ellipsis.</param>
        /// <param name="rotation">The angle, in degrees, from the x-axis of the current coordinate system to the x-axis of the ellipse.</param>
        /// <param name="startAngle">
        /// The start angle of the elliptical arc prior to the stretch and rotate operations. (0 is at the 3 o'clock position of the arc's circle).
        /// </param>
        /// <param name="sweepAngle">The angle between <paramref name="startAngle"/> and the end of the arc.</param>
        /// <returns>The <see cref="PathBuilder"/>.</returns>
        public PathBuilder AddArc(PointF center, float radiusX, float radiusY, float rotation, float startAngle, float sweepAngle)
            => this.AddArc(center.X, center.Y, radiusX, radiusY, rotation, startAngle, sweepAngle);

        /// <summary>
        /// Adds an elliptical arc to the current figure.
        /// </summary>
        /// <param name="center">The center <see cref="Point"/> of the ellipse from which the arc is taken.</param>
        /// <param name="radiusX">The x-radius of the ellipsis.</param>
        /// <param name="radiusY">The y-radius of the ellipsis.</param>
        /// <param name="rotation">The angle, in degrees, from the x-axis of the current coordinate system to the x-axis of the ellipse.</param>
        /// <param name="startAngle">
        /// The start angle of the elliptical arc prior to the stretch and rotate operations. (0 is at the 3 o'clock position of the arc's circle).
        /// </param>
        /// <param name="sweepAngle">The angle between <paramref name="startAngle"/> and the end of the arc.</param>
        /// <returns>The <see cref="PathBuilder"/>.</returns>
        public PathBuilder AddArc(Point center, int radiusX, int radiusY, int rotation, int startAngle, int sweepAngle)
            => this.AddArc((PointF)center, radiusX, radiusY, rotation, startAngle, sweepAngle);

        /// <summary>
        /// Adds an elliptical arc to the current figure.
        /// </summary>
        /// <param name="x">The x-coordinate of the center point of the ellipse from which the arc is taken.</param>
        /// <param name="y">The y-coordinate of the center point of the ellipse from which the arc is taken.</param>
        /// <param name="radiusX">The x-radius of the ellipsis.</param>
        /// <param name="radiusY">The y-radius of the ellipsis.</param>
        /// <param name="rotation">The angle, in degrees, from the x-axis of the current coordinate system to the x-axis of the ellipse.</param>
        /// <param name="startAngle">
        /// The start angle of the elliptical arc prior to the stretch and rotate operations. (0 is at the 3 o'clock position of the arc's circle).
        /// </param>
        /// <param name="sweepAngle">The angle between <paramref name="startAngle"/> and the end of the arc.</param>
        /// <returns>The <see cref="PathBuilder"/>.</returns>
        public PathBuilder AddArc(int x, int y, int radiusX, int radiusY, int rotation, int startAngle, int sweepAngle)
            => this.AddSegment(new ArcLineSegment(new(x, y), new(radiusX, radiusY), rotation, startAngle, sweepAngle));

        /// <summary>
        /// Adds an elliptical arc to the current figure.
        /// </summary>
        /// <param name="x">The x-coordinate of the center point of the ellipse from which the arc is taken.</param>
        /// <param name="y">The y-coordinate of the center point of the ellipse from which the arc is taken.</param>
        /// <param name="radiusX">The x-radius of the ellipsis.</param>
        /// <param name="radiusY">The y-radius of the ellipsis.</param>
        /// <param name="rotation">The angle, in degrees, from the x-axis of the current coordinate system to the x-axis of the ellipse.</param>
        /// <param name="startAngle">
        /// The start angle of the elliptical arc prior to the stretch and rotate operations. (0 is at the 3 o'clock position of the arc's circle).
        /// </param>
        /// <param name="sweepAngle">The angle between <paramref name="startAngle"/> and the end of the arc.</param>
        /// <returns>The <see cref="PathBuilder"/>.</returns>
        public PathBuilder AddArc(float x, float y, float radiusX, float radiusY, float rotation, float startAngle, float sweepAngle)
            => this.AddSegment(new ArcLineSegment(new(x, y), new(radiusX, radiusY), rotation, startAngle, sweepAngle));

        /// <summary>
        /// Starts a new figure but leaves the previous one open.
        /// </summary>
        /// <returns>The <see cref="PathBuilder"/>.</returns>
        public PathBuilder StartFigure()
        {
            if (!this.currentFigure.IsEmpty)
            {
                this.currentFigure = new Figure();
                this.figures.Add(this.currentFigure);
            }
            else
            {
                this.currentFigure.IsClosed = false;
            }

            return this;
        }

        /// <summary>
        /// Closes the current figure.
        /// </summary>
        /// <returns>The <see cref="PathBuilder"/>.</returns>
        public PathBuilder CloseFigure()
        {
            this.currentFigure.IsClosed = true;
            this.StartFigure();

            return this;
        }

        /// <summary>
        /// Closes the current figure.
        /// </summary>
        /// <returns>The <see cref="PathBuilder"/>.</returns>
        public PathBuilder CloseAllFigures()
        {
            foreach (Figure f in this.figures)
            {
                f.IsClosed = true;
            }

            this.CloseFigure();

            return this;
        }

        /// <summary>
        /// Builds a complex polygon from the current working set of working operations.
        /// </summary>
        /// <returns>The current set of operations as a complex polygon</returns>
        public IPath Build()
        {
            IPath[] paths = this.figures.Where(x => !x.IsEmpty).Select(x => x.Build()).ToArray();
            if (paths.Length == 1)
            {
                return paths[0];
            }

            return new ComplexPolygon(paths);
        }

        /// <summary>
        /// Resets this instance, clearing any drawn paths and reseting any transforms.
        /// </summary>
        /// <returns>The <see cref="PathBuilder"/>.</returns>
        public PathBuilder Reset()
        {
            this.Clear();
            this.ResetTransform();
            this.currentPoint = default;

            return this;
        }

        /// <summary>
        /// Clears all drawn paths, Leaving any applied transforms.
        /// </summary>
        public void Clear()
        {
            this.currentFigure = new Figure();
            this.figures.Clear();
            this.figures.Add(this.currentFigure);

            // TODO: Should we reset currentPoint here instead?
        }

        private class Figure
        {
            private readonly List<ILineSegment> segments = new();

            public bool IsClosed { get; set; }

            public bool IsEmpty => this.segments.Count == 0;

            public void AddSegment(ILineSegment segment) => this.segments.Add(segment);

            public IPath Build()
                => this.IsClosed
                ? new Polygon(this.segments.ToArray())
                : new Path(this.segments.ToArray());
        }
    }
}
