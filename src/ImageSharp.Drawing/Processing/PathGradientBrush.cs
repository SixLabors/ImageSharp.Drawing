// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Processing;

/// <summary>
/// Provides an implementation of a brush for painting gradients between multiple color positions in 2D coordinates.
/// </summary>
public sealed class PathGradientBrush : Brush
{
    private readonly PointF[] points;
    private readonly Color[] colors;
    private readonly Edge[] edges;

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

        this.points = [.. points];
        this.colors = [.. colors];
        this.edges = new Edge[this.points.Length];

        for (int i = 0; i < this.points.Length; i++)
        {
            this.edges[i] = new Edge(this.points[i % size], this.points[(i + 1) % size], ColorAt(i), ColorAt(i + 1));
        }

        this.CenterColor = CalculateCenterColor(this.colors);

        Color ColorAt(int index) => this.colors[index % this.colors.Length];
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
        this.CenterColor = centerColor;
        this.HasExplicitCenterColor = true;
    }

    /// <summary>
    /// Gets the polygon points that define the gradient area.
    /// </summary>
    public ReadOnlySpan<PointF> Points => this.points;

    /// <summary>
    /// Gets the colors that are mapped to the polygon points.
    /// </summary>
    public ReadOnlySpan<Color> Colors => this.colors;

    /// <summary>
    /// Gets the color at the center of the gradient area.
    /// </summary>
    public Color CenterColor { get; }

    /// <summary>
    /// Gets a value indicating whether the center color was explicitly supplied.
    /// </summary>
    public bool HasExplicitCenterColor { get; }

    /// <inheritdoc/>
    public override Brush Transform(Matrix4x4 matrix)
    {
        if (matrix.IsIdentity)
        {
            return this;
        }

        PointF[] transformedPoints = new PointF[this.points.Length];
        for (int i = 0; i < transformedPoints.Length; i++)
        {
            transformedPoints[i] = PointF.Transform(this.points[i], matrix);
        }

        return this.HasExplicitCenterColor
            ? new PathGradientBrush(transformedPoints, this.colors, this.CenterColor)
            : new PathGradientBrush(transformedPoints, this.colors);
    }

    /// <inheritdoc />
    public override bool Equals(Brush? other)
    {
        if (other is PathGradientBrush brush)
        {
            return this.CenterColor.Equals(brush.CenterColor)
                && this.HasExplicitCenterColor.Equals(brush.HasExplicitCenterColor)
                && this.edges?.SequenceEqual(brush.edges) == true;
        }

        return false;
    }

    /// <inheritdoc/>
    public override int GetHashCode()
        => HashCode.Combine(this.edges, this.CenterColor, this.HasExplicitCenterColor);

    /// <inheritdoc />
    public override BrushRenderer<TPixel> CreateRenderer<TPixel>(
        Configuration configuration,
        GraphicsOptions options,
        int canvasWidth,
        RectangleF region)
        => new PathGradientBrushRenderer<TPixel>(
            configuration,
            options,
            canvasWidth,
            this.edges,
            this.CenterColor,
            this.HasExplicitCenterColor);

    private static Color CalculateCenterColor(Color[] colors)
    {
        Guard.NotNull(colors, nameof(colors));
        Guard.MustBeGreaterThan(colors.Length, 0, nameof(colors));

        return Color.FromScaledVector(colors.Select(c => c.ToScaledVector4()).Aggregate((p1, p2) => p1 + p2) / colors.Length);
    }

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
            PolygonUtilities.LineSegmentToLineSegmentIgnoreCollinear(start, end, this.Start, this.End, ref ip);

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
    private sealed class PathGradientBrushRenderer<TPixel> : BrushRenderer<TPixel>
        where TPixel : unmanaged, IPixel<TPixel>
    {
        private readonly Vector2 center;

        private readonly Vector4 centerColor;

        private readonly bool hasSpecialCenterColor;

        private readonly float maxDistance;

        private readonly IList<Edge> edges;

        private readonly TPixel centerPixel;

        private readonly TPixel transparentPixel;

        /// <summary>
        /// Initializes a new instance of the <see cref="PathGradientBrushRenderer{TPixel}"/> class.
        /// </summary>
        /// <param name="configuration">The configuration instance to use when performing operations.</param>
        /// <param name="options">The graphics options.</param>
        /// <param name="canvasWidth">The canvas width for the current render pass.</param>
        /// <param name="edges">Edges of the polygon.</param>
        /// <param name="centerColor">Color at the center of the gradient area to which the other colors converge.</param>
        /// <param name="hasSpecialCenterColor">Whether the center color is different from a smooth gradient between the edges.</param>
        public PathGradientBrushRenderer(
            Configuration configuration,
            GraphicsOptions options,
            int canvasWidth,
            IList<Edge> edges,
            Color centerColor,
            bool hasSpecialCenterColor)
            : base(configuration, options, canvasWidth)
        {
            this.edges = edges;
            Vector2[] points = [.. edges.Select(s => s.Start)];

            this.center = points.Aggregate((p1, p2) => p1 + p2) / edges.Count;
            this.centerColor = centerColor.ToScaledVector4();
            this.hasSpecialCenterColor = hasSpecialCenterColor;
            this.centerPixel = centerColor.ToPixel<TPixel>();
            this.maxDistance = points.Select(p => p - this.center).Max(d => d.Length());
            this.transparentPixel = Color.Transparent.ToPixel<TPixel>();
        }

        internal TPixel this[int x, int y]
        {
            get
            {
                // Match other gradient brushes by evaluating at pixel centers.
                Vector2 point = new(x + 0.5F, y + 0.5F);

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
        public override void Apply(
            Span<TPixel> destinationRow,
            ReadOnlySpan<float> scanline,
            int x,
            int y,
            BrushWorkspace<TPixel> workspace)
        {
            Span<float> amounts = workspace.GetAmounts(scanline.Length);
            Span<TPixel> overlays = workspace.GetOverlays(scanline.Length);
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

            this.Blender.Blend(
                this.Configuration,
                destinationRow,
                destinationRow,
                overlays,
                amounts,
                workspace.GetBlendScratch(scanline.Length, 3));
        }

        private (Edge Edge, Vector2 Point)? FindIntersection(
            PointF start,
            PointF end)
        {
            Vector2 ip = default;
            Vector2 closestIntersection = default;
            Edge? closestEdge = null;
            float minDistance = float.MaxValue;
            foreach (Edge edge in this.edges)
            {
                if (!edge.Intersect(start, end, ref ip))
                {
                    continue;
                }

                float d = Vector2.DistanceSquared(start, ip);
                if (d < minDistance)
                {
                    minDistance = d;
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
