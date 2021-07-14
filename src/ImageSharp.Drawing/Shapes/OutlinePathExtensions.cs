// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Numerics;
using SixLabors.ImageSharp.Drawing.PolygonClipper;

namespace SixLabors.ImageSharp.Drawing
{
    /// <summary>
    /// Path extensions to generate outlines of paths.
    /// </summary>
    public static class OutlinePathExtensions
    {
        private const double MiterOffsetDelta = 20;

        /// <summary>
        /// Generates a outline of the path with alternating on and off segments based on the pattern.
        /// </summary>
        /// <param name="path">the path to outline</param>
        /// <param name="width">The final width outline</param>
        /// <param name="pattern">The pattern made of multiples of the width.</param>
        /// <returns>A new path representing the outline.</returns>
        /// <exception cref="ClipperException">Calculate: Couldn't caculate Offset</exception>
        public static IPath GenerateOutline(this IPath path, float width, float[] pattern)
            => path.GenerateOutline(width, new ReadOnlySpan<float>(pattern));

        /// <summary>
        /// Generates a outline of the path with alternating on and off segments based on the pattern.
        /// </summary>
        /// <param name="path">the path to outline</param>
        /// <param name="width">The final width outline</param>
        /// <param name="pattern">The pattern made of multiples of the width.</param>
        /// <returns>A new path representing the outline.</returns>
        /// <exception cref="ClipperException">Calculate: Couldn't caculate Offset</exception>
        public static IPath GenerateOutline(this IPath path, float width, ReadOnlySpan<float> pattern)
            => path.GenerateOutline(width, pattern, false);

        /// <summary>
        /// Generates a outline of the path with alternating on and off segments based on the pattern.
        /// </summary>
        /// <param name="path">the path to outline</param>
        /// <param name="width">The final width outline</param>
        /// <param name="pattern">The pattern made of multiples of the width.</param>
        /// <param name="startOff">Weather the first item in the pattern is on or off.</param>
        /// <returns>A new path representing the outline.</returns>
        /// <exception cref="ClipperException">Calculate: Couldn't caculate Offset</exception>
        public static IPath GenerateOutline(this IPath path, float width, float[] pattern, bool startOff)
            => path.GenerateOutline(width, new ReadOnlySpan<float>(pattern), startOff);

        /// <summary>
        /// Generates a outline of the path with alternating on and off segments based on the pattern.
        /// </summary>
        /// <param name="path">the path to outline</param>
        /// <param name="width">The final width outline</param>
        /// <param name="pattern">The pattern made of multiples of the width.</param>
        /// <param name="startOff">Weather the first item in the pattern is on or off.</param>
        /// <returns>A new path representing the outline.</returns>
        /// <exception cref="ClipperException">Calculate: Couldn't caculate Offset</exception>
        public static IPath GenerateOutline(this IPath path, float width, ReadOnlySpan<float> pattern, bool startOff)
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
        /// <exception cref="ClipperException">Calculate: Couldn't caculate Offset</exception>
        public static IPath GenerateOutline(this IPath path, float width, ReadOnlySpan<float> pattern, bool startOff, JointStyle jointStyle = JointStyle.Square, EndCapStyle patternSectionCapStyle = EndCapStyle.Butt)
        {
            if (pattern.Length < 2)
            {
                return path.GenerateOutline(width, jointStyle: jointStyle);
            }

            IEnumerable<ISimplePath> paths = path.Flatten();

            var offset = new ClipperOffset(MiterOffsetDelta);
            var buffer = new List<PointF>();
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
                        buffer.Add(currentPoint);
                        buffer.Add(point);

                        // we now inset a line joining
                        if (online)
                        {
                            offset.AddPath(new ReadOnlySpan<PointF>(buffer.ToArray()), jointStyle, patternSectionCapStyle);
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
                        buffer.Add(currentPoint);
                        currentPoint = targetPoint;
                        i++;
                        targetLength -= distToNext;
                    }
                }

                if (buffer.Count > 0)
                {
                    if (p.IsClosed)
                    {
                        buffer.Add(points[0]);
                    }
                    else
                    {
                        buffer.Add(points[points.Length - 1]);
                    }

                    if (online)
                    {
                        offset.AddPath(new ReadOnlySpan<PointF>(buffer.ToArray()), jointStyle, patternSectionCapStyle);
                    }

                    online = !online;

                    buffer.Clear();
                    patternPos = (patternPos + 1) % pattern.Length;
                    targetLength = pattern[patternPos] * width;
                }
            }

            return offset.Execute(width);
        }

        /// <summary>
        /// Generates a solid outline of the path.
        /// </summary>
        /// <param name="path">the path to outline</param>
        /// <param name="width">The final width outline</param>
        /// <returns>A new path representing the outline.</returns>
        /// <exception cref="ClipperException">Calculate: Couldn't caculate Offset</exception>
        public static IPath GenerateOutline(this IPath path, float width) => GenerateOutline(path, width, JointStyle.Square, EndCapStyle.Butt);

        /// <summary>
        /// Generates a solid outline of the path.
        /// </summary>
        /// <param name="path">the path to outline</param>
        /// <param name="width">The final width outline</param>
        /// <param name="jointStyle">The style to render the joints.</param>
        /// <param name="endCapStyle">The style to render the end caps of open paths (ignored on closed paths).</param>
        /// <returns>A new path representing the outline.</returns>
        /// <exception cref="ClipperException">Calculate: Couldn't caculate Offset</exception>
        public static IPath GenerateOutline(this IPath path, float width, JointStyle jointStyle = JointStyle.Square, EndCapStyle endCapStyle = EndCapStyle.Square)
        {
            var offset = new ClipperOffset(MiterOffsetDelta);
            offset.AddPath(path, jointStyle, endCapStyle);

            return offset.Execute(width);
        }
    }
}
