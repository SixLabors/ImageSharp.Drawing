// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Numerics;

namespace SixLabors.ImageSharp.Drawing
{
    /// <summary>
    /// Represents a line segment that contains radii and angles that will be rendered as a elliptical arc
    /// </summary>
    /// <seealso cref="ILineSegment" />
    public sealed class EllipticalArcLineSegment : ILineSegment
    {
        private const float MinimumSqrDistance = 1.75f;
        private readonly PointF[] linePoints;
        private readonly float x;
        private readonly float y;
        private readonly float radiusX;
        private readonly float radiusY;
        private readonly float rotation;
        private readonly float startAngle;
        private readonly float sweepAngle;
        private readonly Matrix3x2 transformation;

        /// <summary>
        /// Initializes a new instance of the <see cref="EllipticalArcLineSegment"/> class.
        /// </summary>
        /// <param name="x"> The x-coordinate of the center point of the ellips from which the arc is taken.</param>
        /// <param name="y"> The y-coordinate of the center point of the ellips from which the arc is taken.</param>
        /// <param name="radiusX">X radius of the ellipsis.</param>
        /// <param name="radiusY">Y radius of the ellipsis.</param>
        /// <param name="rotation">The rotation of (<paramref name="radiusX"/> to the X-axis and (<paramref name="radiusY"/> to the Y-axis, measured in degrees anticlockwise.</param>
        /// <param name="startAngle">The Start angle of the ellipsis, measured in degrees anticlockwise from the Y-axis.</param>
        /// <param name="sweepAngle"> The angle between (<paramref name="startAngle"/> and the end of the arc. </param>
        /// <param name="transformation">The Tranformation matrix, that should be used on the arc.</param>
        public EllipticalArcLineSegment(float x, float y, float radiusX, float radiusY, float rotation, float startAngle, float sweepAngle, Matrix3x2 transformation)
        {
            Guard.MustBeGreaterThan(radiusX, 0, nameof(radiusX));
            Guard.MustBeGreaterThan(radiusY, 0, nameof(radiusY));
            this.x = x;
            this.y = y;
            this.radiusX = radiusX;
            this.radiusY = radiusY;
            this.rotation = -rotation % 360;
            this.startAngle = startAngle % 360;
            this.transformation = transformation;
            this.sweepAngle = sweepAngle;
            if (sweepAngle > 360)
            {
                this.sweepAngle = 360;
            }

            if (sweepAngle < -360)
            {
                this.sweepAngle = -360;
            }

            this.linePoints = this.GetDrawingPoints();
            this.EndPoint = this.linePoints[this.linePoints.Length - 1];
        }

        /// <summary>
        /// Gets the end point.
        /// </summary>
        /// <value>
        /// The end point.
        /// </value>
        public PointF EndPoint { get; }

        /// <summary>
        /// Transforms the current LineSegment using specified matrix.
        /// </summary>
        /// <param name="matrix">The transformation matrix.</param>
        /// <returns>A line segment with the matrix applied to it.</returns>
        public EllipticalArcLineSegment Transform(Matrix3x2 matrix)
        {
            if (matrix.IsIdentity)
            {
                return this;
            }

            return new EllipticalArcLineSegment(this.x, this.y, this.radiusX, this.radiusY, this.rotation, this.startAngle, this.sweepAngle, Matrix3x2.Multiply(this.transformation, matrix));
        }

        /// <summary>
        /// Transforms the current LineSegment using specified matrix.
        /// </summary>
        /// <param name="matrix">The matrix.</param>
        /// <returns>A line segment with the matrix applied to it.</returns>
        ILineSegment ILineSegment.Transform(Matrix3x2 matrix) => this.Transform(matrix);

        private PointF[] GetDrawingPoints()
        {
            var points = new List<PointF>()
            {
            this.CalculatePoint(this.startAngle)
            };
            if (this.sweepAngle < 0)
            {
                for (float i = this.startAngle; i > this.startAngle + this.sweepAngle; i--)
                {
                    float end = i - 1;
                    if (end <= this.startAngle + this.sweepAngle)
                    {
                        end = this.startAngle + this.sweepAngle;
                    }

                    points.AddRange(this.GetDrawingPoints(i, end, 0));
                }
            }
            else
            {
                for (float i = this.startAngle; i < this.startAngle + this.sweepAngle; i++)
                {
                    float end = i + 1;
                    if (end >= this.startAngle + this.sweepAngle)
                    {
                        end = this.startAngle + this.sweepAngle;
                    }

                    points.AddRange(this.GetDrawingPoints(i, end, 0));
                }
            }

            return points.ToArray();
        }

        private List<PointF> GetDrawingPoints(float start, float end, int depth)
        {
            if (depth > 1000)
            {
                return new List<PointF>();
            }

            var points = new List<PointF>();

            PointF startP = this.CalculatePoint(start);
            PointF endP = this.CalculatePoint(end);
            if ((new Vector2(endP.X, endP.Y) - new Vector2(startP.X, startP.Y)).LengthSquared() < MinimumSqrDistance)
            {
                points.Add(endP);
            }
            else
            {
                float mid = start + ((end - start) / 2);
                points.AddRange(this.GetDrawingPoints(start, mid, depth + 1));
                points.AddRange(this.GetDrawingPoints(mid, end, depth + 1));
            }

            return points;
        }

        private PointF CalculatePoint(float angle)
        {
            float x = (this.radiusX * MathF.Sin(MathF.PI * angle / 180) * MathF.Cos(MathF.PI * this.rotation / 180)) - (this.radiusY * MathF.Cos(MathF.PI * angle / 180) * MathF.Sin(MathF.PI * this.rotation / 180)) + this.x;
            float y = (this.radiusX * MathF.Sin(MathF.PI * angle / 180) * MathF.Sin(MathF.PI * this.rotation / 180)) + (this.radiusY * MathF.Cos(MathF.PI * angle / 180) * MathF.Cos(MathF.PI * this.rotation / 180)) + this.y;
            var currPoint = new PointF(x, y);
            return PointF.Transform(currPoint, this.transformation);
        }

        /// <summary>
        /// Returns the current <see cref="ILineSegment" /> a simple linear path.
        /// </summary>
        /// <returns>
        /// Returns the current <see cref="ILineSegment" /> as simple linear path.
        /// </returns>
        public ReadOnlyMemory<PointF> Flatten() => this.linePoints;
    }
}
