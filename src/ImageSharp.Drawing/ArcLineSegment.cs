// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using System.Runtime.CompilerServices;

namespace SixLabors.ImageSharp.Drawing;

/// <summary>
/// Represents a line segment that contains radii and angles that will be rendered as an elliptical arc.
/// </summary>
public class ArcLineSegment : ILineSegment
{
    /// <summary>
    /// The tolerance below which radii and squared distances are treated as zero.
    /// </summary>
    private const float ZeroTolerance = 1e-05F;

    /// <summary>
    /// The retained linearized arc points, baked in local space at construction.
    /// </summary>
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
        bool ellipse = largeArc && ((Vector2)to - (Vector2)from).LengthSquared() < ZeroTolerance && radius.Width > 0 && radius.Height > 0;
        if (ellipse)
        {
            // The circle always has a start angle of 0 which is positioned at 3 o'clock.
            // This means the centre point is to the left of the start position.
            Vector2 center = (Vector2)from - new Vector2(radius.Width, 0);
            this.linePoints = EllipticArcToBezierCurve(from, center, radius, rotation, 0, sweep ? 2 * MathF.PI : -2 * MathF.PI);
        }
        else
        {
            this.linePoints = EllipticArcFromEndParams(from, to, radius, rotation, largeArc, sweep);
        }

        this.Bounds = CalculateBounds(this.linePoints);
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
        bool ellipse = largeArc && (to - from).LengthSquared() < ZeroTolerance && radius.Width > 0 && radius.Height > 0;

        if (ellipse)
        {
            this.linePoints = EllipticArcToBezierCurve(from, center, radius, rotation, startAngle, sweepAngle);
        }
        else
        {
            this.linePoints = EllipticArcFromEndParams(from, to, radius, rotation, largeArc, sweep);
        }

        this.Bounds = CalculateBounds(this.linePoints);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ArcLineSegment"/> class.
    /// Used to wrap pre-linearized points produced by <see cref="Transform(Matrix4x4)"/>.
    /// </summary>
    /// <param name="linePoints">The retained linearized arc points.</param>
    private ArcLineSegment(PointF[] linePoints)
    {
        this.linePoints = linePoints;
        this.Bounds = CalculateBounds(linePoints);
    }

    /// <inheritdoc/>
    public PointF StartPoint => this.linePoints[0];

    /// <inheritdoc/>
    public PointF EndPoint => this.linePoints[^1];

    /// <inheritdoc />
    public RectangleF Bounds { get; }

    /// <inheritdoc />
    public int LinearVertexCount(Vector2 scale) => this.linePoints.Length;

    /// <inheritdoc />
    public void CopyTo(Span<PointF> destination, bool skipFirstPoint, Vector2 scale)
    {
        int startIndex = skipFirstPoint ? 1 : 0;
        ReadOnlySpan<PointF> source = this.linePoints.AsSpan(startIndex);

        if (scale == Vector2.One)
        {
            source.CopyTo(destination);
            return;
        }

        for (int i = 0; i < source.Length; i++)
        {
            destination[i] = new PointF(source[i].X * scale.X, source[i].Y * scale.Y);
        }
    }

    /// <summary>
    /// Transforms the current <see cref="ArcLineSegment"/> using specified matrix.
    /// </summary>
    /// <param name="matrix">The transformation matrix.</param>
    /// <returns>An <see cref="ArcLineSegment"/> with the matrix applied to it.</returns>
    public ILineSegment Transform(Matrix4x4 matrix)
    {
        if (matrix.IsIdentity)
        {
            return this;
        }

        PointF[] transformedPoints = new PointF[this.linePoints.Length];
        for (int i = 0; i < this.linePoints.Length; i++)
        {
            transformedPoints[i] = PointF.Transform(this.linePoints[i], matrix);
        }

        return new ArcLineSegment(transformedPoints);
    }

