using ClipperLib;
using SixLabors.Primitives;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;

namespace SixLabors.Shapes
{
    /// <summary>
    /// Path extensions to generate outlines of paths.
    /// </summary>
    public static class Outliner
    {
        private const float ScalingFactor = 1000.0f;

        /// <summary>
        /// Generates a outline of the path with alternating on and off segments based on the pattern.
        /// </summary>
        /// <param name="path">the path to outline</param>
        /// <param name="width">The final width outline</param>
        /// <param name="pattern">The pattern made of multiples of the width.</param>
        /// <returns>A new path representing the outline.</returns>
        public static IPath GenerateOutline(this IPath path, float width, float[] pattern)
        {
            return path.GenerateOutline(width, pattern, false);
        }

        /// <summary>
        /// Generates a outline of the path with alternating on and off segments based on the pattern.
        /// </summary>
        /// <param name="path">the path to outline</param>
        /// <param name="width">The final width outline</param>
        /// <param name="pattern">The pattern made of multiples of the width.</param>
        /// <param name="startOff">Weather the first item in the pattern is on or off.</param>
        /// <returns>A new path representing the outline.</returns>
        public static IPath GenerateOutline(this IPath path, float width, float[] pattern, bool startOff)
        {
            if (pattern == null || pattern.Length < 2)
            {
                return path.GenerateOutline(width);
            }

            IEnumerable<ISimplePath> paths = path.Flatten();

            ClipperOffset offset = new ClipperOffset();

            List<IntPoint> buffer = new List<IntPoint>(3);
            foreach (ISimplePath p in paths)
            {
                bool online = !startOff;
                float targetLength = pattern[0] * width;
                int patternPos = 0;
                // create a new list of points representing the new outline
                int pCount = p.Points.Count;
                if (!p.IsClosed)
                {
                    pCount--;
                }
                int i = 0;
                Vector2 currentPoint = p.Points[0];
                
                while (i < pCount)
                {
                    int next = (i + 1) % p.Points.Count;
                    Vector2 targetPoint = p.Points[next];
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
                            offset.AddPath(buffer, JoinType.jtSquare, EndType.etOpenButt);
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
                        buffer.Add(p.Points.First().ToPoint());
                    }
                    else
                    {
                        buffer.Add(p.Points.Last().ToPoint());
                    }

                    if (online)
                    {
                        offset.AddPath(buffer, JoinType.jtSquare, EndType.etOpenButt);
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
        public static IPath GenerateOutline(this IPath path, float width)
        {
            ClipperOffset offset = new ClipperLib.ClipperOffset();

            //pattern can be applied to the path by cutting it into segments
            IEnumerable<ISimplePath> paths = path.Flatten();
            foreach (ISimplePath p in paths)
            {
                IReadOnlyList<PointF> vectors = p.Points;
                List<IntPoint> points = new List<ClipperLib.IntPoint>(vectors.Count);
                foreach (Vector2 v in vectors)
                {
                    points.Add(new IntPoint(v.X * ScalingFactor, v.Y * ScalingFactor));
                }

                EndType type = p.IsClosed ? EndType.etClosedLine : EndType.etOpenButt;


                offset.AddPath(points, JoinType.jtMiter, type);
            }

            return ExecuteOutliner(width, offset);
        }

        private static IPath ExecuteOutliner(float width, ClipperOffset offset)
        {
            List<List<IntPoint>> tree = new List<List<IntPoint>>();
            offset.Execute(ref tree, width * ScalingFactor / 2);
            List<Polygon> polygons = new List<Polygon>();
            foreach (List<IntPoint> pt in tree)
            {
                PointF[] points = pt.Select(p => new PointF(p.X / ScalingFactor, p.Y / ScalingFactor)).ToArray();
                polygons.Add(new Polygon(new LinearLineSegment(points)));
            }

            return new ComplexPolygon(polygons.ToArray());
        }

        private static IntPoint ToPoint(this PointF vector)
        {
            return new IntPoint(vector.X * ScalingFactor, vector.Y * ScalingFactor);
        }

        private static IntPoint ToPoint(this Vector2 vector)
        {
            return new IntPoint(vector.X * ScalingFactor, vector.Y * ScalingFactor);
        }
    }
}
