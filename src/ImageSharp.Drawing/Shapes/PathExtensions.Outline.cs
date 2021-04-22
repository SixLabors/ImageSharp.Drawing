// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using ClipperLib;

namespace SixLabors.ImageSharp.Drawing
{
    /// <content>
    /// Path extensions to generate outlines of paths.
    /// </content>
    public static partial class PathExtensions
    {
        private const double MiterOffsetDelta = 20;
        private const float ScalingFactor = 1000.0f;

        /// <summary>
        /// Generates a outline of the path with alternating on and off segments based on the pattern.
        /// </summary>
        /// <param name="path">the path to outline</param>
        /// <param name="width">The final width outline</param>
        /// <param name="pattern">The pattern made of multiples of the width.</param>
        /// <returns>A new path representing the outline.</returns>
        public static ComplexPolygon GenerateOutline(this IPath path, float width, float[] pattern)
            => path.GenerateOutline(width, new ReadOnlySpan<float>(pattern));

        /// <summary>
        /// Generates a outline of the path with alternating on and off segments based on the pattern.
        /// </summary>
        /// <param name="path">the path to outline</param>
        /// <param name="width">The final width outline</param>
        /// <param name="pattern">The pattern made of multiples of the width.</param>
        /// <returns>A new path representing the outline.</returns>
        public static ComplexPolygon GenerateOutline(this IPath path, float width, ReadOnlySpan<float> pattern)
            => path.GenerateOutline(width, pattern, false);

        /// <summary>
        /// Generates a outline of the path with alternating on and off segments based on the pattern.
        /// </summary>
        /// <param name="path">the path to outline</param>
        /// <param name="width">The final width outline</param>
        /// <param name="pattern">The pattern made of multiples of the width.</param>
        /// <param name="startOff">Weather the first item in the pattern is on or off.</param>
        /// <returns>A new path representing the outline.</returns>
        public static ComplexPolygon GenerateOutline(this IPath path, float width, float[] pattern, bool startOff)
            => path.GenerateOutline(width, new ReadOnlySpan<float>(pattern), startOff);

        /// <summary>
        /// Generates a outline of the path with alternating on and off segments based on the pattern.
        /// </summary>
        /// <param name="path">the path to outline</param>
        /// <param name="width">The final width outline</param>
        /// <param name="pattern">The pattern made of multiples of the width.</param>
        /// <param name="startOff">Weather the first item in the pattern is on or off.</param>
        /// <returns>A new path representing the outline.</returns>
        public static ComplexPolygon GenerateOutline(this IPath path, float width, ReadOnlySpan<float> pattern, bool startOff)
            => GenerateOutline(path, width, pattern, startOff, JointStyle.Square, EndCapStyle.Butt);

        /// <summary>
        /// Generates a outline of the path with alternating on and off segments based on the pattern.
        /// </summary>
        /// <param name="path">the path to outline</param>
        /// <param name="width">The final width outline</param>
        /// <param name="pattern">The pattern made of multiples of the width.</param>
        /// <param name="startOff">Weather the first item in the pattern is on or off.</param>
        /// <param name="jointStyle">The style to render the joints.</param>
        /// <param name="patternSectionCapStyle">The style to render between sections of the specified pattern.</param>
        /// <returns>A new path representing the outline.</returns>
        public static ComplexPolygon GenerateOutline(this IPath path, float width, ReadOnlySpan<float> pattern, bool startOff, JointStyle jointStyle = JointStyle.Square, EndCapStyle patternSectionCapStyle = EndCapStyle.Butt)
        {
            if (pattern.Length < 2)
            {
                return path.GenerateOutline(width, jointStyle: jointStyle);
            }

            JoinType style = Convert(jointStyle);
            EndType patternSectionCap = Convert(patternSectionCapStyle);

            IEnumerable<ISimplePath> paths = path.Flatten();

            var offset = new ClipperOffset()
            {
                MiterLimit = MiterOffsetDelta
            };

            var buffer = new List<IntPoint>(3);
            foreach (ISimplePath p in paths)
            {
                bool online = !startOff;
                float targetLength = pattern[0] * width;
                int patternPos = 0;
                ReadOnlySpan<PointF> points = p.Points.Span;

                // Create a new list of points representing the new outline
                int pCount = points.Length;
                if (!p.IsClosed)
                {
                    pCount--;
                }

                int i = 0;
                Vector2 currentPoint = points[0];

                while (i < pCount)
                {
                    int next = (i + 1) % points.Length;
                    Vector2 targetPoint = points[next];
                    float distToNext = Vector2.Distance(currentPoint, targetPoint);
                    if (distToNext > targetLength)
                    {
                        // find a point between the 2
                        float t = targetLength / distToNext;

                        Vector2 point = (currentPoint * (1 - t)) + (targetPoint * t);
                        buffer.Add(currentPoint.ToPoint());
                        buffer.Add(point.ToPoint());

                        // we now inset a line joining
                        if (online)
                        {
                            offset.AddPath(buffer, style, patternSectionCap);
                        }

                        online = !online;

                        buffer.Clear();

                        currentPoint = point;

                        // next length
                        patternPos = (patternPos + 1) % pattern.Length;
                        targetLength = pattern[patternPos] * width;
                    }
                    else if (distToNext <= targetLength)
                    {
                        buffer.Add(currentPoint.ToPoint());
                        currentPoint = targetPoint;
                        i++;
                        targetLength -= distToNext;
                    }
                }

                if (buffer.Count > 0)
                {
                    if (p.IsClosed)
                    {
                        buffer.Add(points[0].ToPoint());
                    }
                    else
                    {
                        buffer.Add(points[points.Length - 1].ToPoint());
                    }

                    if (online)
                    {
                        offset.AddPath(buffer, style, patternSectionCap);
                    }

                    online = !online;

                    buffer.Clear();
                    patternPos = (patternPos + 1) % pattern.Length;
                    targetLength = pattern[patternPos] * width;
                }
            }

            return ExecuteOutliner(width, offset);
        }

