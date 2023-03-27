// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Linq;
using System.Numerics;
using SixLabors.ImageSharp.Drawing.Shapes.PolygonClipper;

namespace SixLabors.ImageSharp.Drawing.PolygonClipper
{
    /// <summary>
    /// Wrapper for clipper offset
    /// </summary>
    internal class ClipperOffset
    {
        // To make the floating point polygons compatable with clipper we have to scale them.
        private const float ScalingFactor = 1000F;
        private readonly PolygonOffsetter polygonClipperOffset;

        /// <summary>
        /// Initializes a new instance of the <see cref="ClipperOffset"/> class.
        /// </summary>
        /// <param name="meterLimit">meter limit</param>
        /// <param name="arcTolerance">arc tolerance</param>
        public ClipperOffset(double meterLimit = 2, double arcTolerance = 0.25)
            => this.polygonClipperOffset = new((float)meterLimit, (float)arcTolerance);

        /// <summary>
        /// Calculates an offset polygon based on the given path and width.
        /// </summary>
        /// <param name="width">Width</param>
        /// <returns>path offset</returns>
        /// <exception cref="ClipperException">Calculate: Couldn't calculate the cffset.</exception>
        public ComplexPolygon Execute(float width)
        {
            PathsF solution = new();
            this.polygonClipperOffset.Execute(width * ScalingFactor, solution);

            var polygons = new Polygon[solution.Count];
            const float scale = 1F / ScalingFactor;
            for (int i = 0; i < solution.Count; i++)
            {
                PathF pt = solution[i];

                PointF[] points = pt.Select(p => (PointF)(p * scale)).ToArray();
                polygons[i] = new Polygon(new LinearLineSegment(points));
            }

            return new ComplexPolygon(polygons);
        }

        /// <summary>
        /// Adds the path points
        /// </summary>
        /// <param name="pathPoints">The path points</param>
        /// <param name="jointStyle">Joint Style</param>
        /// <param name="endCapStyle">Endcap Style</param>
        /// <exception cref="ClipperException">AddPath: Invalid Path</exception>
        public void AddPath(ReadOnlySpan<PointF> pathPoints, JointStyle jointStyle, EndCapStyle endCapStyle)
        {
            PathF points = new(pathPoints.Length);
            for (int i = 0; i < pathPoints.Length; i++)
            {
                Vector2 v = pathPoints[i];
                points.Add(v * ScalingFactor);
            }

            this.polygonClipperOffset.AddPath(points, jointStyle, endCapStyle);
        }

        /// <summary>
        /// Adds the path.
        /// </summary>
        /// <param name="path">The path.</param>
        /// <param name="jointStyle">Joint Style</param>
        /// <param name="endCapStyle">Endcap Style</param>
        /// <exception cref="ClipperException">AddPath: Invalid Path</exception>
        public void AddPath(IPath path, JointStyle jointStyle, EndCapStyle endCapStyle)
        {
            Guard.NotNull(path, nameof(path));

            foreach (ISimplePath p in path.Flatten())
            {
                this.AddPath(p, jointStyle, endCapStyle);
            }
        }

        /// <summary>
        /// Adds the path.
        /// </summary>
        /// <param name="path">The path.</param>
        /// <param name="jointStyle">Joint Style</param>
        /// <param name="endCapStyle">Endcap Style</param>
        private void AddPath(ISimplePath path, JointStyle jointStyle, EndCapStyle endCapStyle)
        {
            ReadOnlySpan<PointF> vectors = path.Points.Span;
            this.AddPath(vectors, jointStyle, path.IsClosed ? EndCapStyle.Joined : endCapStyle);
        }
    }
}