    /// <inheritdoc/>
    ILineSegment ILineSegment.Transform(Matrix4x4 matrix) => this.Transform(matrix);

    /// <summary>
    /// Computes the bounds for the retained linearized arc points.
    /// </summary>
    /// <param name="points">The linearized arc points.</param>
    /// <returns>The axis-aligned bounds enclosing the points.</returns>
    private static RectangleF CalculateBounds(ReadOnlySpan<PointF> points)
    {
        float minX = float.MaxValue;
        float minY = float.MaxValue;
        float maxX = float.MinValue;
        float maxY = float.MinValue;

        for (int i = 0; i < points.Length; i++)
        {
            PointF point = points[i];
            minX = MathF.Min(minX, point.X);
            minY = MathF.Min(minY, point.Y);
            maxX = MathF.Max(maxX, point.X);
            maxY = MathF.Max(maxY, point.Y);
        }

        return RectangleF.FromLTRB(minX, minY, maxX, maxY);
    }

    /// <summary>
    /// Linearizes an elliptical arc described in SVG endpoint parameterization. Degenerate arcs
    /// (coincident endpoints or zero radii) collapse to a straight line between the endpoints.
    /// </summary>
    /// <param name="from">The arc start point.</param>
    /// <param name="to">The arc end point.</param>
    /// <param name="radius">The ellipse radii.</param>
    /// <param name="rotation">The ellipse x-axis rotation, in radians.</param>
    /// <param name="largeArc">Whether the arc spans more than 180 degrees.</param>
    /// <param name="sweep">Whether the arc sweeps through increasing angles.</param>
    /// <returns>The linearized arc points.</returns>
    private static PointF[] EllipticArcFromEndParams(
        PointF from,
        PointF to,
        SizeF radius,
        float rotation,
        bool largeArc,
        bool sweep)
    {
        Vector2 absRadius = Vector2.Abs(radius);

        if (EllipticArcOutOfRange(from, to, radius))
        {
            return [from, to];
        }

        EndpointToCenterArcParams(from, to, ref absRadius, rotation, largeArc, sweep, out Vector2 center, out Vector2 angles);
        return EllipticArcToBezierCurve(from, center, absRadius, rotation, angles.X, angles.Y);
    }

    /// <summary>
    /// Detects the SVG F.6.2 out-of-range cases where the endpoint arc parameters cannot
    /// describe an arc and the segment degenerates to a straight line.
    /// </summary>
    /// <param name="from">The arc start point.</param>
    /// <param name="to">The arc end point.</param>
    /// <param name="radius">The ellipse radii.</param>
    /// <returns><see langword="true"/> when the parameters are out of range; otherwise, <see langword="false"/>.</returns>
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

    /// <summary>
    /// Computes the derivative of the rotated ellipse parameterization at angle <paramref name="t"/>.
    /// </summary>
    /// <param name="r">The ellipse radii.</param>
    /// <param name="xAngle">The ellipse x-axis rotation, in radians.</param>
    /// <param name="t">The parametric angle, in radians.</param>
    /// <returns>The tangent vector at the given angle.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector2 EllipticArcDerivative(Vector2 r, float xAngle, float t)
        => new(
            (-r.X * MathF.Cos(xAngle) * MathF.Sin(t)) - (r.Y * MathF.Sin(xAngle) * MathF.Cos(t)),
            (-r.X * MathF.Sin(xAngle) * MathF.Sin(t)) + (r.Y * MathF.Cos(xAngle) * MathF.Cos(t)));

    /// <summary>
    /// Evaluates the rotated ellipse parameterization at angle <paramref name="t"/>.
    /// </summary>
    /// <param name="c">The ellipse center.</param>
    /// <param name="r">The ellipse radii.</param>
    /// <param name="xAngle">The ellipse x-axis rotation, in radians.</param>
    /// <param name="t">The parametric angle, in radians.</param>
    /// <returns>The point on the ellipse at the given angle.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector2 EllipticArcPoint(Vector2 c, Vector2 r, float xAngle, float t)
        => new(
            c.X + (r.X * MathF.Cos(xAngle) * MathF.Cos(t)) - (r.Y * MathF.Sin(xAngle) * MathF.Sin(t)),
            c.Y + (r.X * MathF.Sin(xAngle) * MathF.Cos(t)) + (r.Y * MathF.Cos(xAngle) * MathF.Sin(t)));

