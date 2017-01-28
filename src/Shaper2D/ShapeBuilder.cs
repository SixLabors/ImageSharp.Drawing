// <copyright file="ShapeBuilder.cs" company="Scott Williams">
// Copyright (c) Scott Williams and contributors.
// Licensed under the Apache License, Version 2.0.
// </copyright>

namespace Shaper2D
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Numerics;
    using System.Threading.Tasks;

    /// <summary>
    /// Allow you to derivatively build shapes and paths.
    /// </summary>
    public class ShapeBuilder
    {
        private readonly List<ILineSegment[]> figures = new List<ILineSegment[]>();
        private readonly List<ILineSegment> segments = new List<ILineSegment>();
        private readonly Matrix3x2 defaultTransform;
        private Vector2 currentPoint = Vector2.Zero;
        private Matrix3x2 currentTransform;

        /// <summary>
        /// Initializes a new instance of the <see cref="ShapeBuilder" /> class.
        /// </summary>
        public ShapeBuilder()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ShapeBuilder"/> class.
        /// </summary>
        /// <param name="defaultTransform">The default transform.</param>
        public ShapeBuilder(Matrix3x2 defaultTransform)
        {
            this.defaultTransform = defaultTransform;
            this.ResetTransform();
        }

        /// <summary>
        /// Sets the translation to be applied to all items to follow being applied to the <see cref="ShapeBuilder"/>.
        /// </summary>
        /// <param name="translation">The translation.</param>
        public void SetTransform(Matrix3x2 translation)
        {
            this.currentTransform = translation;
        }

        /// <summary>
        /// Sets the origin all subsequent point should be relative to.
        /// </summary>
        /// <param name="origin">The origin.</param>
        public void SetOrigin(Vector2 origin)
        {
            this.currentTransform.Translation = origin;
        }

        /// <summary>
        /// Resets the translation to the default.
        /// </summary>
        public void ResetTransform()
        {
            this.currentTransform = this.defaultTransform;
        }

        /// <summary>
        /// Resets the origin to the default.
        /// </summary>
        public void ResetOrigin()
        {
            this.currentTransform.Translation = this.defaultTransform.Translation;
        }

        /// <summary>
        /// Adds the line connecting the current point to the new point.
        /// </summary>
        /// <param name="point">The point.</param>
        public void AddLine(Vector2 point)
        {
            var endPoint = Vector2.Transform(point, this.currentTransform);
            this.segments.Add(new LinearLineSegment(this.currentPoint, endPoint));
            this.currentPoint = endPoint;
        }

        /// <summary>
        /// Adds a series of line segments connecting the current point to the new points.
        /// </summary>
        /// <param name="points">The points.</param>
        public void AddLines(IEnumerable<Vector2> points)
        {
            foreach (var p in points)
            {
                this.AddLine(p);
            }
        }

        /// <summary>
        /// Adds the segment.
        /// </summary>
        /// <param name="segment">The segment.</param>
        public void AddSegment(ILineSegment segment)
        {
            var segments = segment.Transform(this.currentTransform);
            this.segments.Add(segments);
            this.currentPoint = segments.EndPoint;
        }

        /// <summary>
        /// Adds a bezier curve to the current figure joining the last point to the endPoint.
        /// </summary>
        /// <param name="controlPoint1">The control point1.</param>
        /// <param name="controlPoint2">The control point2.</param>
        /// <param name="endPoint">The end point.</param>
        public void AddBezier(Vector2 controlPoint1, Vector2 controlPoint2, Vector2 endPoint)
        {
            endPoint = Vector2.Transform(endPoint, this.currentTransform);
            this.segments.Add(new BezierLineSegment(
                this.currentPoint,
                 Vector2.Transform(controlPoint1, this.currentTransform),
                 Vector2.Transform(controlPoint2, this.currentTransform),
                endPoint));
            this.currentPoint = endPoint;
        }

        /// <summary>
        /// Moves the current point.
        /// </summary>
        /// <param name="point">The point.</param>
        public void MoveTo(Vector2 point)
        {
            if (this.segments.Any())
            {
                this.figures.Add(this.segments.ToArray());
                this.segments.Clear();
            }

            this.currentPoint = Vector2.Transform(point, this.currentTransform);
        }

        /// <summary>
        /// Moves the current point
        /// </summary>
        /// <param name="x">The x.</param>
        /// <param name="y">The y.</param>
        public void MoveTo(float x, float y)
        {
            this.MoveTo(new Vector2(x, y));
        }

        /// <summary>
        /// Builds a complex polygon fromn the current working set of working operations.
        /// </summary>
        /// <returns>The current set of operations as a complex polygon</returns>
        public ComplexPolygon Build()
        {
            var isWorking = this.segments.Any();

            var shapes = new IShape[this.figures.Count + (isWorking ? 1 : 0)];
            var index = 0;
            foreach (var segments in this.figures)
            {
                shapes[index++] = new Polygon(segments);
            }

            if (isWorking)
            {
                shapes[index++] = new Polygon(this.segments.ToArray());
            }

            return new ComplexPolygon(shapes);
        }
    }
}
