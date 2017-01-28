// <copyright file="BezierLineSegment.cs" company="Scott Williams">
// Copyright (c) Scott Williams and contributors.
// Licensed under the Apache License, Version 2.0.
// </copyright>

namespace Shaper2D
{
    using System;
    using System.Collections.Immutable;
    using System.Linq;
    using System.Numerics;

    /// <summary>
    /// Represents a line segment that colonists of control points that will be rendered as a cubic bezier curve
    /// </summary>
    /// <seealso cref="Shaper2D.ILineSegment" />
    public class BezierLineSegment : ILineSegment
    {
        // code for this taken from <see href="http://devmag.org.za/2011/04/05/bzier-curves-a-tutorial/"/>

        /// <summary>
        /// The segments per curve.
        /// </summary>
        private const int SegmentsPerCurve = 50;

        /// <summary>
        /// The line points.
        /// </summary>
        private readonly ImmutableArray<Vector2> linePoints;
        private readonly Vector2[] controlPoints;

        /// <summary>
        /// Initializes a new instance of the <see cref="BezierLineSegment"/> class.
        /// </summary>
        /// <param name="points">The points.</param>
        public BezierLineSegment(Vector2[] points)
        {
            Guard.NotNull(points, nameof(points));
            Guard.MustBeGreaterThanOrEqualTo(points.Length, 4, nameof(points));

            int correctPointCount = (points.Length - 1) % 3;
            if (correctPointCount != 0)
            {
                throw new ArgumentOutOfRangeException(nameof(points), "points must be a multiple of 3 plus 1 long.");
            }

            this.controlPoints = points.ToArray();
            this.linePoints = this.GetDrawingPoints(points);

            this.EndPoint = points[points.Length - 1];
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="BezierLineSegment"/> class.
        /// </summary>
        /// <param name="start">The start.</param>
        /// <param name="controlPoint1">The control point1.</param>
        /// <param name="controlPoint2">The control point2.</param>
        /// <param name="end">The end.</param>
        /// <param name="additionalPoints">The additional points.</param>
        public BezierLineSegment(Vector2 start, Vector2 controlPoint1, Vector2 controlPoint2, Vector2 end, params Vector2[] additionalPoints)
            : this(new[] { start, controlPoint1, controlPoint2, end }.Merge(additionalPoints))
        {
        }

        /// <summary>
        /// Gets the end point.
        /// </summary>
        /// <value>
        /// The end point.
        /// </value>
        public Vector2 EndPoint { get; private set; }

        /// <summary>
        /// Returns the current <see cref="ILineSegment" /> a simple linear path.
        /// </summary>
        /// <returns>
        /// Returns the current <see cref="ILineSegment" /> as simple linear path.
        /// </returns>
        public ImmutableArray<Vector2> Flatten()
        {
            return this.linePoints;
        }

        /// <summary>
        /// Transforms the current LineSegment using specified matrix.
        /// </summary>
        /// <param name="matrix">The matrix.</param>
        /// <returns>A line segment with the matrix applied to it.</returns>
        public ILineSegment Transform(Matrix3x2 matrix)
        {
            if (matrix.IsIdentity)
            {
                // no transform to apply skip it
                return this;
            }

            var points = new Vector2[this.controlPoints.Length];
            var i = 0;
            foreach (var p in this.controlPoints)
            {
                points[i++] = Vector2.Transform(p, matrix);
            }

            return new BezierLineSegment(points);
        }

        /// <summary>
        /// Returns the drawing points along the line.
        /// </summary>
        /// <param name="controlPoints">The control points.</param>
        /// <returns>
        /// The <see cref="T:Vector2[]"/>.
        /// </returns>
        private ImmutableArray<Vector2> GetDrawingPoints(Vector2[] controlPoints)
        {
            // TODO we need to calculate an optimal SegmentsPerCurve value depending on the calculated length of this curve
            int curveCount = (controlPoints.Length - 1) / 3;
            int finalPointCount = (SegmentsPerCurve * curveCount) + 1; // we have SegmentsPerCurve for each curve plus the origon point;

            Vector2[] drawingPoints = new Vector2[finalPointCount];

            int position = 0;
            int targetPoint = controlPoints.Length - 3;
            for (int i = 0; i < targetPoint; i += 3)
            {
                Vector2 p0 = controlPoints[i];
                Vector2 p1 = controlPoints[i + 1];
                Vector2 p2 = controlPoints[i + 2];
                Vector2 p3 = controlPoints[i + 3];

                // only do this for the first end point. When i != 0, this coincides with the end point of the previous segment,
                if (i == 0)
                {
                    drawingPoints[position++] = this.CalculateBezierPoint(0, p0, p1, p2, p3);
                }

                for (int j = 1; j <= SegmentsPerCurve; j++)
                {
                    float t = j / (float)SegmentsPerCurve;
                    drawingPoints[position++] = this.CalculateBezierPoint(t, p0, p1, p2, p3);
                }
            }

            return ImmutableArray.Create(drawingPoints);
        }

        /// <summary>
        /// Calculates the bezier point along the line.
        /// </summary>
        /// <param name="t">The position within the line.</param>
        /// <param name="p0">The p 0.</param>
        /// <param name="p1">The p 1.</param>
        /// <param name="p2">The p 2.</param>
        /// <param name="p3">The p 3.</param>
        /// <returns>
        /// The <see cref="Vector2"/>.
        /// </returns>
        private Vector2 CalculateBezierPoint(float t, Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3)
        {
            float u = 1 - t;
            float tt = t * t;
            float uu = u * u;
            float uuu = uu * u;
            float ttt = tt * t;

            Vector2 p = uuu * p0; // first term

            p += 3 * uu * t * p1; // second term
            p += 3 * u * tt * p2; // third term
            p += ttt * p3; // fourth term

            return p;
        }
    }
}
