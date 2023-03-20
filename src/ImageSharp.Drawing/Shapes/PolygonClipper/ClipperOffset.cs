// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Linq;
using Clipper2Lib;

namespace SixLabors.ImageSharp.Drawing.PolygonClipper
{
    /// <summary>
    /// Wrapper for clipper offset
    /// </summary>
    internal class ClipperOffset
    {
        private const float ScalingFactor = 1000.0f;

        private readonly Clipper2Lib.ClipperOffset innerClipperOffest;
        private readonly object syncRoot = new();

        /// <summary>
        /// Initializes a new instance of the <see cref="ClipperOffset"/> class.
        /// </summary>
        /// <param name="meterLimit">meter limit</param>
        /// <param name="arcTolerance">arc tolerance</param>
        public ClipperOffset(double meterLimit = 2, double arcTolerance = 0.25)
            => this.innerClipperOffest = new Clipper2Lib.ClipperOffset(meterLimit, arcTolerance);

        /// <summary>
        /// Calcualte Offset
        /// </summary>
        /// <param name="width">Width</param>
        /// <returns>path offset</returns>
        /// <exception cref="ClipperException">Calculate: Couldn't caculate Offset</exception>
        public ComplexPolygon Execute(float width)
        {
            Paths64 tree = new();
            lock (this.syncRoot)
            {
                this.innerClipperOffest.Execute(width * ScalingFactor, tree);
            }

            var polygons = new Polygon[tree.Count];
            for (int i = 0; i < tree.Count; i++)
            {
                Path64 pt = tree[i];

                PointF[] points = pt.Select(p => new PointF(p.X / ScalingFactor, p.Y / ScalingFactor)).ToArray();
                polygons[i] = new Polygon(new LinearLineSegment(points));
            }

            return new ComplexPolygon(polygons.ToArray());
        }

        /// <summary>
        /// Adds the path points
        /// </summary>
        /// <param name="pathPoints">The path points</param>
        /// <param name="jointStyle">Joint Style</param>
        /// <param name="endCapStyle">Endcap Style</param>
        /// <exception cref="ClipperException">AddPath: Invalid Path</exception>
        public void AddPath(ReadOnlySpan<PointF> pathPoints, JointStyle jointStyle, EndCapStyle endCapStyle) =>
            this.AddPath(pathPoints, jointStyle, Convert(endCapStyle));

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
            EndType type = path.IsClosed ? EndType.Joined : Convert(endCapStyle);
            this.AddPath(vectors, jointStyle, type);
        }

        /// <summary>
        /// Adds the path.
        /// </summary>
        /// <param name="pathPoints">The path points</param>
        /// <param name="jointStyle">Joint Style</param>
        /// <param name="endCapStyle">Endcap Style</param>
        /// <exception cref="ClipperException">AddPath: Invalid Path</exception>
        private void AddPath(ReadOnlySpan<PointF> pathPoints, JointStyle jointStyle, EndType endCapStyle)
        {
            Path64 points = new();
            foreach (PointF v in pathPoints)
            {
                points.Add(new Point64(v.X * ScalingFactor, v.Y * ScalingFactor));
            }

            // TODO: Why are we locking?
            lock (this.syncRoot)
            {
                this.innerClipperOffest.AddPath(points, Convert(jointStyle), endCapStyle);
            }
        }

        private static JoinType Convert(JointStyle style)
            => style switch
            {
                JointStyle.Round => JoinType.Round,
                JointStyle.Miter => JoinType.Miter,
                _ => JoinType.Square,
            };

        private static EndType Convert(EndCapStyle style)
            => style switch
            {
                EndCapStyle.Round => EndType.Round,
                EndCapStyle.Square => EndType.Square,
                _ => EndType.Butt,
            };
    }
}
