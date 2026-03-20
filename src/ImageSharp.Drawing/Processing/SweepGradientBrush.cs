// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Processing;

using SixLabors.ImageSharp.Memory;

/// <summary>
/// Provides an implementation of a brush for painting sweep (conic) gradients within areas.
/// Angles increase counter-clockwise from +X on the design grid.
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
    /// <param name="startAngleDegrees">
    /// The starting angle, in degrees, measured counter-clockwise from +X on the design grid.
    /// This value is stored as provided so the sign and magnitude of the sweep remain intact.
    /// </param>
    /// <param name="endAngleDegrees">
    /// The ending angle, in degrees, measured counter-clockwise from +X on the design grid.
    /// If equal to <paramref name="startAngleDegrees"/>, the gradient is treated as a full 360 degree sweep.
    /// Otherwise, the signed difference between start and end determines the sweep direction.
    /// </param>
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

    /// <summary>
    /// Gets the center point of the sweep gradient.
    /// </summary>
    public PointF Center => this.center;

    /// <summary>
    /// Gets the starting angle in degrees.
    /// </summary>
    public float StartAngleDegrees => this.startAngleDegrees;

    /// <summary>
    /// Gets the ending angle in degrees.
    /// </summary>
    public float EndAngleDegrees => this.endAngleDegrees;

    /// <inheritdoc/>
    public override Brush Transform(Matrix4x4 matrix)
    {
        PointF tc = PointF.Transform(this.center, matrix);

        // Treat the brush as two rays starting at the center:
        // one ray for the start angle and one ray for the end angle.
        // The important value is the signed angular distance between those rays.
        // We keep that sign so a reflected transform can turn a counter-clockwise
        // sweep into a clockwise sweep instead of silently "fixing" it.
        float sweepDegrees = GetEffectiveSweepDegrees(this.startAngleDegrees, this.endAngleDegrees);
        float startRad = GeometryUtilities.DegreeToRadian(this.startAngleDegrees);
        float endRad = GeometryUtilities.DegreeToRadian(this.startAngleDegrees + sweepDegrees);

        // The public API uses the design-grid convention, which is y-up.
        // Screen pixels are y-down, so a positive mathematical rotation uses
        // `center.Y - sin(theta)` rather than `center.Y + sin(theta)`.
        PointF startDir = PointF.Transform(new PointF(this.center.X + MathF.Cos(startRad), this.center.Y - MathF.Sin(startRad)), matrix);
        PointF endDir = PointF.Transform(new PointF(this.center.X + MathF.Cos(endRad), this.center.Y - MathF.Sin(endRad)), matrix);

        // Convert the transformed rays back into brush angles in the same public convention:
        // counter-clockwise from +X on the design grid.
        float newStart = NormalizeDirectionDegrees(MathF.Atan2(-(startDir.Y - tc.Y), startDir.X - tc.X) * (180f / MathF.PI));
        float newEnd = NormalizeDirectionDegrees(MathF.Atan2(-(endDir.Y - tc.Y), endDir.X - tc.X) * (180f / MathF.PI));

        // A negative determinant means the transform flips orientation.
        // That flips the direction of the sweep, so we use it to decide whether
        // the end angle should unwrap forwards or backwards from the new start.
        float determinant = (matrix.M11 * matrix.M22) - (matrix.M12 * matrix.M21);
        float directionHint = MathF.Sign(sweepDegrees);
        if (directionHint == 0F)
        {
            directionHint = 1F;
        }

        if (determinant < 0F)
        {
            directionHint = -directionHint;
        }

        return new SweepGradientBrush(
            tc,
            newStart,
            UnwrapSweepEndDegrees(newStart, newEnd, directionHint, MathF.Abs(sweepDegrees)),
            this.RepetitionMode,
            this.ColorStopsArray);
    }

    /// <inheritdoc/>
    public override bool Equals(Brush? other)
    {
        // Sweep brushes are equal only when they describe the same center,
        // the same signed angular interval, and the same inherited stop data.
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

    /// <summary>
    /// Converts the stored start/end angles into the signed sweep interval that the brush should render.
    /// </summary>
    /// <param name="startAngleDegrees">The starting angle in degrees.</param>
    /// <param name="endAngleDegrees">The ending angle in degrees.</param>
    /// <returns>
    /// The signed angular interval in degrees. Equal endpoints are treated as a full turn.
    /// </returns>
    // Sweep gradients interpret equal endpoints as "full turn".
    // All other cases keep the caller-provided signed angular span.
    private static float GetEffectiveSweepDegrees(float startAngleDegrees, float endAngleDegrees)
    {
        float sweepDegrees = endAngleDegrees - startAngleDegrees;
        if (MathF.Abs(sweepDegrees) < 1e-6F)
        {
            // Equal endpoints mean "full circle", not an empty span.
            return 360F;
        }

        return sweepDegrees;
    }

    /// <summary>
    /// Normalizes an angle to the canonical <c>[0, 360)</c> direction range.
    /// </summary>
    /// <param name="degrees">The angle to normalize.</param>
    /// <returns>The equivalent direction in the canonical degree range.</returns>
    // Convert any equivalent direction into the canonical [0, 360) representation
    // so transformed brushes remain stable when compared or reused.
    private static float NormalizeDirectionDegrees(float degrees)
    {
        float normalized = degrees % 360F;
        if (normalized < 0F)
        {
            normalized += 360F;
        }

        return normalized;
    }

    /// <summary>
    /// Reconstructs the signed end angle after independently transforming the start and end rays.
    /// </summary>
    /// <param name="startDegrees">The transformed starting angle in normalized degrees.</param>
    /// <param name="endDegrees">The transformed ending angle in normalized degrees.</param>
    /// <param name="directionHint">
    /// The expected sweep direction. Positive means unwrap forwards, negative means unwrap backwards.
    /// </param>
    /// <param name="minimumMagnitude">The minimum magnitude the restored interval must preserve.</param>
    /// <returns>The unwrapped ending angle measured relative to <paramref name="startDegrees"/>.</returns>
    // After transforming the start and end rays separately, both directions land in [0, 360).
    // This method restores the intended signed sweep by unwrapping the end angle relative to
    // the start angle, using the desired direction as the constraint.
    private static float UnwrapSweepEndDegrees(float startDegrees, float endDegrees, float directionHint, float minimumMagnitude)
    {
        float delta = endDegrees - startDegrees;
        if (directionHint >= 0F)
        {
            // Keep the end angle ahead of the start angle for a positive sweep.
            while (delta < 0F)
            {
                delta += 360F;
            }

            if (MathF.Abs(delta) < 1e-6F && minimumMagnitude >= 360F - 1e-6F)
            {
                delta = 360F;
            }
        }
        else
        {
            // Keep the end angle behind the start angle for a negative sweep.
            while (delta > 0F)
            {
                delta -= 360F;
            }

            if (MathF.Abs(delta) < 1e-6F && minimumMagnitude >= 360F - 1e-6F)
            {
                delta = -360F;
            }
        }

        return startDegrees + delta;
    }

    /// <inheritdoc />
    public override BrushRenderer<TPixel> CreateRenderer<TPixel>(
        Configuration configuration,
        GraphicsOptions options,
        int canvasWidth,
        RectangleF region) =>

        // The renderer precomputes the angular interval once and then samples it per pixel.
        new SweepGradientBrushRenderer<TPixel>(
            configuration,
            options,
            canvasWidth,
            this,
            this.ColorStopsArray,
            this.RepetitionMode);

    /// <summary>
    /// The sweep (conic) gradient brush applicator.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    private sealed class SweepGradientBrushRenderer<TPixel> : GradientBrushRenderer<TPixel>
        where TPixel : unmanaged, IPixel<TPixel>
    {
        private const float Tau = MathF.Tau;

        private readonly float cx;

        private readonly float cy;

        private readonly float startRad;

        private readonly float endRad;

        /// <summary>
        /// Initializes a new instance of the <see cref="SweepGradientBrushRenderer{TPixel}"/> class.
        /// </summary>
        /// <param name="configuration">The configuration instance to use when performing operations.</param>
        /// <param name="options">The graphics options.</param>
        /// <param name="canvasWidth">The canvas width for the current render pass.</param>
        /// <param name="brush">The sweep gradient brush.</param>
        /// <param name="colorStops">The gradient color stops (ratios in [0..1]).</param>
        /// <param name="repetitionMode">Defines how gradient colors are repeated outside [0..1].</param>
        public SweepGradientBrushRenderer(
            Configuration configuration,
            GraphicsOptions options,
            int canvasWidth,
            SweepGradientBrush brush,
            ColorStop[] colorStops,
            GradientRepetitionMode repetitionMode)
            : base(configuration, options, canvasWidth, colorStops, repetitionMode)
        {
            this.cx = brush.Center.X;
            this.cy = brush.Center.Y;

            // Store the interval as radians once so sampling only needs one subtraction and one divide.
            float sweepDegrees = GetEffectiveSweepDegrees(brush.StartAngleDegrees, brush.EndAngleDegrees);
            this.startRad = GeometryUtilities.DegreeToRadian(brush.StartAngleDegrees);
            this.endRad = GeometryUtilities.DegreeToRadian(brush.StartAngleDegrees + sweepDegrees);
        }

        /// <inheritdoc />
        protected override float PositionOnGradient(float x, float y)
        {
            // Move the sample into center-relative coordinates.
            float dx = x - this.cx;
            float dy = y - this.cy;

            if (dx == 0f && dy == 0f)
            {
                // The center has no unique angle, so pick a stable value on the gradient.
                return 0f;
            }

            // Convert from y-down image space back into the brush's y-up angle convention,
            // then normalize to [0, 2π) so subtraction against the stored start angle is stable.
            float angle = MathF.Atan2(-dy, dx);
            if (angle < 0f)
            {
                angle += Tau;
            }

            // Divide by the signed angular span.
            // A positive denominator produces a counter-clockwise sweep and a negative
            // denominator produces a clockwise sweep. The base gradient code then applies
            // the repetition mode to this unbounded parameter.
            return (angle - this.startRad) / (this.endRad - this.startRad);
        }
    }
}
