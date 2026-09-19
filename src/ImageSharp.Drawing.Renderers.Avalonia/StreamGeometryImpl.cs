// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using Avalonia;
using Avalonia.Media;
using Avalonia.Platform;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;

namespace SixLabors.ImageSharp.Drawing.Renderers.Avalonia;

/// <summary>
/// Mutable stream geometry implementation backed by ImageSharp path builders.
/// </summary>
internal sealed class StreamGeometryImpl : GeometryImpl, IStreamGeometryImpl
{
    private IPath strokePath = EmptyPath.OpenPath;
    private IPath? fillPath = EmptyPath.OpenPath;

    /// <summary>
    /// Initializes an empty stream geometry.
    /// </summary>
    public StreamGeometryImpl()
        : base(EmptyPath.OpenPath, EmptyPath.OpenPath, IntersectionRule.EvenOdd)
    {
    }

    internal StreamGeometryImpl(IPath strokePath, IPath? fillPath, IntersectionRule fillRule)
        : base(strokePath, fillPath, fillRule)
    {
        this.strokePath = strokePath;
        this.fillPath = fillPath;
    }

    /// <inheritdoc />
    public IStreamGeometryImpl Clone() => new StreamGeometryImpl(this.strokePath, this.fillPath, this.FillRule);

    /// <inheritdoc />
    public IStreamGeometryContextImpl Open() => new StreamGeometryImplContext(this);

    private void SetStreamPaths(IPath strokePath, IPath? fillPath, IntersectionRule fillRule)
    {
        this.strokePath = strokePath;
        this.fillPath = fillPath;
        this.SetPaths(strokePath, fillPath, fillRule);
    }

    /// <summary>
    /// Stream geometry context that records Avalonia path commands into ImageSharp path builders.
    /// </summary>
    private sealed class StreamGeometryImplContext : IStreamGeometryContextImpl
    {
        private readonly StreamGeometryImpl owner;
        private readonly PathBuilder strokeBuilder = new();
        private readonly PathBuilder fillBuilder = new();
        private PointF startPoint;
        private bool isFilled;
        private bool isFigureBroken;
        private bool hasFill;
        private IntersectionRule fillRule = IntersectionRule.EvenOdd;

        /// <summary>
        /// Initializes a new stream geometry context.
        /// </summary>
        /// <param name="owner">The stream geometry updated when the context is disposed.</param>
        public StreamGeometryImplContext(StreamGeometryImpl owner)
            => this.owner = owner;

        /// <inheritdoc />
        public void ArcTo(AvaloniaPoint point, AvaloniaSize size, double rotationAngle, bool isLargeArc, SweepDirection sweepDirection, bool isStroked = true)
        {
            PointF endPoint = point.ToPointF();
            bool sweep = sweepDirection == SweepDirection.Clockwise;

            // Avalonia's rotationAngle is nominally radians, but the Skia backend passes the raw value
            // straight to SKPath.ArcTo, whose x-axis rotation is degrees - so callers (e.g. the rounded
            // rectangle builder, which passes PiOver2) are effectively calibrated against a degrees-valued
            // axis rotation. Match Skia by forwarding the value unchanged rather than converting to degrees.
            // See https://github.com/AvaloniaUI/Avalonia/issues/21445.
            float rotation = (float)rotationAngle;

            if (isStroked)
            {
                this.strokeBuilder.ArcTo((float)size.Width, (float)size.Height, rotation, isLargeArc, sweep, endPoint);
            }
            else
            {
                this.BreakStrokeFigure(endPoint);
            }

            if (this.isFilled)
            {
                this.fillBuilder.ArcTo((float)size.Width, (float)size.Height, rotation, isLargeArc, sweep, endPoint);
            }
        }

        /// <inheritdoc />
        public void BeginFigure(AvaloniaPoint startPoint, bool isFilled = true)
        {
            this.startPoint = startPoint.ToPointF();
            this.isFilled = isFilled;
            this.isFigureBroken = false;
            this.strokeBuilder.MoveTo(this.startPoint);

            if (isFilled)
            {
                this.hasFill = true;
                this.fillBuilder.MoveTo(this.startPoint);
            }
        }

        /// <inheritdoc />
        public void CubicBezierTo(AvaloniaPoint controlPoint1, AvaloniaPoint controlPoint2, AvaloniaPoint endPoint, bool isStroked = true)
        {
            PointF imageEndPoint = endPoint.ToPointF();

            if (isStroked)
            {
                this.strokeBuilder.CubicBezierTo(
                    controlPoint1.ToPointF(),
                    controlPoint2.ToPointF(),
                    imageEndPoint);
            }
            else
            {
                this.BreakStrokeFigure(imageEndPoint);
            }

            if (this.isFilled)
            {
                this.fillBuilder.CubicBezierTo(
                    controlPoint1.ToPointF(),
                    controlPoint2.ToPointF(),
                    imageEndPoint);
            }
        }

        /// <inheritdoc />
        public void QuadraticBezierTo(AvaloniaPoint controlPoint, AvaloniaPoint endPoint, bool isStroked = true)
        {
            PointF imageEndPoint = endPoint.ToPointF();

            if (isStroked)
            {
                this.strokeBuilder.QuadraticBezierTo(controlPoint.ToPointF(), imageEndPoint);
            }
            else
            {
                this.BreakStrokeFigure(imageEndPoint);
            }

            if (this.isFilled)
            {
                this.fillBuilder.QuadraticBezierTo(controlPoint.ToPointF(), imageEndPoint);
            }
        }

        /// <inheritdoc />
        public void LineTo(AvaloniaPoint point, bool isStroked = true)
        {
            PointF imagePoint = point.ToPointF();

            if (isStroked)
            {
                this.strokeBuilder.LineTo(imagePoint);
            }
            else
            {
                this.BreakStrokeFigure(imagePoint);
            }

            if (this.isFilled)
            {
                this.fillBuilder.LineTo(imagePoint);
            }
        }

        /// <inheritdoc />
        public void EndFigure(bool isClosed)
        {
            if (isClosed)
            {
                if (this.isFigureBroken)
                {
                    this.strokeBuilder.LineTo(this.startPoint);
                    this.isFigureBroken = false;
                }
                else
                {
                    this.strokeBuilder.CloseFigure();
                }

                if (this.isFilled)
                {
                    this.fillBuilder.CloseFigure();
                }
            }
        }

        /// <inheritdoc />
        public void SetFillRule(FillRule fillRule)
        {
            this.fillRule = fillRule.ToIntersectionRule();
        }

        /// <inheritdoc />
        public void Dispose()
            => this.owner.SetStreamPaths(this.strokeBuilder.Build(), this.hasFill ? this.fillBuilder.Build() : null, this.fillRule);

        private void BreakStrokeFigure(PointF point)
        {
            this.isFigureBroken = true;
            this.strokeBuilder.MoveTo(point);
        }
    }
}
