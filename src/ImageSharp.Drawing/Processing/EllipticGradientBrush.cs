// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Memory;

namespace SixLabors.ImageSharp.Drawing.Processing;

/// <summary>
/// Gradient Brush with elliptic shape.
/// The ellipse is defined by a center point,
/// a point on the longest extension of the ellipse and
/// the ratio between longest and shortest extension.
/// </summary>
public sealed class EllipticGradientBrush : GradientBrush
{
    /// <inheritdoc cref="GradientBrush" />
    /// <param name="center">The center of the elliptical gradient and 0 for the color stops.</param>
    /// <param name="referenceAxisEnd">The end point of the reference axis of the ellipse.</param>
    /// <param name="axisRatio">
    ///   The ratio of the axis widths.
    ///   The second axis' is perpendicular to the reference axis and
    ///   it's length is the reference axis' length multiplied by this factor.
    /// </param>
    /// <param name="repetitionMode">Defines how the colors of the gradients are repeated.</param>
    /// <param name="colorStops">the color stops as defined in base class.</param>
    public EllipticGradientBrush(
        PointF center,
        PointF referenceAxisEnd,
        float axisRatio,
        GradientRepetitionMode repetitionMode,
        params ColorStop[] colorStops)
        : base(repetitionMode, colorStops)
    {
        this.Center = center;
        this.ReferenceAxisEnd = referenceAxisEnd;
        this.AxisRatio = axisRatio;
    }

    /// <summary>
    /// Gets the center of the ellipse.
    /// </summary>
    public PointF Center { get; }

    /// <summary>
    /// Gets the end point of the reference axis.
    /// </summary>
    public PointF ReferenceAxisEnd { get; }

    /// <summary>
    /// Gets the ratio of the secondary axis to the primary axis.
    /// </summary>
    public float AxisRatio { get; }

    /// <inheritdoc />
    public override BrushApplicator<TPixel> CreateApplicator<TPixel>(
        Configuration configuration,
        GraphicsOptions options,
        Buffer2DRegion<TPixel> targetRegion,
        RectangleF region) =>
        new EllipticGradientBrushApplicator<TPixel>(
            configuration,
            options,
            targetRegion,
            this,
            this.ColorStopsArray,
            this.RepetitionMode);

    /// <inheritdoc />
    private sealed class EllipticGradientBrushApplicator<TPixel> : GradientBrushApplicator<TPixel>
        where TPixel : unmanaged, IPixel<TPixel>
    {
        private readonly PointF center;

        private readonly float cosRotation;

        private readonly float sinRotation;

        private readonly float referenceRadiusSquared;

        private readonly float secondRadiusSquared;

        /// <summary>
        /// Initializes a new instance of the <see cref="EllipticGradientBrushApplicator{TPixel}" /> class.
        /// </summary>
        /// <param name="configuration">The configuration instance to use when performing operations.</param>
        /// <param name="options">The graphics options.</param>
        /// <param name="targetRegion">The destination pixel region.</param>
        /// <param name="brush">The elliptic gradient brush.</param>
        /// <param name="colorStops">Definition of colors.</param>
        /// <param name="repetitionMode">Defines how the gradient colors are repeated.</param>
        public EllipticGradientBrushApplicator(
            Configuration configuration,
            GraphicsOptions options,
            Buffer2DRegion<TPixel> targetRegion,
            EllipticGradientBrush brush,
            ColorStop[] colorStops,
            GradientRepetitionMode repetitionMode)
            : base(configuration, options, targetRegion, colorStops, repetitionMode)
        {
            this.center = brush.Center;

            float refDx = brush.ReferenceAxisEnd.X - brush.Center.X;
            float refDy = brush.ReferenceAxisEnd.Y - brush.Center.Y;
            float rotation = MathF.Atan2(refDy, refDx);
            float referenceRadius = MathF.Sqrt((refDx * refDx) + (refDy * refDy));
            float secondRadius = referenceRadius * brush.AxisRatio;

            this.referenceRadiusSquared = referenceRadius * referenceRadius;
            this.secondRadiusSquared = secondRadius * secondRadius;
            this.sinRotation = MathF.Sin(rotation);
            this.cosRotation = MathF.Cos(rotation);
        }

        /// <inheritdoc />
        protected override float PositionOnGradient(float x, float y)
        {
            float x0 = x - this.center.X;
            float y0 = y - this.center.Y;

            float xR = (x0 * this.cosRotation) - (y0 * this.sinRotation);
            float yR = (x0 * this.sinRotation) + (y0 * this.cosRotation);

            float xSquared = xR * xR;
            float ySquared = yR * yR;

            return (xSquared / this.referenceRadiusSquared) + (ySquared / this.secondRadiusSquared);
        }
    }
}