        /// <summary>
        /// Generates a solid outline of the path.
        /// </summary>
        /// <param name="path">the path to outline</param>
        /// <param name="width">The final width outline</param>
        /// <returns>A new path representing the outline.</returns>
        public static ComplexPolygon GenerateOutline(this IPath path, float width) => GenerateOutline(path, width, JointStyle.Square, EndCapStyle.Butt);

        /// <summary>
        /// Generates a solid outline of the path.
        /// </summary>
        /// <param name="path">the path to outline</param>
        /// <param name="width">The final width outline</param>
        /// <param name="jointStyle">The style to render the joints.</param>
        /// <param name="endCapStyle">The style to render the end caps of open paths (ignored on closed paths).</param>
        /// <returns>A new path representing the outline.</returns>
        public static ComplexPolygon GenerateOutline(this IPath path, float width, JointStyle jointStyle = JointStyle.Square, EndCapStyle endCapStyle = EndCapStyle.Square)
        {
            var offset = new ClipperOffset()
            {
                MiterLimit = MiterOffsetDelta
            };

            JoinType style = Convert(jointStyle);
            EndType openEndCapStyle = Convert(endCapStyle);

            // Pattern can be applied to the path by cutting it into segments
            IEnumerable<ISimplePath> paths = path.Flatten();
            foreach (ISimplePath p in paths)
            {
                ReadOnlySpan<Vector2> vectors = MemoryMarshal.Cast<PointF, Vector2>(p.Points.Span);
                var points = new List<IntPoint>(vectors.Length);
                foreach (Vector2 v in vectors)
                {
                    points.Add(new IntPoint(v.X * ScalingFactor, v.Y * ScalingFactor));
                }

                EndType type = p.IsClosed ? EndType.etClosedLine : openEndCapStyle;

                offset.AddPath(points, style, type);
            }

            return ExecuteOutliner(width, offset);
        }

        private static ComplexPolygon ExecuteOutliner(float width, ClipperOffset offset)
        {
            var tree = new List<List<IntPoint>>();
            offset.Execute(ref tree, width * ScalingFactor / 2);
            var polygons = new List<Polygon>();
            foreach (List<IntPoint> pt in tree)
            {
                PointF[] points = pt.Select(p => new PointF(p.X / ScalingFactor, p.Y / ScalingFactor)).ToArray();
                polygons.Add(new Polygon(new LinearLineSegment(points)));
            }

            return new ComplexPolygon(polygons.ToArray());
        }

        private static IntPoint ToPoint(this PointF vector)
            => new IntPoint(vector.X * ScalingFactor, vector.Y * ScalingFactor);

        private static IntPoint ToPoint(this Vector2 vector)
            => new IntPoint(vector.X * ScalingFactor, vector.Y * ScalingFactor);

        private static JoinType Convert(JointStyle style)
            => style switch
            {
                JointStyle.Round => JoinType.jtRound,
                JointStyle.Miter => JoinType.jtMiter,
                _ => JoinType.jtSquare,
            };

        private static EndType Convert(EndCapStyle style)
            => style switch
            {
                EndCapStyle.Round => EndType.etOpenRound,
                EndCapStyle.Square => EndType.etOpenSquare,
                _ => EndType.etOpenButt,
            };
    }
}
