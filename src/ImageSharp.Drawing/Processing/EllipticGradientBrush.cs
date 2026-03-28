// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
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

    /// <inheritdoc/>
    public override Brush Transform(Matrix4x4 matrix)
    {
        PointF tc = PointF.Transform(this.Center, matrix);
        PointF tRef = PointF.Transform(this.ReferenceAxisEnd, matrix);

        // Compute a point on the perpendicular (secondary) axis and transform it.
        float refDx = this.ReferenceAxisEnd.X - this.Center.X;
        float refDy = this.ReferenceAxisEnd.Y - this.Center.Y;
        float refLen = MathF.Sqrt((refDx * refDx) + (refDy * refDy));
        float secondLen = refLen * this.AxisRatio;

        // Perpendicular direction (rotated 90 degrees).
        PointF secondEnd = new(
            this.Center.X + (-refDy / refLen * secondLen),
            this.Center.Y + (refDx / refLen * secondLen));
        PointF tSec = PointF.Transform(secondEnd, matrix);

        // Derive new ratio from transformed lengths.
        float newRefLen = MathF.Sqrt(
            ((tRef.X - tc.X) * (tRef.X - tc.X)) + ((tRef.Y - tc.Y) * (tRef.Y - tc.Y)));
        float newSecLen = MathF.Sqrt(
            ((tSec.X - tc.X) * (tSec.X - tc.X)) + ((tSec.Y - tc.Y) * (tSec.Y - tc.Y)));
        float newRatio = newRefLen > 0f ? newSecLen / newRefLen : this.AxisRatio;

        return new EllipticGradientBrush(tc, tRef, newRatio, this.RepetitionMode, this.ColorStopsArray);
    }

    /// <inheritdoc />
    public override BrushRenderer<TPixel> CreateRenderer<TPixel>(
        Configuration configuration,
        GraphicsOptions options,
        int canvasWidth,
        RectangleF region) =>
        new EllipticGradientBrushRenderer<TPixel>(
            configuration,
            options,
            canvasWidth,
            this,
            this.ColorStopsArray,
            this.RepetitionMode);

    /// <inheritdoc />
    private sealed class EllipticGradientBrushRenderer<TPixel> : GradientBrushRenderer<TPixel>
        where TPixel : unmanaged, IPixel<TPixel>
    {
        private readonly PointF center;

        private readonly float cosRotation;

        private readonly float sinRotation;

        private readonly float referenceRadiusSquared;

        private readonly float secondRadiusSquared;

        /// <summary>
        /// Initializes a new instance of the <see cref="EllipticGradientBrushRenderer{TPixel}" /> class.
        /// </summary>
        /// <param name="configuration">The configuration instance to use when performing operations.</param>
        /// <param name="options">The graphics options.</param>
        /// <param name="canvasWidth">The canvas width for the current render pass.</param>
        /// <param name="brush">The elliptic gradient brush.</param>
        /// <param name="colorStops">Definition of colors.</param>
        /// <param name="repetitionMode">Defines how the gradient colors are repeated.</param>
        public EllipticGradientBrushRenderer(
            Configuration configuration,
            GraphicsOptions options,
            int canvasWidth,
            EllipticGradientBrush brush,
            ColorStop[] colorStops,
            GradientRepetitionMode repetitionMode)
            : base(configuration, options, canvasWidth, colorStops, repetitionMode)
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

            return MathF.Sqrt((xSquared / this.referenceRadiusSquared) + (ySquared / this.secondRadiusSquared));
        }
    }
}