    /// <summary>
    /// Approximates the elliptical arc with cubic bezier spans of at most 45 degrees each and
    /// flattens those spans into a single contiguous point run.
    /// </summary>
    /// <param name="from">The arc start point.</param>
    /// <param name="center">The ellipse center.</param>
    /// <param name="radius">The ellipse radii.</param>
    /// <param name="xAngle">The ellipse x-axis rotation, in radians.</param>
    /// <param name="startAngle">The arc start angle, in radians.</param>
    /// <param name="sweepAngle">The signed arc sweep, in radians.</param>
    /// <returns>The linearized arc points.</returns>
    private static PointF[] EllipticArcToBezierCurve(Vector2 from, Vector2 center, Vector2 radius, float xAngle, float startAngle, float sweepAngle)
    {
        float s = startAngle;
        float e = s + sweepAngle;
        bool neg = e < s;
        float sign = neg ? -1 : 1;
        float remain = Math.Abs(e - s);
        int curveCount = Math.Max((int)MathF.Ceiling(remain / (MathF.PI / 4F)), 1);

        // Arc flattening retains the final point array, so use the builder to avoid the
        // intermediate collection and copy a list would generate.
        FlattenedPointBuilder points = new(curveCount * 4);

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

            CubicBezierLineSegment bezier = new(from, q1, q2, p2);
            int bezierCount = bezier.LinearVertexCount(Vector2.One);
            Span<PointF> destination = points.GetAppendSpan(bezierCount);
            bezier.CopyTo(destination, skipFirstPoint: false, Vector2.One);
            points.Advance(bezierCount);

            from = p2;

            s += signStep;
            remain -= step;
            prev = p2;
        }

        return points.Detach();
    }

    /// <summary>
    /// Converts SVG endpoint arc parameterization to center parameterization following
    /// SVG spec section F.6.5, scaling up too-small radii as required by F.6.6.
    /// </summary>
    /// <param name="p1">The arc start point.</param>
    /// <param name="p2">The arc end point.</param>
    /// <param name="r">The ellipse radii; scaled up on return when too small to span both endpoints.</param>
    /// <param name="xRotation">The ellipse x-axis rotation, in radians.</param>
    /// <param name="flagA">The large arc flag.</param>
    /// <param name="flagS">The sweep flag.</param>
    /// <param name="center">When this method returns, contains the ellipse center.</param>
    /// <param name="angles">When this method returns, contains the start angle (X) and sweep delta (Y), in radians.</param>
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

    /// <summary>
    /// Clamps <paramref name="val"/> to the inclusive range [<paramref name="min"/>, <paramref name="max"/>].
    /// </summary>
    /// <param name="val">The value to clamp.</param>
    /// <param name="min">The minimum allowed value.</param>
    /// <param name="max">The maximum allowed value.</param>
    /// <returns>The clamped value.</returns>
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

    /// <summary>
    /// Computes the signed angle between two vectors as defined by SVG spec equation F.6.5.4.
    /// </summary>
    /// <param name="ux">The x-component of the first vector.</param>
    /// <param name="uy">The y-component of the first vector.</param>
    /// <param name="vx">The x-component of the second vector.</param>
    /// <param name="vy">The y-component of the second vector.</param>
    /// <returns>The signed angle, in radians.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float SvgAngle(double ux, double uy, double vx, double vy)
    {
        Vector2 u = new((float)ux, (float)uy);
        Vector2 v = new((float)vx, (float)vy);

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
