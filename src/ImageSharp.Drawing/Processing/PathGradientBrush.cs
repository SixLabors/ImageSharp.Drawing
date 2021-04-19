// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Drawing.Processing
{
    /// <summary>
    /// Provides an implementation of a brush for painting gradients between multiple color positions in 2D coordinates.
    /// </summary>
    public sealed class PathGradientBrush : IBrush
    {
        private readonly IList<Edge> edges;
        private readonly Color centerColor;
        private readonly bool hasSpecialCenterColor;

        /// <summary>
        /// Initializes a new instance of the <see cref="PathGradientBrush"/> class.
        /// </summary>
        /// <param name="points">Points that constitute a polygon that represents the gradient area.</param>
        /// <param name="colors">Array of colors that correspond to each point in the polygon.</param>
        public PathGradientBrush(PointF[] points, Color[] colors)
        {
            if (points == null)
            {
                throw new ArgumentNullException(nameof(points));
            }

            if (points.Length < 3)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(points),
                    "There must be at least 3 lines to construct a path gradient brush.");
            }

            if (colors == null)
            {
                throw new ArgumentNullException(nameof(colors));
            }

            if (colors.Length == 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(colors),
                    "One or more color is needed to construct a path gradient brush.");
            }

            int size = points.Length;

            var lines = new ILineSegment[size];

            for (int i = 0; i < size; i++)
            {
                lines[i] = new LinearLineSegment(points[i % size], points[(i + 1) % size]);
            }

            this.centerColor = CalculateCenterColor(colors);

            Color ColorAt(int index) => colors[index % colors.Length];

            this.edges = lines.Select(s => new Path(s))
                .Select((path, i) => new Edge(path, ColorAt(i), ColorAt(i + 1))).ToList();
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="PathGradientBrush"/> class.
        /// </summary>
        /// <param name="points">Points that constitute a polygon that represents the gradient area.</param>
        /// <param name="colors">Array of colors that correspond to each point in the polygon.</param>
        /// <param name="centerColor">Color at the center of the gradient area to which the other colors converge.</param>
        public PathGradientBrush(PointF[] points, Color[] colors, Color centerColor)
            : this(points, colors)
        {
            this.centerColor = centerColor;
            this.hasSpecialCenterColor = true;
        }

        /// <inheritdoc />
        public BrushApplicator<TPixel> CreateApplicator<TPixel>(
            Configuration configuration,
            GraphicsOptions options,
            ImageFrame<TPixel> source,
            RectangleF region)
            where TPixel : unmanaged, IPixel<TPixel>
            => new PathGradientBrushApplicator<TPixel>(
                configuration,
                options,
                source,
                this.edges,
                this.centerColor,
                this.hasSpecialCenterColor);

        private static Color CalculateCenterColor(Color[] colors)
        {
            if (colors == null)
            {
                throw new ArgumentNullException(nameof(colors));
            }

            if (colors.Length == 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(colors),
                    "One or more color is needed to construct a path gradient brush.");
            }

            return new Color(colors.Select(c => (Vector4)c).Aggregate((p1, p2) => p1 + p2) / colors.Length);
        }

        private static float DistanceBetween(PointF p1, PointF p2) => ((Vector2)(p2 - p1)).Length();

        private struct Intersection
        {
            public Intersection(PointF point, float distance)
            {
                this.Point = point;
                this.Distance = distance;
            }

            public PointF Point { get; }

            public float Distance { get; }
        }

        /// <summary>
        /// An edge of the polygon that represents the gradient area.
        /// </summary>
        private class Edge
        {
            private readonly float length;

            public Edge(Path path, Color startColor, Color endColor)
            {
                this.Path = path;

                Vector2[] points = path.LineSegments.SelectMany(s => s.Flatten().ToArray()).Select(p => (Vector2)p).ToArray();

                this.Start = points[0];
                this.StartColor = (Vector4)startColor;

                this.End = points.Last();
                this.EndColor = (Vector4)endColor;

                this.length = DistanceBetween(this.End, this.Start);
            }

            public Path Path { get; }

            public PointF Start { get; }

            public Vector4 StartColor { get; }

            public PointF End { get; }

            public Vector4 EndColor { get; }

            public Intersection? FindIntersection(
                PointF start,
                PointF end,
                Span<PointF> intersections,
                Span<PointOrientation> orientations)
            {
                int pathIntersections = this.Path.FindIntersections(
                    start,
                    end,
                    intersections.Slice(0, this.Path.MaxIntersections),
                    orientations.Slice(0, this.Path.MaxIntersections));

                if (pathIntersections == 0)
                {
                    return null;
                }

                intersections = intersections.Slice(0, pathIntersections);

                PointF minPoint = intersections[0];
                var min = new Intersection(minPoint, ((Vector2)(minPoint - start)).LengthSquared());
                for (int i = 1; i < intersections.Length; i++)
                {
                    PointF point = intersections[i];
                    var current = new Intersection(point, ((Vector2)(point - start)).LengthSquared());

                    if (min.Distance > current.Distance)
                    {
                        min = current;
                    }
                }

                return min;
            }

            public Vector4 ColorAt(float distance)
            {
                float ratio = this.length > 0 ? distance / this.length : 0;

                return Vector4.Lerp(this.StartColor, this.EndColor, ratio);
            }

            public Vector4 ColorAt(PointF point) => this.ColorAt(DistanceBetween(point, this.Start));
        }

        /// <summary>
        /// The path gradient brush applicator.
        /// </summary>
        private class PathGradientBrushApplicator<TPixel> : BrushApplicator<TPixel>
            where TPixel : unmanaged, IPixel<TPixel>
        {
            private readonly PointF center;

            private readonly Vector4 centerColor;

            private readonly bool hasSpecialCenterColor;

            private readonly float maxDistance;

            private readonly int maxIntersections;

            private readonly IList<Edge> edges;

            private readonly TPixel centerPixel;

            private readonly TPixel transparentPixel;

            /// <summary>
            /// Initializes a new instance of the <see cref="PathGradientBrushApplicator{TPixel}"/> class.
            /// </summary>
            /// <param name="configuration">The configuration instance to use when performing operations.</param>
            /// <param name="options">The graphics options.</param>
            /// <param name="source">The source image.</param>
            /// <param name="edges">Edges of the polygon.</param>
            /// <param name="centerColor">Color at the center of the gradient area to which the other colors converge.</param>
            /// <param name="hasSpecialCenterColor">Whether the center color is different from a smooth gradient between the edges.</param>
            public PathGradientBrushApplicator(
                Configuration configuration,
                GraphicsOptions options,
                ImageFrame<TPixel> source,
                IList<Edge> edges,
                Color centerColor,
                bool hasSpecialCenterColor)
                : base(configuration, options, source)
            {
                this.edges = edges;
                PointF[] points = edges.Select(s => s.Start).ToArray();

                this.center = points.Aggregate((p1, p2) => p1 + p2) / edges.Count;
                this.centerColor = (Vector4)centerColor;
                this.hasSpecialCenterColor = hasSpecialCenterColor;
                this.centerPixel = centerColor.ToPixel<TPixel>();
                this.maxDistance = points.Select(p => (Vector2)(p - this.center)).Max(d => d.Length());
                this.maxIntersections = this.edges.Max(e => e.Path.MaxIntersections);
                this.transparentPixel = Color.Transparent.ToPixel<TPixel>();
            }

            internal TPixel this[int x, int y, Span<PointF> intersections, Span<PointOrientation> orientations]
            {
                get
                {
                    var point = new PointF(x, y);

                    if (point == this.center)
                    {
                        return this.centerPixel;
                    }

                    if (this.edges.Count == 3 && !this.hasSpecialCenterColor)
                    {
                        if (!FindPointOnTriangle(
                            this.edges[0].Start,
                            this.edges[1].Start,
                            this.edges[2].Start,
                            point,
                            out float u,
                            out float v))
                        {
                            return this.transparentPixel;
                        }

                        Vector4 pointColor = ((1 - u - v) * this.edges[0].StartColor)
                            + (u * this.edges[0].EndColor)
                            + (v * this.edges[2].StartColor);

                        TPixel px = default;
                        px.FromScaledVector4(pointColor);
                        return px;
                    }

                    var direction = Vector2.Normalize(point - this.center);
                    PointF end = point + (PointF)(direction * this.maxDistance);

                    (Edge edge, Intersection? info) = this.FindIntersection(point, end, intersections, orientations);

                    if (!info.HasValue)
                    {
                        return this.transparentPixel;
                    }

                    PointF intersection = info.Value.Point;
                    Vector4 edgeColor = edge.ColorAt(intersection);

                    float length = DistanceBetween(intersection, this.center);
                    float ratio = length > 0 ? DistanceBetween(intersection, point) / length : 0;

                    var color = Vector4.Lerp(edgeColor, this.centerColor, ratio);

                    TPixel pixel = default;
                    pixel.FromScaledVector4(color);
                    return pixel;
                }
            }

            /// <inheritdoc />
            public override void Apply(Span<float> scanline, int x, int y)
            {
                MemoryAllocator memoryAllocator = this.Configuration.MemoryAllocator;
                using IMemoryOwner<float> amountBuffer = memoryAllocator.Allocate<float>(scanline.Length);
                using IMemoryOwner<TPixel> overlayBuffer = memoryAllocator.Allocate<TPixel>(scanline.Length);
                using IMemoryOwner<PointF> intersectionsBuffer = memoryAllocator.Allocate<PointF>(this.maxIntersections);
                using IMemoryOwner<PointOrientation> orientationsBuffer = memoryAllocator.Allocate<PointOrientation>(this.maxIntersections);

                Span<float> amountSpan = amountBuffer.Memory.Span;
                Span<TPixel> overlaySpan = overlayBuffer.Memory.Span;
                Span<PointF> intersectionsSpan = intersectionsBuffer.Memory.Span;
                Span<PointOrientation> orientationsSpan = orientationsBuffer.Memory.Span;
                float blendPercentage = this.Options.BlendPercentage;

                // TODO: Remove bounds checks.
                if (blendPercentage < 1)
                {
                    for (int i = 0; i < scanline.Length; i++)
                    {
                        amountSpan[i] = scanline[i] * blendPercentage;
                        overlaySpan[i] = this[x + i, y, intersectionsSpan, orientationsSpan];
                    }
                }
                else
                {
                    for (int i = 0; i < scanline.Length; i++)
                    {
                        amountSpan[i] = scanline[i];
                        overlaySpan[i] = this[x + i, y, intersectionsSpan, orientationsSpan];
                    }
                }

                Span<TPixel> destinationRow = this.Target.GetPixelRowSpan(y).Slice(x, scanline.Length);
                this.Blender.Blend(this.Configuration, destinationRow, destinationRow, overlaySpan, amountSpan);
            }

            private (Edge edge, Intersection? info) FindIntersection(
                PointF start,
                PointF end,
                Span<PointF> intersections,
                Span<PointOrientation> orientations)
            {
                (Edge edge, Intersection? info) closest = default;

                foreach (Edge edge in this.edges)
                {
                    Intersection? intersection = edge.FindIntersection(start, end, intersections, orientations);

                    if (!intersection.HasValue)
                    {
                        continue;
                    }

                    if (closest.info == null || closest.info.Value.Distance > intersection.Value.Distance)
                    {
                        closest = (edge, intersection);
                    }
                }

                return closest;
            }

            private static bool FindPointOnTriangle(PointF v1, PointF v2, PointF v3, PointF point, out float u, out float v)
            {
                Vector2 e1 = v2 - v1;
                Vector2 e2 = v3 - v2;
                Vector2 e3 = v1 - v3;

                Vector2 pv1 = point - v1;
                Vector2 pv2 = point - v2;
                Vector2 pv3 = point - v3;

                var d1 = Vector3.Cross(new Vector3(e1.X, e1.Y, 0), new Vector3(pv1.X, pv1.Y, 0));
                var d2 = Vector3.Cross(new Vector3(e2.X, e2.Y, 0), new Vector3(pv2.X, pv2.Y, 0));
                var d3 = Vector3.Cross(new Vector3(e3.X, e3.Y, 0), new Vector3(pv3.X, pv3.Y, 0));

                if (Math.Sign(Vector3.Dot(d1, d2)) * Math.Sign(Vector3.Dot(d1, d3)) == -1 || Math.Sign(Vector3.Dot(d1, d2)) * Math.Sign(Vector3.Dot(d2, d3)) == -1)
                {
                    u = 0;
                    v = 0;
                    return false;
                }

                // From Real-Time Collision Detection
                // https://gamedev.stackexchange.com/questions/23743/whats-the-most-efficient-way-to-find-barycentric-coordinates
                float d00 = Vector2.Dot(e1, e1);
                float d01 = -Vector2.Dot(e1, e3);
                float d11 = Vector2.Dot(e3, e3);
                float d20 = Vector2.Dot(pv1, e1);
                float d21 = -Vector2.Dot(pv1, e3);
                float denominator = (d00 * d11) - (d01 * d01);
                u = ((d11 * d20) - (d01 * d21)) / denominator;
                v = ((d00 * d21) - (d01 * d20)) / denominator;
                return true;
            }
        }
    }
}
