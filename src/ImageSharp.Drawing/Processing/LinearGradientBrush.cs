// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using SixLabors.ImageSharp.Memory;

namespace SixLabors.ImageSharp.Drawing.Processing;

/// <summary>
/// Provides a brush that paints linear gradients within an area.
/// Supports both classic two-point gradients and three-point (rotated) gradients.
/// </summary>
public sealed class LinearGradientBrush : GradientBrush
{
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
        this.StartPoint = p0;
        this.EndPoint = p1;
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
        ResolveAxis(p0, p1, rotationPoint, out PointF start, out PointF end);
        this.StartPoint = start;
        this.EndPoint = end;
    }

    /// <summary>
    /// Gets the start point of the gradient axis.
    /// </summary>
    public PointF StartPoint { get; }

    /// <summary>
    /// Gets the end point of the gradient axis.
    /// </summary>
    public PointF EndPoint { get; }

    /// <inheritdoc/>
    public override Brush Transform(Matrix4x4 matrix)
        => new LinearGradientBrush(
            PointF.Transform(this.StartPoint, matrix),
            PointF.Transform(this.EndPoint, matrix),
            this.RepetitionMode,
            this.ColorStopsArray);

    /// <inheritdoc/>
    public override bool Equals(Brush? other)
    {
        if (other is LinearGradientBrush brush)
        {
            return base.Equals(other)
                && this.StartPoint.Equals(brush.StartPoint)
                && this.EndPoint.Equals(brush.EndPoint);
        }

        return false;
    }

    /// <inheritdoc/>
    public override int GetHashCode()
        => HashCode.Combine(base.GetHashCode(), this.StartPoint, this.EndPoint);

    /// <summary>
    /// Resolves a three-point gradient axis into a two-point axis by projecting
    /// the gradient vector (p0→p1) onto the perpendicular of the rotation vector (p0→rotationPoint).
    /// This follows the COLRv1 font specification for rotated linear gradients.
    /// </summary>
    /// <param name="p0">The gradient start point.</param>
    /// <param name="p1">The gradient vector endpoint.</param>
    /// <param name="rotationPoint">The rotation reference point.</param>
    /// <param name="start">The resolved start point of the gradient axis.</param>
    /// <param name="end">The resolved end point of the gradient axis.</param>
    private static void ResolveAxis(PointF p0, PointF p1, PointF rotationPoint, out PointF start, out PointF end)
    {
        // Gradient vector from p0 to p1.
        float vx = p1.X - p0.X;
        float vy = p1.Y - p0.Y;

        // Rotation vector from p0 to rotation point.
        float rx = rotationPoint.X - p0.X;
        float ry = rotationPoint.Y - p0.Y;

        // Perpendicular to the rotation vector.
        float nx = ry;
        float ny = -rx;

        float ndotn = (nx * nx) + (ny * ny);
        if (ndotn == 0f)
        {
            // Degenerate: p0 == rotationPoint, fall back to original axis.
            start = p0;
            end = p1;
        }
        else
        {
            // Project the gradient vector onto the perpendicular direction.
            float vdotn = (vx * nx) + (vy * ny);
            float scale = vdotn / ndotn;
            start = p0;
            end = new PointF(p0.X + (scale * nx), p0.Y + (scale * ny));
        }
    }

    /// <inheritdoc/>
    public override BrushRenderer<TPixel> CreateRenderer<TPixel>(
        Configuration configuration,
        GraphicsOptions options,
        int canvasWidth,
        RectangleF region)
        => new LinearGradientBrushRenderer<TPixel>(
            configuration,
            options,
            canvasWidth,
            this,
            this.ColorStopsArray,
            this.RepetitionMode);

    /// <summary>
    /// Implements the gradient application logic for <see cref="LinearGradientBrush"/>.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    private sealed class LinearGradientBrushRenderer<TPixel> : GradientBrushRenderer<TPixel>
        where TPixel : unmanaged, IPixel<TPixel>
    {
        private readonly PointF start;
        private readonly float alongX;
        private readonly float alongY;
        private readonly float alongsSquared;

        /// <summary>
        /// Initializes a new instance of the <see cref="LinearGradientBrushRenderer{TPixel}"/> class.
        /// </summary>
        /// <param name="configuration">The ImageSharp configuration.</param>
        /// <param name="options">The graphics options.</param>
        /// <param name="canvasWidth">The canvas width for the current render pass.</param>
        /// <param name="brush">The linear gradient brush.</param>
        /// <param name="colorStops">The gradient color stops.</param>
        /// <param name="repetitionMode">Defines how the gradient repeats.</param>
        public LinearGradientBrushRenderer(
            Configuration configuration,
            GraphicsOptions options,
            int canvasWidth,
            LinearGradientBrush brush,
            ColorStop[] colorStops,
            GradientRepetitionMode repetitionMode)
            : base(configuration, options, canvasWidth, colorStops, repetitionMode)
        {
            this.start = brush.StartPoint;

            this.alongX = brush.EndPoint.X - this.start.X;
            this.alongY = brush.EndPoint.Y - this.start.Y;
            this.alongsSquared = (this.alongX * this.alongX) + (this.alongY * this.alongY);
        }

        /// <inheritdoc/>
        protected override float PositionOnGradient(float x, float y)
        {
            if (this.alongsSquared == 0f)
            {
                return 1f;
            }

            float deltaX = x - this.start.X;
            float deltaY = y - this.start.Y;
            return ((deltaX * this.alongX) + (deltaY * this.alongY)) / this.alongsSquared;
        }
    }
}
