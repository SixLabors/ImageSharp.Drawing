// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using System.Runtime.CompilerServices;

namespace SixLabors.ImageSharp.Drawing;

/// <summary>
/// Represents a line segment that contains radii and angles that will be rendered as a elliptical arc.
/// </summary>
public class ArcLineSegment : ILineSegment
{
    private const float ZeroTolerance = 1e-05F;
    private readonly PointF[] linePoints;

    /// <summary>
    /// Initializes a new instance of the <see cref="ArcLineSegment"/> class.
    /// </summary>
    /// <param name="from">The absolute coordinates of the current point on the path.</param>
    /// <param name="to">The absolute coordinates of the final point of the arc.</param>
    /// <param name="radius">The radii of the ellipse (also known as its semi-major and semi-minor axes).</param>
    /// <param name="rotation">The angle, in degrees, from the x-axis of the current coordinate system to the x-axis of the ellipse.</param>
    /// <param name="largeArc">
    /// The large arc flag, and is <see langword="false"/> if an arc spanning less than or equal to 180 degrees
    /// is chosen, or <see langword="true"/> if an arc spanning greater than 180 degrees is chosen.
    /// </param>
    /// <param name="sweep">
    /// The sweep flag, and is <see langword="false"/> if the line joining center to arc sweeps through decreasing
    /// angles, or <see langword="true"/> if it sweeps through increasing angles.
    /// </param>
    public ArcLineSegment(PointF from, PointF to, SizeF radius, float rotation, bool largeArc, bool sweep)
    {
        rotation = GeometryUtilities.DegreeToRadian(rotation);
        bool circle = largeArc && ((Vector2)to - (Vector2)from).LengthSquared() < ZeroTolerance && radius.Width > 0 && radius.Height > 0;
        this.linePoints = EllipticArcFromEndParams(from, to, radius, rotation, largeArc, sweep, circle);
        this.EndPoint = this.linePoints[this.linePoints.Length - 1];
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ArcLineSegment"/> class.
    /// </summary>
    /// <param name="center">The coordinates of the center of the ellipse.</param>
    /// <param name="radius">The radii of the ellipse (also known as its semi-major and semi-minor axes).</param>
    /// <param name="rotation">The angle, in degrees, from the x-axis of the current coordinate system to the x-axis of the ellipse.</param>
    /// <param name="startAngle">
    /// The start angle of the elliptical arc prior to the stretch and rotate operations.
    /// (0 is at the 3 o'clock position of the arc's circle).
    /// </param>
    /// <param name="sweepAngle">The angle between <paramref name="startAngle"/> and the end of the arc.</param>
    public ArcLineSegment(PointF center, SizeF radius, float rotation, float startAngle, float sweepAngle)
    {
        rotation = GeometryUtilities.DegreeToRadian(rotation);
        startAngle = GeometryUtilities.DegreeToRadian(Clamp(startAngle, -360F, 360F));
        sweepAngle = GeometryUtilities.DegreeToRadian(Clamp(sweepAngle, -360F, 360F));

        Vector2 from = EllipticArcPoint(center, radius, rotation, startAngle);
        Vector2 to = EllipticArcPoint(center, radius, rotation, startAngle + sweepAngle);

        bool largeArc = Math.Abs(sweepAngle) > MathF.PI;
        bool sweep = sweepAngle > 0;
        bool circle = largeArc && (to - from).LengthSquared() < ZeroTolerance && radius.Width > 0 && radius.Height > 0;

        this.linePoints = EllipticArcFromEndParams(from, to, radius, rotation, largeArc, sweep, circle);
        this.EndPoint = this.linePoints[this.linePoints.Length - 1];
    }

    private ArcLineSegment(PointF[] linePoints)
    {
        this.linePoints = linePoints;
        this.EndPoint = this.linePoints[this.linePoints.Length - 1];
    }

    /// <inheritdoc/>
    public PointF EndPoint { get; }

    /// <inheritdoc/>
    public ReadOnlyMemory<PointF> Flatten() => this.linePoints;

    /// <summary>
    /// Transforms the current <see cref="ArcLineSegment"/> using specified matrix.
    /// </summary>
    /// <param name="matrix">The transformation matrix.</param>
    /// <returns>An <see cref="ArcLineSegment"/> with the matrix applied to it.</returns>
    public ILineSegment Transform(Matrix3x2 matrix)
    {
        if (matrix.IsIdentity)
        {
            return this;
        }

        var transformedPoints = new PointF[this.linePoints.Length];
        for (int i = 0; i < this.linePoints.Length; i++)
        {
            transformedPoints[i] = PointF.Transform(this.linePoints[i], matrix);
        }

        return new ArcLineSegment(transformedPoints);
    }

    /// <inheritdoc/>
    ILineSegment ILineSegment.Transform(Matrix3x2 matrix) => this.Transform(matrix);

    private static PointF[] EllipticArcFromEndParams(PointF from, PointF to, SizeF radius, float rotation, bool largeArc, bool sweep, bool circle)
    {
        {
            var absRadius = Vector2.Abs(radius);

            if (circle)
            {
                // It's a circle. SVG arcs cannot handle this so let's hack together our own angles.
                // This appears to match the behavior of Web CanvasRenderingContext2D.arc().
                // https://developer.mozilla.org/en-US/docs/Web/API/CanvasRenderingContext2D/arc
                Vector2 center = (Vector2)from - new Vector2(absRadius.X, 0);
                return EllipticArcToBezierCurve(from, center, absRadius, rotation, 0, 2 * MathF.PI);
            }
            else
            {
                if (EllipticArcOutOfRange(from, to, radius))
                {
                    return new[] { from, to };
                }

                float xRotation = rotation;
                EndpointToCenterArcParams(from, to, ref absRadius, xRotation, largeArc, sweep, out Vector2 center, out Vector2 angles);

                return EllipticArcToBezierCurve(from, center, absRadius, xRotation, angles.X, angles.Y);
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool EllipticArcOutOfRange(Vector2 from, Vector2 to, Vector2 radius)
    {
        // F.6.2 Out-of-range parameters
        radius = Vector2.Abs(radius);
        float len = (to - from).LengthSquared();
        if (len < ZeroTolerance)
        {
            return true;
        }

        if (radius.X < ZeroTolerance || radius.Y < ZeroTolerance)
        {
            return true;
        }

        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector2 EllipticArcDerivative(Vector2 r, float xAngle, float t)
        => new(
            (-r.X * MathF.Cos(xAngle) * MathF.Sin(t)) - (r.Y * MathF.Sin(xAngle) * MathF.Cos(t)),
            (-r.X * MathF.Sin(xAngle) * MathF.Sin(t)) + (r.Y * MathF.Cos(xAngle) * MathF.Cos(t)));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector2 EllipticArcPoint(Vector2 c, Vector2 r, float xAngle, float t)
        => new(
            c.X + (r.X * MathF.Cos(xAngle) * MathF.Cos(t)) - (r.Y * MathF.Sin(xAngle) * MathF.Sin(t)),
            c.Y + (r.X * MathF.Sin(xAngle) * MathF.Cos(t)) + (r.Y * MathF.Cos(xAngle) * MathF.Sin(t)));

    private static PointF[] EllipticArcToBezierCurve(Vector2 from, Vector2 center, Vector2 radius, float xAngle, float startAngle, float sweepAngle)
    {
        List<PointF> points = new();

        float s = startAngle;
        float e = s + sweepAngle;
        bool neg = e < s;
        float sign = neg ? -1 : 1;
        float remain = Math.Abs(e - s);

        Vector2 prev = EllipticArcPoint(center, radius, xAngle, s);

        while (remain > ZeroTolerance)
        {
            float step = (float)Math.Min(remain, Math.PI / 4);
            float signStep = step * sign;

            Vector2 p1 = prev;
            Vector2 p2 = EllipticArcPoint(center, radius, xAngle, s + signStep);

            float alphaT = (float)Math.Tan(signStep / 2);
            float alpha = (float)(Math.Sin(signStep) * (Math.Sqrt(4 + (3 * alphaT * alphaT)) - 1) / 3);
            Vector2 q1 = p1 + (alpha * EllipticArcDerivative(radius, xAngle, s));
            Vector2 q2 = p2 - (alpha * EllipticArcDerivative(radius, xAngle, s + signStep));

            ReadOnlySpan<PointF> bezierPoints = new CubicBezierLineSegment(from, q1, q2, p2).Flatten().Span;
            for (int i = 0; i < bezierPoints.Length; i++)
            {
                points.Add(bezierPoints[i]);
            }

            from = p2;

            s += signStep;
            remain -= step;
            prev = p2;
        }

        return points.ToArray();
    }

    private static void EndpointToCenterArcParams(
        Vector2 p1,
        Vector2 p2,
        ref Vector2 r,
        float xRotation,
        bool flagA,
        bool flagS,
        out Vector2 center,
        out Vector2 angles)
    {
        double rX = Math.Abs(r.X);
        double rY = Math.Abs(r.Y);

        // (F.6.5.1)
        double dx2 = (p1.X - p2.X) / 2.0;
        double dy2 = (p1.Y - p2.Y) / 2.0;
        double x1p = (Math.Cos(xRotation) * dx2) + (Math.Sin(xRotation) * dy2);
        double y1p = (-Math.Sin(xRotation) * dx2) + (Math.Cos(xRotation) * dy2);

        // (F.6.5.2)
        double rxs = rX * rX;
        double rys = rY * rY;
        double x1ps = x1p * x1p;
        double y1ps = y1p * y1p;

        // check if the radius is too small `pq < 0`, when `dq > rxs * rys` (see below)
        // cr is the ratio (dq : rxs * rys)
        double cr = (x1ps / rxs) + (y1ps / rys);
        if (cr > 1)
        {
            // scale up rX,rY equally so cr == 1
            double s = Math.Sqrt(cr);
            rX = s * rX;
            rY = s * rY;
            rxs = rX * rX;
            rys = rY * rY;
        }

        double dq = (rxs * y1ps) + (rys * x1ps);
        double pq = ((rxs * rys) - dq) / dq;
        double q = Math.Sqrt(Math.Max(0, pq)); // Use Max to account for float precision
        if (flagA == flagS)
        {
            q = -q;
        }

        double cxp = q * rX * y1p / rY;
        double cyp = -q * rY * x1p / rX;

        // (F.6.5.3)
        double cx = (Math.Cos(xRotation) * cxp) - (Math.Sin(xRotation) * cyp) + ((p1.X + p2.X) / 2);
        double cy = (Math.Sin(xRotation) * cxp) + (Math.Cos(xRotation) * cyp) + ((p1.Y + p2.Y) / 2);

        // (F.6.5.5)
        double theta = SvgAngle(1, 0, (x1p - cxp) / rX, (y1p - cyp) / rY);

        // (F.6.5.6)
        double delta = SvgAngle((x1p - cxp) / rX, (y1p - cyp) / rY, (-x1p - cxp) / rX, (-y1p - cyp) / rY);
        delta %= Math.PI * 2;

        if (!flagS && delta > 0)
        {
            delta -= 2 * Math.PI;
        }

        if (flagS && delta < 0)
        {
            delta += 2 * Math.PI;
        }

        r = new Vector2((float)rX, (float)rY);
        center = new Vector2((float)cx, (float)cy);
        angles = new Vector2((float)theta, (float)delta);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float Clamp(float val, float min, float max)
    {
        if (val < min)
        {
            return min;
        }
        else if (val > max)
        {
            return max;
        }
        else
        {
            return val;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float SvgAngle(double ux, double uy, double vx, double vy)
    {
        var u = new Vector2((float)ux, (float)uy);
        var v = new Vector2((float)vx, (float)vy);

        // (F.6.5.4)
        float dot = Vector2.Dot(u, v);
        float len = u.Length() * v.Length();
        float ang = (float)Math.Acos(Clamp(dot / len, -1, 1)); // floating point precision, slightly over values appear
        if (((u.X * v.Y) - (u.Y * v.X)) < 0)
        {
            ang = -ang;
        }

        return ang;
    }
}
