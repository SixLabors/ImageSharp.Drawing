// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Processing;

/// <summary>
/// A radial gradient brush defined by either one circle or two circles.
/// When one circle is provided, the gradient parameter is the distance from the center divided by the radius.
/// When two circles are provided, the gradient parameter is computed along the family of circles interpolating
/// between the start and end circles.
/// </summary>
public sealed class RadialGradientBrush : GradientBrush
{
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
        this.Center0 = center;
        this.Radius0 = radius;
        this.Center1 = null;
        this.Radius1 = null;
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
        this.Center0 = startCenter;
        this.Radius0 = startRadius;
        this.Center1 = endCenter;
        this.Radius1 = endRadius;
    }

    /// <summary>
    /// Gets the center of the starting circle.
    /// </summary>
    public PointF Center0 { get; }

    /// <summary>
    /// Gets the radius of the starting circle.
    /// </summary>
    public float Radius0 { get; }

    /// <summary>
    /// Gets the center of the ending circle, or <see langword="null"/> for single-circle form.
    /// </summary>
    public PointF? Center1 { get; }

    /// <summary>
    /// Gets the radius of the ending circle, or <see langword="null"/> for single-circle form.
    /// </summary>
    public float? Radius1 { get; }

    /// <summary>
    /// Gets a value indicating whether this is a two-circle radial gradient.
    /// </summary>
    public bool IsTwoCircle => this.Center1.HasValue && this.Radius1.HasValue;

    /// <inheritdoc/>
    public override Brush Transform(Matrix4x4 matrix)
    {
        PointF tc0 = PointF.Transform(this.Center0, matrix);
        float scale = MatrixUtilities.GetAverageScale(in matrix);
        if (this.IsTwoCircle)
        {
            PointF tc1 = PointF.Transform(this.Center1!.Value, matrix);
            return new RadialGradientBrush(tc0, this.Radius0 * scale, tc1, this.Radius1!.Value * scale, this.RepetitionMode, this.ColorStopsArray);
        }

        return new RadialGradientBrush(tc0, this.Radius0 * scale, this.RepetitionMode, this.ColorStopsArray);
    }

    /// <inheritdoc/>
    public override bool Equals(Brush? other)
    {
        if (other is RadialGradientBrush b)
        {
            return base.Equals(other)
                && this.Center0.Equals(b.Center0)
                && this.Radius0.Equals(b.Radius0)
                && Nullable.Equals(this.Center1, b.Center1)
                && Nullable.Equals(this.Radius1, b.Radius1);
        }

        return false;
    }

    /// <inheritdoc/>
    public override int GetHashCode()
        => HashCode.Combine(base.GetHashCode(), this.Center0, this.Radius0, this.Center1, this.Radius1);

    /// <inheritdoc />
    public override BrushRenderer<TPixel> CreateRenderer<TPixel>(
        Configuration configuration,
        GraphicsOptions options,
        int canvasWidth,
        RectangleF region)
        => new RadialGradientBrushRenderer<TPixel>(
            configuration,
            options,
            canvasWidth,
            this.Center0,
            this.Radius0,
            this.Center1,
            this.Radius1,
            this.ColorStopsArray,
            this.RepetitionMode);

    /// <summary>
    /// The radial gradient brush applicator.
    /// </summary>
    private sealed class RadialGradientBrushRenderer<TPixel> : GradientBrushRenderer<TPixel>
        where TPixel : unmanaged, IPixel<TPixel>
    {
        private const float GradientEpsilon = 1F / (1 << 12);

        // Single-circle fields
        private readonly bool isTwoCircle;
        private readonly float c0x;
        private readonly float c0y;
        private readonly float r0;

        // Two-circle gradient fields.
        // The transform changes coordinates so the gradient can be evaluated
        // with simple formulas around a canonical line/circle configuration.
        private readonly Matrix3x2 radialTransform;
        private readonly float focalX;
        private readonly float radius;
        private readonly bool isStrip;
        private readonly bool isCircular;
        private readonly bool isFocalOnCircle;
        private readonly bool isSwapped;

        /// <summary>
        /// Initializes a new instance of the <see cref="RadialGradientBrushRenderer{TPixel}" /> class.
        /// </summary>
        /// <param name="configuration">The configuration instance to use when performing operations.</param>
        /// <param name="options">The graphics options.</param>
        /// <param name="canvasWidth">The canvas width for the current render pass.</param>
        /// <param name="center0">Center of the starting circle.</param>
        /// <param name="radius0">Radius of the starting circle.</param>
        /// <param name="center1">Center of the ending circle, or null to use single-circle form.</param>
        /// <param name="radius1">Radius of the ending circle, or null to use single-circle form.</param>
        /// <param name="colorStops">Definition of colors.</param>
        /// <param name="repetitionMode">How the colors are repeated beyond the first gradient.</param>
        public RadialGradientBrushRenderer(
            Configuration configuration,
            GraphicsOptions options,
            int canvasWidth,
            PointF center0,
            float radius0,
            PointF? center1,
            float? radius1,
            ColorStop[] colorStops,
            GradientRepetitionMode repetitionMode)
            : base(configuration, options, canvasWidth, colorStops, repetitionMode)
        {
            this.c0x = center0.X;
            this.c0y = center0.Y;
            this.r0 = radius0;

            this.isTwoCircle = center1.HasValue && radius1.HasValue;

            if (this.isTwoCircle)
            {
                ConicalGradientParameters parameters = CreateConicalGradientParameters(
                    center0,
                    radius0,
                    center1!.Value,
                    radius1!.Value);

                this.radialTransform = parameters.Transform;
                this.focalX = parameters.FocalX;
                this.radius = parameters.Radius;
                this.isStrip = parameters.IsStrip;
                this.isCircular = parameters.IsCircular;
                this.isFocalOnCircle = parameters.IsFocalOnCircle;
                this.isSwapped = parameters.IsSwapped;
            }
            else
            {
                this.radialTransform = Matrix3x2.Identity;
                this.focalX = 0F;
                this.radius = 0F;
                this.isStrip = false;
                this.isCircular = false;
                this.isFocalOnCircle = false;
                this.isSwapped = false;
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

            // Move the sample into the canonical coordinate system where the
            // end circle lies on the x-axis and the conic can be solved using
            // closed-form expressions.
            Vector2 local = Vector2.Transform(new Vector2(x, y), this.radialTransform);
            float localX = local.X;
            float localY = local.Y;
            float xx = localX * localX;
            float yy = localY * localY;
            float t;

            if (this.isStrip)
            {
                // Strip gradients are bounded by a band around the axis.
                // radius stores the squared half-width in normalized space,
                // so points outside the band are invalid.
                float a = this.radius - yy;
                if (a < 0F)
                {
                    return float.NaN;
                }

                // Once inside the band, the parameter advances along the axis.
                t = MathF.Sqrt(a) + localX;
            }
            else if (this.isFocalOnCircle)
            {
                // This degenerate case reduces to a rational expression where
                // the focal point sits exactly on the limiting circle.
                if (localX == 0F)
                {
                    return float.NaN;
                }

                t = (xx + yy) / localX;
                if (t < 0F)
                {
                    return float.NaN;
                }
            }
            else if (this.radius > 1F)
            {
                // Wide cones use a circular norm. The x term shifts the root
                // back into the original gradient parameterization.
                float radiusReciprocal = this.isCircular ? 0F : 1F / this.radius;
                t = MathF.Sqrt(xx + yy) - (localX * radiusReciprocal);
            }
            else
            {
                // Narrow cones use a hyperbolic form. Points with x^2 < y^2
                // lie outside the valid branch and must not contribute.
                float a = xx - yy;
                if (a < 0F)
                {
                    return float.NaN;
                }

                // lessScale picks the correct branch of the hyperbola after
                // swaps and orientation changes.
                float lessScale = (this.isSwapped || (1F - this.focalX) < 0F) ? -1F : 1F;
                t = (lessScale * MathF.Sqrt(a)) - (localX / this.radius);
                if (t < 0F)
                {
                    return float.NaN;
                }
            }

            // Convert back from the normalized local solution into the brush's
            // gradient parameter, then undo the earlier swap if required.
            t = this.focalX + (MathF.Sign(1F - this.focalX) * t);
            return this.isSwapped ? 1F - t : t;
        }

        private static ConicalGradientParameters CreateConicalGradientParameters(
            PointF center0,
            float radius0,
            PointF center1,
            float radius1)
        {
            PointF p0 = center0;
            PointF p1 = center1;
            float r0 = radius0;
            float r1 = radius1;

            if (MathF.Abs(r0 - r1) <= GradientEpsilon)
            {
                // When both circles have the same radius, the locus becomes a
                // strip: solve along the axis between the centers, with the
                // radius contributing only a perpendicular cutoff.
                float scaled = r0 / Distance(p0, p1);
                return new ConicalGradientParameters(
                    TwoPointToUnitLine(p0, p1),
                    0F,
                    scaled * scaled,
                    IsStrip: true,
                    IsCircular: false,
                    IsFocalOnCircle: false,
                    IsSwapped: false);
            }

            bool isCircular = false;
            if (p0 == p1)
            {
                isCircular = true;

                // Equal centers make the conic circular. Nudge slightly so the
                // line construction below stays invertible.
                p0 = new PointF(p0.X + GradientEpsilon, p0.Y + GradientEpsilon);
            }

            bool isSwapped = false;
            if (r1 == 0F)
            {
                isSwapped = true;

                // Put the zero-radius focus on the start side so the later
                // formulas keep one orientation.
                (p0, p1) = (p1, p0);
                (r0, r1) = (r1, r0);
            }

            // focalX describes where the focal point lies along the line from
            // the start circle to the end circle. Values outside [0, 1] are
            // valid and correspond to cones whose focus lies beyond an endpoint.
            float focalX = r0 / (r0 - r1);
            PointF cf = new(
                ((1F - focalX) * p0.X) + (focalX * p1.X),
                ((1F - focalX) * p0.Y) + (focalX * p1.Y));

            // radius is the end-circle radius expressed in the normalized frame
            // built from the focal point and the end center.
            float radius = r1 / Distance(cf, p1);
            Matrix3x2 userToUnitLine = TwoPointToUnitLine(cf, p1);
            Matrix3x2 transform;
            bool isFocalOnCircle = false;

            if (MathF.Abs(radius - 1F) <= GradientEpsilon)
            {
                isFocalOnCircle = true;

                // When the focal point lies on the circle, the quadratic terms
                // collapse to a simpler rational form.
                float scale = 0.5F * MathF.Abs(1F - focalX);
                transform = userToUnitLine * Matrix3x2.CreateScale(scale);
            }
            else
            {
                // Otherwise scale the unit-line frame so the gradient can be
                // tested with either x^2 + y^2 or x^2 - y^2, depending on
                // whether the cone opens wider or narrower than the unit case.
                float a = (radius * radius) - 1F;
                float scaleRatio = MathF.Abs(1F - focalX) / a;
                float scaleX = radius * scaleRatio;
                float scaleY = MathF.Sqrt(MathF.Abs(a)) * scaleRatio;
                transform = userToUnitLine * Matrix3x2.CreateScale(scaleX, scaleY);
            }

            return new ConicalGradientParameters(
                transform,
                focalX,
                radius,
                IsStrip: false,
                IsCircular: isCircular,
                IsFocalOnCircle: isFocalOnCircle,
                IsSwapped: isSwapped);
        }

        private static float Distance(Vector2 p0, Vector2 p1) => Vector2.Distance(p0, p1);

        private static Matrix3x2 TwoPointToUnitLine(PointF p0, PointF p1)
        {
            // Build a change-of-basis that sends the segment p0->p1 to the
            // unit line. That lets the gradient math work in one fixed frame
            // instead of re-deriving equations for every brush.
            Matrix3x2 source = FromPoly2(p0, p1);
            Matrix3x2.Invert(source, out Matrix3x2 inverse);
            return inverse * FromPoly2(new PointF(0F, 0F), new PointF(1F, 0F));
        }

        private static Matrix3x2 FromPoly2(PointF p0, PointF p1)

            // This affine frame uses p0 as the origin and p0->p1 as one axis.
            // Its inverse is the basis change we need for normalization.
            => new(
                p1.Y - p0.Y,
                p0.X - p1.X,
                p1.X - p0.X,
                p1.Y - p0.Y,
                p0.X,
                p0.Y);

        private readonly record struct ConicalGradientParameters(
            Matrix3x2 Transform,
            float FocalX,
            float Radius,
            bool IsStrip,
            bool IsCircular,
            bool IsFocalOnCircle,
            bool IsSwapped);
    }
}
