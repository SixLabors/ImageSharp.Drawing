// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Processing;

/// <summary>
/// A radial gradient brush defined by either one circle or two circles.
/// When one circle is provided, the gradient parameter is the distance from the center divided by the radius.
/// When two circles are provided, the gradient parameter is computed along the family of circles interpolating
/// between the start and end circles.
/// </summary>
public sealed class RadialGradientBrush : GradientBrush
{
    private readonly PointF center0;
    private readonly float radius0;
    private readonly PointF? center1; // null means single-circle form
    private readonly float? radius1;

    /// <summary>
    /// Initializes a new instance of the <see cref="RadialGradientBrush"/> class using a single circle.
    /// </summary>
    /// <param name="center">The center of the circular gradient.</param>
    /// <param name="radius">The radius of the circular gradient.</param>
    /// <param name="repetitionMode">Defines how the colors in the gradient are repeated.</param>
    /// <param name="colorStops">The ordered gradient stops.</param>
    public RadialGradientBrush(
        PointF center,
        float radius,
        GradientRepetitionMode repetitionMode,
        params ColorStop[] colorStops)
        : base(repetitionMode, colorStops)
    {
        this.center0 = center;
        this.radius0 = radius;
        this.center1 = null;
        this.radius1 = null;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RadialGradientBrush"/> class using two circles.
    /// </summary>
    /// <param name="startCenter">The center of the starting circle.</param>
    /// <param name="startRadius">The radius of the starting circle.</param>
    /// <param name="endCenter">The center of the ending circle.</param>
    /// <param name="endRadius">The radius of the ending circle.</param>
    /// <param name="repetitionMode">Defines how the colors in the gradient are repeated.</param>
    /// <param name="colorStops">The ordered gradient stops.</param>
    public RadialGradientBrush(
        PointF startCenter,
        float startRadius,
        PointF endCenter,
        float endRadius,
        GradientRepetitionMode repetitionMode,
        params ColorStop[] colorStops)
        : base(repetitionMode, colorStops)
    {
        this.center0 = startCenter;
        this.radius0 = startRadius;
        this.center1 = endCenter;
        this.radius1 = endRadius;
    }

    /// <inheritdoc/>
    public override bool Equals(Brush? other)
    {
        if (other is RadialGradientBrush b)
        {
            return base.Equals(other)
                && this.center0.Equals(b.center0)
                && this.radius0.Equals(b.radius0)
                && Nullable.Equals(this.center1, b.center1)
                && Nullable.Equals(this.radius1, b.radius1);
        }

        return false;
    }

    /// <inheritdoc/>
    public override int GetHashCode()
        => HashCode.Combine(base.GetHashCode(), this.center0, this.radius0, this.center1, this.radius1);

    /// <inheritdoc />
    public override BrushApplicator<TPixel> CreateApplicator<TPixel>(
        Configuration configuration,
        GraphicsOptions options,
        ImageFrame<TPixel> source,
        RectangleF region)
        => new RadialGradientBrushApplicator<TPixel>(
            configuration,
            options,
            source,
            this.center0,
            this.radius0,
            this.center1,
            this.radius1,
            this.ColorStops,
            this.RepetitionMode);

    /// <summary>
    /// The radial gradient brush applicator.
    /// </summary>
    private sealed class RadialGradientBrushApplicator<TPixel> : GradientBrushApplicator<TPixel>
        where TPixel : unmanaged, IPixel<TPixel>
    {
        // Single-circle fields
        private readonly bool isTwoCircle;
        private readonly float c0x;
        private readonly float c0y;
        private readonly float r0;

        // Two-circle fields
        private readonly float c1x;
        private readonly float c1y;
        private readonly float r1;

        // Precomputed for two-circle solve
        private readonly float dx;
        private readonly float dy;
        private readonly float dd;   // d·d
        private readonly float dr;   // r1 - r0
        private readonly float denom;    // dd - dr^2

        /// <summary>
        /// Initializes a new instance of the <see cref="RadialGradientBrushApplicator{TPixel}" /> class.
        /// </summary>
        /// <param name="configuration">The configuration instance to use when performing operations.</param>
        /// <param name="options">The graphics options.</param>
        /// <param name="target">The target image.</param>
        /// <param name="center0">Center of the starting circle.</param>
        /// <param name="radius0">Radius of the starting circle.</param>
        /// <param name="center1">Center of the ending circle, or null to use single-circle form.</param>
        /// <param name="radius1">Radius of the ending circle, or null to use single-circle form.</param>
        /// <param name="colorStops">Definition of colors.</param>
        /// <param name="repetitionMode">How the colors are repeated beyond the first gradient.</param>
        public RadialGradientBrushApplicator(
            Configuration configuration,
            GraphicsOptions options,
            ImageFrame<TPixel> target,
            PointF center0,
            float radius0,
            PointF? center1,
            float? radius1,
            ColorStop[] colorStops,
            GradientRepetitionMode repetitionMode)
            : base(configuration, options, target, colorStops, repetitionMode)
        {
            this.c0x = center0.X;
            this.c0y = center0.Y;
            this.r0 = radius0;

            this.isTwoCircle = center1.HasValue && radius1.HasValue;

            if (this.isTwoCircle)
            {
                this.c1x = center1!.Value.X;
                this.c1y = center1.Value.Y;
                this.r1 = radius1!.Value;

                this.dx = this.c1x - this.c0x;
                this.dy = this.c1y - this.c0y;
                this.dd = (this.dx * this.dx) + (this.dy * this.dy);
                this.dr = this.r1 - this.r0;

                // A = |d|^2 - dr^2
                this.denom = this.dd - (this.dr * this.dr);
            }
            else
            {
                this.c1x = 0F;
                this.c1y = 0F;
                this.r1 = 0F;
                this.dx = 0F;
                this.dy = 0F;
                this.dd = 0F;
                this.dr = 0F;
                this.denom = 0F;
            }
        }

        /// <inheritdoc/>
        protected override float PositionOnGradient(float x, float y)
        {
            if (!this.isTwoCircle)
            {
                float ux = x - this.c0x, uy = y - this.c0y;
                return MathF.Sqrt((ux * ux) + (uy * uy)) / this.r0;
            }

            float qx = x - this.c0x, qy = y - this.c0y;

            // Concentric case: centers equal -> dd == 0
            if (this.dd == 0f)
            {
                // t = (|p-c0| - r0) / (r1 - r0)
                float dist = MathF.Sqrt((qx * qx) + (qy * qy));
                float invDr = 1f / MathF.Max(MathF.Abs(this.dr), 1e-20f);
                return (dist - this.r0) * invDr;
            }

            // General two-circle fast form:
            // t = ((q·d) - r0*dr) / (|d|^2 - dr^2)
            if (this.denom == 0f)
            {
                // Near-singular; fall back to concentric-like ratio
                float dist = MathF.Sqrt((qx * qx) + (qy * qy));
                float invDr = 1f / MathF.Max(MathF.Abs(this.dr), 1e-20f);
                return (dist - this.r0) * invDr;
            }

            float num = (qx * this.dx) + (qy * this.dy) - (this.r0 * this.dr);
            return num / this.denom;
        }
    }
}
