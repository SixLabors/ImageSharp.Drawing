// <copyright file="PathBuilder.cs" company="Scott Williams">
// Copyright (c) Scott Williams and contributors.
// Licensed under the Apache License, Version 2.0.
// </copyright>

namespace SixLabors.Shapes
{
    using SixLabors.Primitives;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Numerics;
    using System.Threading.Tasks;

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
        public void SetTransform(Matrix3x2 translation)
        {
            this.setTransform = translation;
            this.currentTransform = this.setTransform * this.defaultTransform;
        }

        /// <summary>
        /// Sets the origin all subsequent point should be relative to.
        /// </summary>
        /// <param name="origin">The origin.</param>
        public void SetOrigin(PointF origin)
        {
            // the new origin should be transofrmed based on the default transform
            this.setTransform.Translation = origin;
            this.currentTransform = this.setTransform * this.defaultTransform;
        }

        /// <summary>
        /// Resets the translation to the default.
        /// </summary>
        public void ResetTransform()
        {
            this.setTransform = Matrix3x2.Identity;
            this.currentTransform = this.setTransform * this.defaultTransform;
        }

        /// <summary>
        /// Resets the origin to the default.
        /// </summary>
        public void ResetOrigin()
        {
            this.setTransform.Translation = Vector2.Zero;
            this.currentTransform = this.setTransform * this.defaultTransform;
        }

        /// <summary>
        /// Adds the line connecting the current point to the new point.
        /// </summary>
        /// <param name="start">The start.</param>
        /// <param name="end">The end.</param>
        public void AddLine(PointF start, PointF end)
        {
            end = PointF.Transform(end, this.currentTransform);
            start = PointF.Transform(start, this.currentTransform);
            this.currentFigure.AddSegment(new LinearLineSegment(start, end));
        }

        /// <summary>
        /// Adds the line connecting the current point to the new point.
        /// </summary>
        /// <param name="x1">The x1.</param>
        /// <param name="y1">The y1.</param>
        /// <param name="x2">The x2.</param>
        /// <param name="y2">The y2.</param>
        public void AddLine(float x1, float y1, float x2, float y2)
        {
            this.AddLine(new PointF(x1, y1), new PointF(x2, y2));
        }

        /// <summary>
        /// Adds a series of line segments connecting the current point to the new points.
        /// </summary>
        /// <param name="points">The points.</param>
        public void AddLines(IEnumerable<PointF> points)
        {
            Guard.NotNull(points, nameof(points));

            this.AddLines(points.ToArray());
        }

        /// <summary>
        /// Adds a series of line segments connecting the current point to the new points.
        /// </summary>
        /// <param name="points">The points.</param>
        public void AddLines(params PointF[] points)
        {
            this.AddSegment(new LinearLineSegment(points));
        }

        /// <summary>
        /// Adds the segment.
        /// </summary>
        /// <param name="segment">The segment.</param>
        public void AddSegment(ILineSegment segment)
        {
            this.currentFigure.AddSegment(segment.Transform(this.currentTransform));
        }

        /// <summary>
        /// Adds a quadratic bezier curve to the current figure joining the last point to the endPoint.
        /// </summary>
        /// <param name="startPoint">The start point.</param>
        /// <param name="controlPoint">The control point1.</param>
        /// <param name="endPoint">The end point.</param>
        public void AddBezier(PointF startPoint, PointF controlPoint, PointF endPoint)
        {
            Vector2 startPointVector = startPoint;
            Vector2 controlPointVector = controlPoint;
            Vector2 endPointVector = endPoint;

            Vector2 c1 = (((controlPointVector - startPointVector) * 2) / 3) + startPointVector;
            Vector2 c2 = (((controlPointVector - endPointVector) * 2) / 3) + endPointVector;

            this.AddBezier(startPointVector, c1, c2, endPoint);
        }

        /// <summary>
        /// Adds a cubic bezier curve to the current figure joining the last point to the endPoint.
        /// </summary>
        /// <param name="startPoint">The start point.</param>
        /// <param name="controlPoint1">The control point1.</param>
        /// <param name="controlPoint2">The control point2.</param>
        /// <param name="endPoint">The end point.</param>
        public void AddBezier(PointF startPoint, PointF controlPoint1, PointF controlPoint2, PointF endPoint)
        {
            this.currentFigure.AddSegment(new CubicBezierLineSegment(
                PointF.Transform(startPoint, this.currentTransform),
                PointF.Transform(controlPoint1, this.currentTransform),
                PointF.Transform(controlPoint2, this.currentTransform),
                PointF.Transform(endPoint, this.currentTransform)));
        }

        /// <summary>
        /// Starts a new figure but leaves the previous one open.
        /// </summary>
        public void StartFigure()
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
        }

        /// <summary>
        /// Closes the current figure.
        /// </summary>
        public void CloseFigure()
        {
            this.currentFigure.IsClosed = true;
            this.StartFigure();
        }

        /// <summary>
        /// Closes the current figure.
        /// </summary>
        public void CloseAllFigures()
        {
            foreach (Figure f in this.figures)
            {
                f.IsClosed = true;
            }

            this.CloseFigure();
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
        public void Reset()
        {
            this.Clear();
            this.ResetTransform();
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
            private List<ILineSegment> segments = new List<ILineSegment>();

            public bool IsClosed { get; set; } = false;

            public bool IsEmpty => !this.segments.Any();

            public void AddSegment(ILineSegment segment)
            {
                this.segments.Add(segment);
            }

            public IPath Build()
            {
                if (this.IsClosed)
                {
                    return new Polygon(this.segments.ToArray());
                }

                return new Path(this.segments.ToArray());
            }
        }
    }
}
