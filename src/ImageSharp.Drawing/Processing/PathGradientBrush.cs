// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using SixLabors.ImageSharp.Drawing.Utilities;

namespace SixLabors.ImageSharp.Drawing.Processing;

/// <summary>
/// Provides an implementation of a brush for painting gradients between multiple color positions in 2D coordinates.
/// </summary>
public sealed class PathGradientBrush : Brush
{
    private readonly Edge[] edges;
    private readonly Color centerColor;
    private readonly bool hasSpecialCenterColor;

    /// <summary>
    /// Initializes a new instance of the <see cref="PathGradientBrush"/> class.
    /// </summary>
    /// <param name="points">Points that constitute a polygon that represents the gradient area.</param>
    /// <param name="colors">Array of colors that correspond to each point in the polygon.</param>
    public PathGradientBrush(PointF[] points, Color[] colors)
    {
        Guard.NotNull(points, nameof(points));
        Guard.MustBeGreaterThanOrEqualTo(points.Length, 3, nameof(points));

        Guard.NotNull(colors, nameof(colors));
        Guard.MustBeGreaterThan(colors.Length, 0, nameof(colors));

        int size = points.Length;

        this.edges = new Edge[points.Length];

        for (int i = 0; i < points.Length; i++)
        {
            this.edges[i] = new Edge(points[i % size], points[(i + 1) % size], ColorAt(i), ColorAt(i + 1));
        }

        this.centerColor = CalculateCenterColor(colors);

        Color ColorAt(int index) => colors[index % colors.Length];
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
    public override bool Equals(Brush? other)
    {
        if (other is PathGradientBrush brush)
        {
            return this.centerColor.Equals(brush.centerColor)
                && this.hasSpecialCenterColor.Equals(brush.hasSpecialCenterColor)
                && this.edges?.SequenceEqual(brush.edges) == true;
        }

        return false;
    }

    /// <inheritdoc/>
    public override int GetHashCode()
        => HashCode.Combine(this.edges, this.centerColor, this.hasSpecialCenterColor);

    /// <inheritdoc />
    public override BrushApplicator<TPixel> CreateApplicator<TPixel>(
        Configuration configuration,
        GraphicsOptions options,
        ImageFrame<TPixel> source,
        RectangleF region)
        => new PathGradientBrushApplicator<TPixel>(
            configuration,
            options,
            source,
            this.edges,
            this.centerColor,
            this.hasSpecialCenterColor);

    private static Color CalculateCenterColor(Color[] colors)
        => Color.FromScaledVector(colors.Select(c => c.ToScaledVector4()).Aggregate((p1, p2) => p1 + p2) / colors.Length);

    private static float DistanceBetween(Vector2 p1, Vector2 p2) => (p2 - p1).Length();

    private readonly struct Intersection
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
    private class Edge : IEquatable<Edge>
    {
        private readonly float length;

        public Edge(Vector2 start, Vector2 end, Color startColor, Color endColor)
        {
            this.Start = start;
            this.End = end;
            this.StartColor = startColor.ToScaledVector4();
            this.EndColor = endColor.ToScaledVector4();

            this.length = DistanceBetween(this.End, this.Start);
        }

        public Vector2 Start { get; }

        public Vector2 End { get; }

        public Vector4 StartColor { get; }

        public Vector4 EndColor { get; }

        public bool Intersect(
            Vector2 start,
            Vector2 end,
            ref Vector2 ip) =>
            Utilities.Intersect.LineSegmentToLineSegmentIgnoreCollinear(start, end, this.Start, this.End, ref ip);

        public Vector4 ColorAt(float distance)
        {
            float ratio = this.length > 0 ? distance / this.length : 0;

            return Vector4.Lerp(this.StartColor, this.EndColor, ratio);
        }

        public Vector4 ColorAt(PointF point) => this.ColorAt(DistanceBetween(point, this.Start));

        public bool Equals(Edge? other)
            => other != null &&
               other.Start == this.Start &&
               other.End == this.End &&
               other.StartColor.Equals(this.StartColor) &&
               other.EndColor.Equals(this.EndColor);

        public override bool Equals(object? obj) => this.Equals(obj as Edge);

        public override int GetHashCode()
            => HashCode.Combine(this.Start, this.End, this.StartColor, this.EndColor);
    }

    /// <summary>
    /// The path gradient brush applicator.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    private sealed class PathGradientBrushApplicator<TPixel> : BrushApplicator<TPixel>
        where TPixel : unmanaged, IPixel<TPixel>
    {
        private readonly Vector2 center;

        private readonly Vector4 centerColor;

        private readonly bool hasSpecialCenterColor;

        private readonly float maxDistance;

        private readonly IList<Edge> edges;

        private readonly TPixel centerPixel;

        private readonly TPixel transparentPixel;

        private readonly ThreadLocalBlenderBuffers<TPixel> blenderBuffers;

        private bool isDisposed;

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
            Vector2[] points = edges.Select(s => s.Start).ToArray();

            this.center = points.Aggregate((p1, p2) => p1 + p2) / edges.Count;
            this.centerColor = centerColor.ToScaledVector4();
            this.hasSpecialCenterColor = hasSpecialCenterColor;
            this.centerPixel = centerColor.ToPixel<TPixel>();
            this.maxDistance = points.Select(p => p - this.center).Max(d => d.Length());
            this.transparentPixel = Color.Transparent.ToPixel<TPixel>();
            this.blenderBuffers = new ThreadLocalBlenderBuffers<TPixel>(configuration.MemoryAllocator, source.Width);
        }

        internal TPixel this[int x, int y]
        {
            get
            {
                Vector2 point = new(x, y);

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

                    return TPixel.FromScaledVector4(pointColor);
                }

                Vector2 direction = Vector2.Normalize(point - this.center);
                Vector2 end = point + (direction * this.maxDistance);

                (Edge Edge, Vector2 Point)? isc = this.FindIntersection(point, end);

                if (!isc.HasValue)
                {
                    return this.transparentPixel;
                }

                Vector2 intersection = isc.Value.Point;
                Vector4 edgeColor = isc.Value.Edge.ColorAt(intersection);

                float length = DistanceBetween(intersection, this.center);
                float ratio = length > 0 ? DistanceBetween(intersection, point) / length : 0;

                Vector4 color = Vector4.Lerp(edgeColor, this.centerColor, ratio);

                return TPixel.FromScaledVector4(color);
            }
        }

