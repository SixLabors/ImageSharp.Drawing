// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;

namespace SixLabors.ImageSharp.Drawing.Processing;

/// <summary>
/// Gradient Brush with elliptic shape.
/// The ellipse is defined by a center point,
/// a point on the longest extension of the ellipse and
/// the ratio between longest and shortest extension.
/// </summary>
public sealed class EllipticGradientBrush : GradientBrush
{
    private readonly PointF center;

    private readonly PointF referenceAxisEnd;

    private readonly float axisRatio;

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
        this.center = center;
        this.referenceAxisEnd = referenceAxisEnd;
        this.axisRatio = axisRatio;
    }

    /// <inheritdoc />
    public override BrushApplicator<TPixel> CreateApplicator<TPixel>(
        Configuration configuration,
        GraphicsOptions options,
        ImageFrame<TPixel> source,
        RectangleF region) =>
        new RadialGradientBrushApplicator<TPixel>(
            configuration,
            options,
            source,
            this.center,
            this.referenceAxisEnd,
            this.axisRatio,
            this.ColorStops,
            this.RepetitionMode);

    /// <inheritdoc />
    private sealed class RadialGradientBrushApplicator<TPixel> : GradientBrushApplicator<TPixel>
        where TPixel : unmanaged, IPixel<TPixel>
    {
        private readonly PointF center;

        private readonly PointF referenceAxisEnd;

        private readonly float axisRatio;

        private readonly double rotation;

        private readonly float referenceRadius;

        private readonly float secondRadius;

        private readonly float cosRotation;

        private readonly float sinRotation;

        private readonly float secondRadiusSquared;

        private readonly float referenceRadiusSquared;

        /// <summary>
        /// Initializes a new instance of the <see cref="RadialGradientBrushApplicator{TPixel}" /> class.
        /// </summary>
        /// <param name="configuration">The configuration instance to use when performing operations.</param>
        /// <param name="options">The graphics options.</param>
        /// <param name="target">The target image.</param>
        /// <param name="center">Center of the ellipse.</param>
        /// <param name="referenceAxisEnd">Point on one angular points of the ellipse.</param>
        /// <param name="axisRatio">
        /// Ratio of the axis length's. Used to determine the length of the second axis,
        /// the first is defined by <see cref="center"/> and <see cref="referenceAxisEnd"/>.</param>
        /// <param name="colorStops">Definition of colors.</param>
        /// <param name="repetitionMode">Defines how the gradient colors are repeated.</param>
        public RadialGradientBrushApplicator(
            Configuration configuration,
            GraphicsOptions options,
            ImageFrame<TPixel> target,
            PointF center,
            PointF referenceAxisEnd,
            float axisRatio,
            ColorStop[] colorStops,
            GradientRepetitionMode repetitionMode)
            : base(configuration, options, target, colorStops, repetitionMode)
        {
            this.center = center;
            this.referenceAxisEnd = referenceAxisEnd;
            this.axisRatio = axisRatio;
            this.rotation = RadialGradientBrushApplicator<TPixel>.AngleBetween(
                this.center,
                new PointF(this.center.X + 1, this.center.Y),
                this.referenceAxisEnd);
            this.referenceRadius = RadialGradientBrushApplicator<TPixel>.DistanceBetween(this.center, this.referenceAxisEnd);
            this.secondRadius = this.referenceRadius * this.axisRatio;

            this.referenceRadiusSquared = this.referenceRadius * this.referenceRadius;
            this.secondRadiusSquared = this.secondRadius * this.secondRadius;

            this.sinRotation = (float)Math.Sin(this.rotation);
            this.cosRotation = (float)Math.Cos(this.rotation);
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

        private static float AngleBetween(PointF junction, PointF a, PointF b)
        {
            PointF vA = a - junction;
            PointF vB = b - junction;
            return MathF.Atan2(vB.Y, vB.X) - MathF.Atan2(vA.Y, vA.X);
        }

        private static float DistanceBetween(PointF p1, PointF p2) => Vector2.Distance(p1, p2);
    }
}
