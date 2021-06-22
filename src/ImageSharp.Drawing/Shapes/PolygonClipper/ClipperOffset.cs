// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Linq;
using ClipperLib;

namespace SixLabors.ImageSharp.Drawing.PolygonClipper
{
    /// <summary>
    /// Wrapper for clipper offset
    /// </summary>
    internal class ClipperOffset
    {
        private const float ScalingFactor = 1000.0f;

        private readonly ClipperLib.ClipperOffset innerClipperOffest;
        private readonly object syncRoot = new object();

        /// <summary>
        /// Initializes a new instance of the <see cref="ClipperOffset"/> class.
        /// </summary>
        /// <param name="meterLimit">meter limit</param>
        /// <param name="arcTolerance">arc tolerance</param>
        public ClipperOffset(double meterLimit = 2, double arcTolerance = 0.25) => this.innerClipperOffest = new ClipperLib.ClipperOffset(meterLimit, arcTolerance);

        /// <summary>
        /// Calcualte Offset
        /// </summary>
        /// <param name="width">Width</param>
        /// <returns>path offset</returns>
        /// <exception cref="ClipperException">Execute: Couldn't caculate Offset</exception>
        public ComplexPolygon Execute(float width)
        {
            var tree = new List<List<IntPoint>>();
            lock (this.syncRoot)
            {
                try
                {
                    this.innerClipperOffest.Execute(ref tree, width * ScalingFactor / 2);
                }
                catch (ClipperLib.ClipperException exception)
                {
                    throw new PolygonClipper.ClipperException(exception.Message);
                }
            }

            var polygons = new List<Polygon>();
            foreach (List<IntPoint> pt in tree)
            {
                PointF[] points = pt.Select(p => new PointF(p.X / ScalingFactor, p.Y / ScalingFactor)).ToArray();
                polygons.Add(new Polygon(new LinearLineSegment(points)));
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
            this.AddPath(pathPoints, jointStyle, this.Convert(endCapStyle));

        /// <summary>
        /// Adds the path.
        /// </summary>
        /// <param name="path">The path.</param>
        /// <param name="jointStyle">Joint Style</param>
        /// <param name="endCapStyle">Endcap Style</param>
        /// <exception cref="ClipperException">AddPath: Invalid Path</exception>
        public void AddPath(IPath path, JointStyle jointStyle, EndCapStyle endCapStyle)
        {
            if (path is null)
            {
                throw new ArgumentNullException(nameof(path));
            }

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
            EndType type = path.IsClosed ? EndType.etClosedLine : this.Convert(endCapStyle);
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
            var points = new List<IntPoint>();
            foreach (PointF v in pathPoints)
            {
                points.Add(new IntPoint(v.X * ScalingFactor, v.Y * ScalingFactor));
            }

            lock (this.syncRoot)
            {
                try
                {
                    this.innerClipperOffest.AddPath(points, this.Convert(jointStyle), endCapStyle);
                }
                catch (ClipperLib.ClipperException exception)
                {
                    throw new PolygonClipper.ClipperException(exception.Message);
                }
            }
        }

        private JoinType Convert(JointStyle style)
        => style switch
        {
            JointStyle.Round => JoinType.jtRound,
            JointStyle.Miter => JoinType.jtMiter,
            _ => JoinType.jtSquare,
        };

        private EndType Convert(EndCapStyle style)
        => style switch
        {
            EndCapStyle.Round => EndType.etOpenRound,
            EndCapStyle.Square => EndType.etOpenSquare,
            _ => EndType.etOpenButt,
        };
    }
}