        /// <inheritdoc />
        public override void Apply(Span<float> scanline, int x, int y)
        {
            Span<float> amounts = this.blenderBuffers.AmountSpan[..scanline.Length];
            Span<TPixel> overlays = this.blenderBuffers.OverlaySpan[..scanline.Length];
            float blendPercentage = this.Options.BlendPercentage;

            // TODO: Remove bounds checks.
            if (blendPercentage < 1)
            {
                for (int i = 0; i < scanline.Length; i++)
                {
                    amounts[i] = scanline[i] * blendPercentage;
                    overlays[i] = this[x + i, y];
                }
            }
            else
            {
                for (int i = 0; i < scanline.Length; i++)
                {
                    amounts[i] = scanline[i];
                    overlays[i] = this[x + i, y];
                }
            }

            Span<TPixel> destinationRow = this.Target.PixelBuffer.DangerousGetRowSpan(y).Slice(x, scanline.Length);
            this.Blender.Blend(this.Configuration, destinationRow, destinationRow, overlays, amounts);
        }

        /// <inheritdoc />
        protected override void Dispose(bool disposing)
        {
            if (this.isDisposed)
            {
                return;
            }

            base.Dispose(disposing);

            if (disposing)
            {
                this.blenderBuffers.Dispose();
            }

            this.isDisposed = true;
        }

        private (Edge Edge, Vector2 Point)? FindIntersection(
            PointF start,
            PointF end)
        {
            Vector2 ip = default;
            Vector2 closestIntersection = default;
            Edge? closestEdge = null;
            const float minDistance = float.MaxValue;
            foreach (Edge edge in this.edges)
            {
                if (!edge.Intersect(start, end, ref ip))
                {
                    continue;
                }

                float d = Vector2.DistanceSquared(start, end);
                if (d < minDistance)
                {
                    closestEdge = edge;
                    closestIntersection = ip;
                }
            }

            return closestEdge != null ? (closestEdge, closestIntersection) : null;
        }

        private static bool FindPointOnTriangle(Vector2 v1, Vector2 v2, Vector2 v3, Vector2 point, out float u, out float v)
        {
            Vector2 e1 = v2 - v1;
            Vector2 e2 = v3 - v2;
            Vector2 e3 = v1 - v3;

            Vector2 pv1 = point - v1;
            Vector2 pv2 = point - v2;
            Vector2 pv3 = point - v3;

            Vector3 d1 = Vector3.Cross(new Vector3(e1.X, e1.Y, 0), new Vector3(pv1.X, pv1.Y, 0));
            Vector3 d2 = Vector3.Cross(new Vector3(e2.X, e2.Y, 0), new Vector3(pv2.X, pv2.Y, 0));
            Vector3 d3 = Vector3.Cross(new Vector3(e3.X, e3.Y, 0), new Vector3(pv3.X, pv3.Y, 0));

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
