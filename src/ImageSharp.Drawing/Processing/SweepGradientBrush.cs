// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Processing;

/// <summary>
/// Provides an implementation of a brush for painting sweep (conic) gradients within areas.
/// Angles increase clockwise (y-down coordinate system) with 0° pointing to the +X direction.
/// </summary>
public sealed class SweepGradientBrush : GradientBrush
{
    private readonly PointF center;

    private readonly float startAngleDegrees;

    private readonly float endAngleDegrees;

    /// <summary>
    /// Initializes a new instance of the <see cref="SweepGradientBrush"/> class.
    /// </summary>
    /// <param name="center">The center point of the sweep gradient in device space.</param>
    /// <param name="startAngleDegrees">The starting angle, in degrees (clockwise, 0° is +X).</param>
    /// <param name="endAngleDegrees">The ending angle, in degrees (clockwise, 0° is +X). If equal to <paramref name="startAngleDegrees"/>, the gradient is treated as a full 360° sweep.</param>
    /// <param name="repetitionMode">Defines how the gradient colors are repeated beyond the interval [0..1].</param>
    /// <param name="colorStops">The gradient color stops. Ratios must be in [0..1] and are interpreted along the angular sweep.</param>
    public SweepGradientBrush(
        PointF center,
        float startAngleDegrees,
        float endAngleDegrees,
        GradientRepetitionMode repetitionMode,
        params ColorStop[] colorStops)
        : base(repetitionMode, colorStops)
    {
        this.center = center;
        this.startAngleDegrees = startAngleDegrees;
        this.endAngleDegrees = endAngleDegrees;
    }

    /// <inheritdoc/>
    public override bool Equals(Brush? other)
    {
        if (other is SweepGradientBrush brush)
        {
            return base.Equals(other)
                && this.center.Equals(brush.center)
                && this.startAngleDegrees.Equals(brush.startAngleDegrees)
                && this.endAngleDegrees.Equals(brush.endAngleDegrees);
        }

        return false;
    }

    /// <inheritdoc/>
    public override int GetHashCode()
        => HashCode.Combine(
            base.GetHashCode(),
            this.center,
            this.startAngleDegrees,
            this.endAngleDegrees);

    /// <inheritdoc />
    public override BrushApplicator<TPixel> CreateApplicator<TPixel>(
        Configuration configuration,
        GraphicsOptions options,
        ImageFrame<TPixel> source,
        RectangleF region)
        => new SweepGradientBrushApplicator<TPixel>(
            configuration,
            options,
            source,
            this.center,
            this.startAngleDegrees,
            this.endAngleDegrees,
            this.ColorStops,
            this.RepetitionMode);

    /// <summary>
    /// The sweep (conic) gradient brush applicator.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    private sealed class SweepGradientBrushApplicator<TPixel> : GradientBrushApplicator<TPixel>
        where TPixel : unmanaged, IPixel<TPixel>
    {
        private const float Tau = MathF.Tau;

        private readonly float cx;

        private readonly float cy;

        private readonly float startRad;

        private readonly float invSweep;

        private readonly bool isFullCircle;

        /// <summary>
        /// Initializes a new instance of the <see cref="SweepGradientBrushApplicator{TPixel}"/> class.
        /// </summary>
        /// <param name="configuration">The configuration instance to use when performing operations.</param>
        /// <param name="options">The graphics options.</param>
        /// <param name="source">The source image.</param>
        /// <param name="center">The center of the sweep gradient.</param>
        /// <param name="startAngleDegrees">The start angle in degrees (clockwise).</param>
        /// <param name="endAngleDegrees">The end angle in degrees (clockwise).</param>
        /// <param name="colorStops">The gradient color stops (ratios in [0..1]).</param>
        /// <param name="repetitionMode">Defines how gradient colors are repeated outside [0..1].</param>
        public SweepGradientBrushApplicator(
            Configuration configuration,
            GraphicsOptions options,
            ImageFrame<TPixel> source,
            PointF center,
            float startAngleDegrees,
            float endAngleDegrees,
            ColorStop[] colorStops,
            GradientRepetitionMode repetitionMode)
            : base(configuration, options, source, colorStops, repetitionMode)
        {
            this.cx = center.X;
            this.cy = center.Y;

            float start = GeometryUtilities.DegreeToRadian(NormalizeDegrees(startAngleDegrees));
            float end = GeometryUtilities.DegreeToRadian(NormalizeDegrees(endAngleDegrees));

            float sweep = NormalizeDeltaRadians(end - start);

            // If sweep collapses numerically to ~0, treat as full circle.
            if (MathF.Abs(sweep) < 1e-6f)
            {
                sweep = Tau;
            }

            this.startRad = start;
            this.invSweep = 1f / sweep;
            this.isFullCircle = MathF.Abs(sweep - Tau) < 1e-6f;
        }

        /// <summary>
        /// Calculates the position parameter along the sweep gradient for the given device-space point.
        /// The returned value is not clamped to [0..1]; repetition semantics are applied by the base class.
        /// </summary>
        /// <param name="x">The x-coordinate of the point (device space).</param>
        /// <param name="y">The y-coordinate of the point (device space).</param>
        /// <returns>The unbounded position on the gradient.</returns>
        protected override float PositionOnGradient(float x, float y)
        {
            // Vector from center to sample point. Y is inverted to maintain clockwise angles in y-down space.
            float dx = x - this.cx;
            float dy = y - this.cy;

            if (dx == 0f && dy == 0f)
            {
                // Arbitrary but stable choice for the center.
                return 0f;
            }

            float angle = MathF.Atan2(-dy, dx); // (-π, π]
            if (angle < 0f)
            {
                angle += Tau; // [0, 2π)
            }

            // Rotate basis by 180° so that 0.75 (270°) maps to "up"/top.
            // This shifts the canonical directions: right->left, up->down, etc.
            angle += MathF.PI;
            if (angle >= Tau)
            {
                angle -= Tau;
            }

            // Phase measured clockwise from start.
            float phase = angle - this.startRad;
            if (phase < 0f)
            {
                phase += Tau;
            }

            if (this.isFullCircle)
            {
                // Map full circle to [0..1).
                return phase / Tau;
            }

            // Partial sweep: phase beyond sweep -> t > 1 (lets repetition mode handle clipping).
            return phase * this.invSweep;
        }

        private static float NormalizeDegrees(float deg)
        {
            float d = deg % 360f;
            if (d < 0f)
            {
                d += 360f;
            }

            return d;
        }

        private static float NormalizeDeltaRadians(float delta)
        {
            float d = delta % Tau;
            if (d <= 0f)
            {
                d += Tau;
            }

            return d;
        }
    }
}
