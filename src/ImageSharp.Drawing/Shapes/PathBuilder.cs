// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace SixLabors.ImageSharp.Drawing
{
    /// <summary>
    /// Allow you to derivatively build shapes and paths.
    /// </summary>
    public class PathBuilder
    {
        private readonly List<Figure> figures = new List<Figure>();
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
        /// <returns>The <see cref="PathBuilder"/></returns>
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
        /// <returns>The <see cref="PathBuilder"/></returns>
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
        /// <returns>The <see cref="PathBuilder"/></returns>
        public PathBuilder ResetTransform()
        {
            this.setTransform = Matrix3x2.Identity;
            this.currentTransform = this.setTransform * this.defaultTransform;

            return this;
        }

        /// <summary>
        /// Resets the origin to the default.
        /// </summary>
        /// <returns>The <see cref="PathBuilder"/></returns>
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
        public void MoveTo(Vector2 point)
        {
            this.StartFigure();
            this.currentPoint = PointF.Transform(point, this.currentTransform);
        }

        /// <summary>
        /// Draws the line connecting the current the current point to the new point.
        /// </summary>
        /// <param name="point">The point.</param>
        /// <returns>The <see cref="PathBuilder"/></returns>
        public PathBuilder LineTo(PointF point)
            => this.AddLine(this.currentPoint, point);

        /// <summary>
        /// Adds the line connecting the current point to the new point.
        /// </summary>
        /// <param name="start">The start.</param>
        /// <param name="end">The end.</param>
        /// <returns>The <see cref="PathBuilder"/></returns>
        public PathBuilder AddLine(PointF start, PointF end)
            => this.AddSegment(new LinearLineSegment(start, end));

        /// <summary>
        /// Adds the line connecting the current point to the new point.
        /// </summary>
        /// <param name="x1">The x1.</param>
        /// <param name="y1">The y1.</param>
        /// <param name="x2">The x2.</param>
        /// <param name="y2">The y2.</param>
        /// <returns>The <see cref="PathBuilder"/></returns>
        public PathBuilder AddLine(float x1, float y1, float x2, float y2)
            => this.AddLine(new PointF(x1, y1), new PointF(x2, y2));

        /// <summary>
        /// Adds a series of line segments connecting the current point to the new points.
        /// </summary>
        /// <param name="points">The points.</param>
        /// <returns>The <see cref="PathBuilder"/></returns>
        public PathBuilder AddLines(IEnumerable<PointF> points)
        {
            if (points is null)
            {
                throw new ArgumentNullException(nameof(points));
            }

            this.AddLines(points.ToArray());

            return this;
        }

        /// <summary>
        /// Adds a series of line segments connecting the current point to the new points.
        /// </summary>
        /// <param name="points">The points.</param>
        /// <returns>The <see cref="PathBuilder"/></returns>
        public PathBuilder AddLines(params PointF[] points)
            => this.AddSegment(new LinearLineSegment(points));

        /// <summary>
        /// Adds the segment.
        /// </summary>
        /// <param name="segment">The segment.</param>
        /// <returns>The <see cref="PathBuilder"/></returns>
        public PathBuilder AddSegment(ILineSegment segment)
        {
            segment = segment.Transform(this.currentTransform);
            this.currentFigure.AddSegment(segment);
            this.currentPoint = segment.EndPoint;
            return this;
        }

        /// <summary>
        /// Draws a quadratics bezier from the current point  to the <paramref name="point"/>
        /// </summary>
        /// <param name="secondControlPoint">The second control point.</param>
        /// <param name="point">The point.</param>
        /// <returns>The <see cref="PathBuilder"/></returns>
        public PathBuilder QuadraticBezierTo(Vector2 secondControlPoint, Vector2 point)
            => this.AddBezier(this.currentPoint, secondControlPoint, point);

        /// <summary>
        /// Draws a quadratics bezier from the current point  to the <paramref name="point"/>
        /// </summary>
        /// <param name="secondControlPoint">The second control point.</param>
        /// <param name="thirdControlPoint">The third control point.</param>
        /// <param name="point">The point.</param>
        /// <returns>The <see cref="PathBuilder"/></returns>
        public PathBuilder CubicBezierTo(Vector2 secondControlPoint, Vector2 thirdControlPoint, Vector2 point)
            => this.AddBezier(this.currentPoint, secondControlPoint, thirdControlPoint, point);

        /// <summary>
        /// Adds a quadratic bezier curve to the current figure joining the last point to the endPoint.
        /// </summary>
        /// <param name="startPoint">The start point.</param>
        /// <param name="controlPoint">The control point1.</param>
        /// <param name="endPoint">The end point.</param>
        /// <returns>The <see cref="PathBuilder"/></returns>
        public PathBuilder AddBezier(PointF startPoint, PointF controlPoint, PointF endPoint)
        {
            Vector2 startPointVector = startPoint;
            Vector2 controlPointVector = controlPoint;
            Vector2 endPointVector = endPoint;

            Vector2 c1 = (((controlPointVector - startPointVector) * 2) / 3) + startPointVector;
            Vector2 c2 = (((controlPointVector - endPointVector) * 2) / 3) + endPointVector;

            this.AddBezier(startPointVector, c1, c2, endPoint);

            return this;
        }

        /// <summary>
        /// Adds a cubic bezier curve to the current figure joining the last point to the endPoint.
        /// </summary>
        /// <param name="startPoint">The start point.</param>
        /// <param name="controlPoint1">The control point1.</param>
        /// <param name="controlPoint2">The control point2.</param>
        /// <param name="endPoint">The end point.</param>
        /// <returns>The <see cref="PathBuilder"/></returns>
        public PathBuilder AddBezier(PointF startPoint, PointF controlPoint1, PointF controlPoint2, PointF endPoint)
            => this.AddSegment(new CubicBezierLineSegment(startPoint, controlPoint1, controlPoint2, endPoint));

        /// <summary>
        /// <para>
        /// Adds an arc to the current figure. The arc curves from the last point to <paramref name="point"/>,
        /// choosing one of four possible routes: clockwise or counterclockwise, and smaller or larger.
        /// </para>
        /// <para>
        /// Th arc sweep is always less than 360 degrees. The method appends a line
        /// to the last point if either radii are zero, or if last point is equal to <paramref name="point"/>.
        /// In addition the method scales the radii to fit last point and <paramref name="point"/> if both
        /// are greater than zero but too small to describe an arc.
        /// </para>
        /// </summary>
        /// <param name="radiusX">X radius of the ellipsis.</param>
        /// <param name="radiusY">Y radius of the ellipsis.</param>
        /// <param name="rotation">The rotation along the X-axis; measured in degrees clockwise.</param>
        /// <param name="largeArc">Whether to use a larger arc.</param>
        /// <param name="sweep">Whether to move the arc clockwise or counter-clockwise.</param>
        /// <param name="point">The end point.</param>
        /// <returns>The <see cref="PathBuilder"/>.</returns>
        public PathBuilder ArcTo(float radiusX, float radiusY, float rotation, bool largeArc, bool sweep, Vector2 point)
        {
            // If rx = 0 or ry = 0 then this arc is treated as a straight line segment
            // joining the endpoints.
            // http://www.w3.org/TR/SVG/implnote.html#ArcOutOfRangeParameters
            if (radiusX == 0 || radiusY == 0)
            {
                return this.LineTo(point);
            }

            // If the current point and target point for the arc are identical, it should be treated as a
            // zero length path. This ensures continuity in animations.
            Vector2 start = this.currentPoint;
            if (start == point)
            {
                return this.LineTo(point);
            }

            radiusX = MathF.Abs(radiusX);
            radiusY = MathF.Abs(radiusY);

            // Check if the radii are big enough to draw the arc, scale radii if not.
            // http://www.w3.org/TR/SVG/implnote.html#ArcCorrectionOutOfRangeRadii
            Vector2 midPointDistance = (start - point) * .5F;
            Matrix3x2 matrix = Matrix3x2Extensions.CreateRotationDegrees(-rotation);
            var xy = Vector2.Transform(midPointDistance, matrix);

            float squareRx = radiusX * radiusX;
            float squareRy = radiusY * radiusY;
            float squareX = xy.X * xy.X;
            float squareY = xy.Y * xy.Y;

            float radiiScale = (squareX / squareRx) + (squareY / squareRy);
            if (radiiScale > 1)
            {
                radiusX = MathF.Sqrt(radiiScale) * radiusX;
                radiusY = MathF.Sqrt(radiiScale) * radiusY;
            }

            // Compute center
            matrix.M11 = 1 / radiusX;
            matrix.M22 = 1 / radiusY;
            matrix *= Matrix3x2Extensions.CreateRotationDegrees(-rotation);

            var unit1 = Vector2.Transform(start, matrix);
            var unit2 = Vector2.Transform(point, matrix);
            Vector2 delta = unit2 - unit1;

            float dot = Vector2.Dot(delta, delta);
            float scaleFactorSquared = MathF.Max((1 / dot) - .25F, 0F);
            float scaleFactor = MathF.Sqrt(scaleFactorSquared);

            if (largeArc == sweep)
            {
                scaleFactor = -scaleFactor;
            }

            delta *= scaleFactor;
            var deltaXY = new Vector2(-delta.Y, delta.X);
            Vector2 scaledCenter = unit1 + unit2;
            scaledCenter *= .5F;
            scaledCenter += deltaXY;
            unit1 -= scaledCenter;
            unit2 -= scaledCenter;

            // Compute θ and Δθ
            float theta1 = MathF.Atan2(unit1.Y, unit1.X);
            float theta2 = MathF.Atan2(unit2.Y, unit2.X);

            // startAngle copied from https://github.com/UkooLabs/SVGSharpie/blob/5f7be977d487d416c4cf62578d6342b799a5c507/src/UkooLabs.SVGSharpie.ImageSharp/Shapes/ArcLineSegment.cs#L160
            float startAngle = -GeometryUtilities.RadianToDegree(VectorAngle(Vector2.UnitX, (xy - deltaXY) / new Vector2(radiusX, radiusY)));
            float sweepAngle = GeometryUtilities.RadianToDegree(theta2 - theta1);

            // Fix the range to −360° < Δθ < 360°
            if (!sweep && sweepAngle > 0)
            {
                sweepAngle -= 360;
            }

            if (sweep && sweepAngle < 0)
            {
                sweepAngle += 360;
            }

            // Skia notes an issue with very small sweep angles.
            // Epsilon is based upon their fix.
            if (MathF.Abs(sweepAngle) < 0.001F)
            {
                return this.LineTo(point);
            }

            var center = Vector2.Lerp(start, point, .5F);
            foreach (ILineSegment item in EllipticArcToBezierCurveInner(start, center, new(radiusX, radiusY), rotation, startAngle, sweepAngle))
            {
                this.AddSegment(item);
            }

            return this;

            // TODO: Fix this.
            // return this.AddEllipticalArc(center, radiusX, radiusY, rotation, startAngle, sweepAngle);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float Clamp(float value, float min, float max)
        {
            if (value > max)
            {
                return max;
            }

            if (value < min)
            {
                return min;
            }

            return value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float VectorAngle(Vector2 u, Vector2 v)
        {
            float dot = Vector2.Dot(u, v);
            float length = u.Length() * v.Length();
            float angle = (float)Math.Acos(Clamp(dot / length, -1, 1)); // floating point precision, slightly over values appear
            if (((u.X * v.Y) - (u.X * v.Y)) < 0)
            {
                angle = -angle;
            }

            return angle;
        }

        private static IEnumerable<ILineSegment> EllipticArcToBezierCurveInner(Vector2 from, Vector2 center, Vector2 radius, float xAngle, float startAngle, float deltaAngle)
        {
            xAngle = GeometryUtilities.DegreeToRadian(xAngle);
            startAngle = GeometryUtilities.DegreeToRadian(startAngle);
            deltaAngle = GeometryUtilities.DegreeToRadian(deltaAngle);

            float s = startAngle;
            float e = s + deltaAngle;
            bool neg = e < s;
            float sign = neg ? -1 : 1;
            float remain = Math.Abs(e - s);

            Vector2 prev = EllipticArcPoint(center, radius, xAngle, s);

            while (remain > 1e-05f)
            {
                float step = (float)Math.Min(remain, Math.PI / 4);
                float signStep = step * sign;

                Vector2 p1 = prev;
                Vector2 p2 = EllipticArcPoint(center, radius, xAngle, s + signStep);

                float alphaT = (float)Math.Tan(signStep / 2);
                float alpha = (float)(Math.Sin(signStep) * (Math.Sqrt(4 + (3 * alphaT * alphaT)) - 1) / 3);
                Vector2 q1 = p1 + (alpha * EllipticArcDerivative(radius, xAngle, s));
                Vector2 q2 = p2 - (alpha * EllipticArcDerivative(radius, xAngle, s + signStep));

                yield return new CubicBezierLineSegment(from, q1, q2, p2);
                from = p2;

                s += signStep;
                remain -= step;
                prev = p2;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vector2 EllipticArcDerivative(Vector2 r, float xAngle, float t)
            => new(
                (-r.X * MathF.Cos(xAngle) * MathF.Sin(t)) - (r.Y * MathF.Sin(xAngle) * MathF.Cos(t)),
                (-r.X * MathF.Sin(xAngle) * MathF.Sin(t)) + (r.Y * MathF.Cos(xAngle) * MathF.Cos(t)));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vector2 EllipticArcPoint(Vector2 c, Vector2 r, float xAngle, float t)
            => new(
                c.X + (r.X * MathF.Cos(xAngle) * MathF.Cos(t)) - (r.Y * MathF.Sin(xAngle) * MathF.Sin(t)),
                c.Y + (r.X * MathF.Sin(xAngle) * MathF.Cos(t)) + (r.Y * MathF.Cos(xAngle) * MathF.Sin(t)));

        /// <summary>
        /// Adds an elliptical arc to the current figure.
        /// </summary>
        /// <param name="rect"> A <see cref="RectangleF"/> that represents the rectangular bounds of the ellipse from which the arc is taken.</param>
        /// <param name="rotation">The rotation of (<paramref name="rect"/>, measured in degrees clockwise.</param>
        /// <param name="startAngle">The start angle of the ellipsis, measured in degrees anticlockwise from the Y-axis.</param>
        /// <param name="sweepAngle"> The angle between (<paramref name="startAngle"/> and the end of the arc. </param>
        /// <returns>The <see cref="PathBuilder"/></returns>
        public PathBuilder AddEllipticalArc(RectangleF rect, float rotation, float startAngle, float sweepAngle)
            => this.AddEllipticalArc((rect.Right + rect.Left) / 2, (rect.Bottom + rect.Top) / 2, rect.Width / 2, rect.Height / 2, rotation, startAngle, sweepAngle);

        /// <summary>
        /// Adds an elliptical arc to the current figure.
        /// </summary>
        /// <param name="rect"> A <see cref="Rectangle"/> that represents the rectangular bounds of the ellipse from which the arc is taken.</param>
        /// <param name="rotation">The rotation of (<paramref name="rect"/>, measured in degrees clockwise.</param>
        /// <param name="startAngle">The start angle of the ellipsis, measured in degrees anticlockwise from the Y-axis.</param>
        /// <param name="sweepAngle"> The angle between (<paramref name="startAngle"/> and the end of the arc. </param>
        /// <returns>The <see cref="PathBuilder"/></returns>
        public PathBuilder AddEllipticalArc(Rectangle rect, int rotation, int startAngle, int sweepAngle)
            => this.AddEllipticalArc((float)(rect.Right + rect.Left) / 2, (float)(rect.Bottom + rect.Top) / 2, (float)rect.Width / 2, (float)rect.Height / 2, rotation, startAngle, sweepAngle);

        /// <summary>
        /// Adds an elliptical arc to the current figure.
        /// </summary>
        /// <param name="center"> The center <see cref="PointF"/> of the ellips from which the arc is taken.</param>
        /// <param name="radiusX">X radius of the ellipsis.</param>
        /// <param name="radiusY">Y radius of the ellipsis.</param>
        /// <param name="rotation">The rotation of (<paramref name="radiusX"/> to the X-axis and (<paramref name="radiusY"/> to the Y-axis, measured in degrees clockwise.</param>
        /// <param name="startAngle">The start angle of the ellipsis, measured in degrees anticlockwise from the Y-axis.</param>
        /// <param name="sweepAngle"> The angle between (<paramref name="startAngle"/> and the end of the arc. </param>
        /// <returns>The <see cref="PathBuilder"/></returns>
        public PathBuilder AddEllipticalArc(PointF center, float radiusX, float radiusY, float rotation, float startAngle, float sweepAngle)
            => this.AddEllipticalArc(center.X, center.Y, radiusX, radiusY, rotation, startAngle, sweepAngle);

        /// <summary>
        /// Adds an elliptical arc to the current figure.
        /// </summary>
        /// <param name="center"> The center <see cref="Point"/> of the ellips from which the arc is taken.</param>
        /// <param name="radiusX">X radius of the ellipsis.</param>
        /// <param name="radiusY">Y radius of the ellipsis.</param>
        /// <param name="rotation">The rotation of (<paramref name="radiusX"/> to the X-axis and (<paramref name="radiusY"/> to the Y-axis, measured in degrees clockwise.</param>
        /// <param name="startAngle">The start angle of the ellipsis, measured in degrees anticlockwise from the Y-axis.</param>
        /// <param name="sweepAngle"> The angle between (<paramref name="startAngle"/> and the end of the arc. </param>
        /// <returns>The <see cref="PathBuilder"/></returns>
        public PathBuilder AddEllipticalArc(Point center, int radiusX, int radiusY, int rotation, int startAngle, int sweepAngle)
            => this.AddEllipticalArc(center.X, center.Y, radiusX, radiusY, rotation, startAngle, sweepAngle);

        /// <summary>
        /// Adds an elliptical arc to the current figure.
        /// </summary>
        /// <param name="x"> The x-coordinate of the center point of the ellips from which the arc is taken.</param>
        /// <param name="y"> The y-coordinate of the center point of the ellips from which the arc is taken.</param>
        /// <param name="radiusX">X radius of the ellipsis.</param>
        /// <param name="radiusY">Y radius of the ellipsis.</param>
        /// <param name="rotation">The rotation of (<paramref name="radiusX"/> to the X-axis and (<paramref name="radiusY"/> to the Y-axis, measured in degrees clockwise.</param>
        /// <param name="startAngle">The start angle of the ellipsis, measured in degrees anticlockwise from the Y-axis.</param>
        /// <param name="sweepAngle"> The angle between (<paramref name="startAngle"/> and the end of the arc. </param>
        /// <returns>The <see cref="PathBuilder"/></returns>
        public PathBuilder AddEllipticalArc(int x, int y, int radiusX, int radiusY, int rotation, int startAngle, int sweepAngle)
            => this.AddSegment(new EllipticalArcLineSegment(x, y, radiusX, radiusY, rotation, startAngle, sweepAngle, Matrix3x2.Identity));

        /// <summary>
        /// Adds an elliptical arc to the current figure.
        /// </summary>
        /// <param name="x"> The x-coordinate of the center point of the ellips from which the arc is taken.</param>
        /// <param name="y"> The y-coordinate of the center point of the ellips from which the arc is taken.</param>
        /// <param name="radiusX">X radius of the ellipsis.</param>
        /// <param name="radiusY">Y radius of the ellipsis.</param>
        /// <param name="rotation">The rotation of (<paramref name="radiusX"/> to the X-axis and (<paramref name="radiusY"/> to the Y-axis, measured in degrees clockwise.</param>
        /// <param name="startAngle">The start angle of the ellipsis, measured in degrees anticlockwise from the Y-axis.</param>
        /// <param name="sweepAngle"> The angle between (<paramref name="startAngle"/> and the end of the arc. </param>
        /// <returns>The <see cref="PathBuilder"/></returns>
        public PathBuilder AddEllipticalArc(float x, float y, float radiusX, float radiusY, float rotation, float startAngle, float sweepAngle)
            => this.AddSegment(new EllipticalArcLineSegment(x, y, radiusX, radiusY, rotation, startAngle, sweepAngle, Matrix3x2.Identity));

        /// <summary>
        /// Starts a new figure but leaves the previous one open.
        /// </summary>
        /// <returns>The <see cref="PathBuilder"/></returns>
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
        /// <returns>The <see cref="PathBuilder"/></returns>
        public PathBuilder CloseFigure()
        {
            this.currentFigure.IsClosed = true;
            this.StartFigure();

            return this;
        }

        /// <summary>
        /// Closes the current figure.
        /// </summary>
        /// <returns>The <see cref="PathBuilder"/></returns>
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
        /// Builds a complex polygon fromn the current working set of working operations.
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
        /// <returns>The <see cref="PathBuilder"/></returns>
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
        }

        private class Figure
        {
            private readonly List<ILineSegment> segments = new List<ILineSegment>();

            public bool IsClosed { get; set; } = false;

            public bool IsEmpty => this.segments.Count == 0;

            public void AddSegment(ILineSegment segment) => this.segments.Add(segment);

            public IPath Build()
                => this.IsClosed
                ? new Polygon(this.segments.ToArray())
                : new Path(this.segments.ToArray());
        }
    }
}
