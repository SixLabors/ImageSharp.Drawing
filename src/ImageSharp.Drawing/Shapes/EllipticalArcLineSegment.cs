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
        private PointF center;
        private float firstRadius;
        private float secondRadius;
        private float rotation;
        private float startAngle;
        private float sweepAngle;

        /// <summary>
        /// Initializes a new instance of the <see cref="EllipticalArcLineSegment"/> class.
        /// </summary>
        /// <param name="center"> The center point of the ellipsis that the arc is a part of</param>
        /// <param name="firstRadius">First radius of the ellipsis</param>
        /// <param name="secondRadius">Second radius of the ellipsis</param>
        /// <param name="rotation">The rotation of First radius to the X-Axis</param>
        /// <param name="startAngle">The Start angle of the ellipsis</param>
        /// <param name="sweepAngle"> The sweeping angle of the arc</param>
        public EllipticalArcLineSegment(PointF center, float firstRadius, float secondRadius, float rotation, float startAngle, float sweepAngle)
        {
            Guard.MustBeGreaterThan(firstRadius, 0, nameof(firstRadius));
            Guard.MustBeGreaterThan(secondRadius, 0, nameof(secondRadius));
            Guard.MustBeGreaterThanOrEqualTo(rotation, 0, nameof(rotation));
            Guard.MustBeGreaterThanOrEqualTo(startAngle, 0, nameof(startAngle));
            Guard.MustBeGreaterThanOrEqualTo(sweepAngle, 0, nameof(sweepAngle));
            this.center = center;
            this.firstRadius = firstRadius;
            this.secondRadius = secondRadius;
            this.rotation = rotation % 360;
            this.startAngle = startAngle % 360;
            this.sweepAngle = sweepAngle;
            if (sweepAngle > 360)
            {
                sweepAngle = 360;
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
        /// <param name="matrix">The matrix.</param>
        /// <returns>A line segment with the matrix applied to it.</returns>
        public EllipticalArcLineSegment Transform(Matrix3x2 matrix)
        {
            if (matrix.IsIdentity)
            {
                return this;
            }

            // TODO
            return this;
        }

        /// <summary>
        /// Transforms the current LineSegment using specified matrix.
        /// </summary>
        /// <param name="matrix">The matrix.</param>
        /// <returns>A line segment with the matrix applied to it.</returns>
        ILineSegment ILineSegment.Transform(Matrix3x2 matrix) => this.Transform(matrix);

        private PointF[] GetDrawingPoints()
        {
            var points = new List<PointF>();

            float startX = (float)((this.firstRadius * Math.Sin(Math.PI * this.startAngle / 180) * Math.Cos(Math.PI * this.rotation / 180)) - (this.secondRadius * Math.Cos(Math.PI * this.startAngle / 180) * Math.Sin(Math.PI * this.rotation / 180)) + this.center.X);
            float startY = (float)((this.firstRadius * Math.Sin(Math.PI * this.startAngle / 180) * Math.Sin(Math.PI * this.rotation / 180)) + (this.secondRadius * Math.Cos(Math.PI * this.startAngle / 180) * Math.Cos(Math.PI * this.rotation / 180)) + this.center.Y);
            points.Add(new PointF(startX, startY));
            for (float i = this.startAngle; i < this.startAngle + this.sweepAngle; i++)
            {
                float end = i + 1;
                if (end >= this.startAngle + this.sweepAngle)
                {
                    end = this.startAngle + this.sweepAngle;
                }

                points.AddRange(this.GetDrawingPoints( i, end,0));
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

            float startX = (float)((this.firstRadius * Math.Sin(Math.PI * start / 180) * Math.Cos(Math.PI * this.rotation / 180)) - (this.secondRadius * Math.Cos(Math.PI * start / 180) * Math.Sin(Math.PI * this.rotation / 180)) + this.center.X);
            float startY = (float)((this.firstRadius * Math.Sin(Math.PI * start / 180) * Math.Sin(Math.PI * this.rotation / 180)) + (this.secondRadius * Math.Cos(Math.PI * start / 180) * Math.Cos(Math.PI * this.rotation / 180)) + this.center.Y);

            float endX = (float)((this.firstRadius * Math.Sin(Math.PI * end / 180) * Math.Cos(Math.PI * this.rotation / 180)) - (this.secondRadius * Math.Cos(Math.PI * end / 180) * Math.Sin(Math.PI * this.rotation / 180)) + this.center.X);
            float endY = (float)((this.firstRadius * Math.Sin(Math.PI * end / 180) * Math.Sin(Math.PI * this.rotation / 180)) + (this.secondRadius * Math.Cos(Math.PI * end / 180) * Math.Cos(Math.PI * this.rotation / 180)) + this.center.Y);
            if ((new Vector2(endX, endY) - new Vector2(startX, startY)).LengthSquared() < MinimumSqrDistance)
            {
                points.Add(new PointF(endX, endY));
            }
            else
            {
                float mid = start + ((end - start) / 2);
                points.AddRange(this.GetDrawingPoints(start, mid, depth + 1));
                points.AddRange(this.GetDrawingPoints(mid, end, depth + 1));
            }

            return points;
        }

        /// <summary>
        /// Returns the current <see cref="ILineSegment" /> a simple linear path.
        /// </summary>
        /// <returns>
        /// Returns the current <see cref="ILineSegment" /> as simple linear path.
        /// </returns>
        public ReadOnlyMemory<PointF> Flatten()
        {
            return this.linePoints;
        }
    }
}
