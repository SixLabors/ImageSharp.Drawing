// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Processing;

/// <summary>
/// Provides a brush that paints linear gradients within an area.
/// Supports both classic two-point gradients and three-point (rotated) gradients.
/// </summary>
public sealed class LinearGradientBrush : GradientBrush
{
    private readonly PointF startPoint;
    private readonly PointF endPoint;
    private readonly PointF? rotationPoint;

    /// <summary>
    /// Initializes a new instance of the <see cref="LinearGradientBrush"/> class using
    /// a start and end point.
    /// </summary>
    /// <param name="p0">The start point of the gradient.</param>
    /// <param name="p1">The end point of the gradient.</param>
    /// <param name="repetitionMode">Defines how the colors are repeated.</param>
    /// <param name="colorStops">The ordered color stops of the gradient.</param>
    public LinearGradientBrush(
        PointF p0,
        PointF p1,
        GradientRepetitionMode repetitionMode,
        params ColorStop[] colorStops)
        : base(repetitionMode, colorStops)
    {
        this.startPoint = p0;
        this.endPoint = p1;
        this.rotationPoint = null;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="LinearGradientBrush"/> class using
    /// three points to define a rotated gradient axis.
    /// </summary>
    /// <param name="p0">The first point (start of the gradient).</param>
    /// <param name="p1">The second point (gradient vector endpoint).</param>
    /// <param name="rotationPoint">
    /// The rotation reference point. This defines the rotation of the gradient axis.
    /// </param>
    /// <param name="repetitionMode">Defines how the colors are repeated.</param>
    /// <param name="colorStops">The ordered color stops of the gradient.</param>
    public LinearGradientBrush(
        PointF p0,
        PointF p1,
        PointF rotationPoint,
        GradientRepetitionMode repetitionMode,
        params ColorStop[] colorStops)
        : base(repetitionMode, colorStops)
    {
        this.startPoint = p0;
        this.endPoint = p1;
        this.rotationPoint = rotationPoint;
    }

    /// <inheritdoc/>
    public override bool Equals(Brush? other)
    {
        if (other is LinearGradientBrush brush)
        {
            return base.Equals(other)
                && this.startPoint.Equals(brush.startPoint)
                && this.endPoint.Equals(brush.endPoint)
                && Nullable.Equals(this.rotationPoint, brush.rotationPoint);
        }

        return false;
    }

    /// <inheritdoc/>
    public override int GetHashCode()
        => HashCode.Combine(base.GetHashCode(), this.startPoint, this.endPoint, this.rotationPoint);

    /// <inheritdoc/>
    public override BrushApplicator<TPixel> CreateApplicator<TPixel>(
        Configuration configuration,
        GraphicsOptions options,
        ImageFrame<TPixel> source,
        RectangleF region)
        => new LinearGradientBrushApplicator<TPixel>(
            configuration,
            options,
            source,
            this.startPoint,
            this.endPoint,
            this.rotationPoint,
            this.ColorStops,
            this.RepetitionMode);

    /// <summary>
    /// Implements the gradient application logic for <see cref="LinearGradientBrush"/>.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    private sealed class LinearGradientBrushApplicator<TPixel> : GradientBrushApplicator<TPixel>
        where TPixel : unmanaged, IPixel<TPixel>
    {
        private readonly PointF start;
        private readonly PointF end;
        private readonly float alongX;
        private readonly float alongY;
        private readonly float acrossX;
        private readonly float acrossY;
        private readonly float alongsSquared;
        private readonly float length;

        /// <summary>
        /// Initializes a new instance of the <see cref="LinearGradientBrushApplicator{TPixel}"/> class.
        /// </summary>
        /// <param name="configuration">The ImageSharp configuration.</param>
        /// <param name="options">The graphics options.</param>
        /// <param name="source">The target image frame.</param>
        /// <param name="p0">The gradient start point.</param>
        /// <param name="p1">The gradient end point.</param>
        /// <param name="p2">The optional rotation point.</param>
        /// <param name="colorStops">The gradient color stops.</param>
        /// <param name="repetitionMode">Defines how the gradient repeats.</param>
        public LinearGradientBrushApplicator(
            Configuration configuration,
            GraphicsOptions options,
            ImageFrame<TPixel> source,
            PointF p0,
            PointF p1,
            PointF? p2,
            ColorStop[] colorStops,
            GradientRepetitionMode repetitionMode)
            : base(configuration, options, source, colorStops, repetitionMode)
        {
            // Determine whether this is a simple linear gradient (2 points)
            // or a rotated one (3 points).
            if (p2 is null)
            {
                // Classic SVG-style gradient from start -> end.
                this.start = p0;
                this.end = p1;
            }
            else
            {
                // Compute the rotated gradient axis per COLRv1 rules.
                // p0 = start, p1 = gradient vector, p2 = rotation reference.
                float vx = p1.X - p0.X;
                float vy = p1.Y - p0.Y;
                float rx = p2.Value.X - p0.X;
                float ry = p2.Value.Y - p0.Y;

                // n = perpendicular to rotation vector
                float nx = ry;
                float ny = -rx;

                // Avoid divide-by-zero if p0 == p2.
                float ndotn = (nx * nx) + (ny * ny);
                if (ndotn == 0f)
                {
                    this.start = p0;
                    this.end = p1;
                }
                else
                {
                    // Project p1 - p0 onto perpendicular direction.
                    float vdotn = (vx * nx) + (vy * ny);
                    float scale = vdotn / ndotn;

                    // The derived endpoint after rotation.
                    this.start = p0;
                    this.end = new PointF(p0.X + (scale * nx), p0.Y + (scale * ny));
                }
            }

            // Calculate axis vectors.
            this.alongX = this.end.X - this.start.X;
            this.alongY = this.end.Y - this.start.Y;

            // Perpendicular axis vector.
            this.acrossX = this.alongY;
            this.acrossY = -this.alongX;

            // Precompute squared length and actual length for later use.
            this.alongsSquared = (this.alongX * this.alongX) + (this.alongY * this.alongY);
            this.length = MathF.Sqrt(this.alongsSquared);
        }

        /// <inheritdoc/>
        protected override float PositionOnGradient(float x, float y)
        {
            // Degenerate case: gradient length == 0, use final stop color.
            if (this.alongsSquared == 0f)
            {
                return 1f;
            }

            // Fast path for horizontal gradients.
            if (this.acrossX == 0f)
            {
                float denom = this.end.X - this.start.X;
                return denom != 0f ? (x - this.start.X) / denom : 1f;
            }

            // Fast path for vertical gradients.
            if (this.acrossY == 0f)
            {
                float denom = this.end.Y - this.start.Y;
                return denom != 0f ? (y - this.start.Y) / denom : 1f;
            }

            // General case: project sample point onto the gradient axis.
            float deltaX = x - this.start.X;
            float deltaY = y - this.start.Y;

            // Compute perpendicular projection scalar.
            float k = ((this.alongY * deltaX) - (this.alongX * deltaY)) / this.alongsSquared;

            // Determine projected point on the gradient line.
            float projX = x - (k * this.alongY);
            float projY = y + (k * this.alongX);

            // Compute distance from gradient start to projected point.
            float dx = projX - this.start.X;
            float dy = projY - this.start.Y;

            // Normalize to [0,1] range along the gradient length.
            return this.length > 0f ? MathF.Sqrt((dx * dx) + (dy * dy)) / this.length : 1f;
        }
    }
}
