// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;

namespace SixLabors.ImageSharp.Drawing;

/// <summary>
/// A pie sector shape defined by a center point, radii, rotation, and arc sweep.
/// </summary>
public sealed class Pie : Polygon
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Pie"/> class.
    /// </summary>
    /// <param name="center">The center point of the pie.</param>
    /// <param name="radius">The x and y radii of the pie ellipse.</param>
    /// <param name="rotation">The ellipse rotation in degrees.</param>
    /// <param name="startAngle">The pie start angle in degrees.</param>
    /// <param name="sweepAngle">The pie sweep angle in degrees.</param>
    public Pie(PointF center, SizeF radius, float rotation, float startAngle, float sweepAngle)
        : base(CreateSegments(center, radius, rotation, startAngle, sweepAngle))
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Pie"/> class.
    /// </summary>
    /// <param name="center">The center point of the pie.</param>
    /// <param name="radius">The x and y radii of the pie ellipse.</param>
    /// <param name="startAngle">The pie start angle in degrees.</param>
    /// <param name="sweepAngle">The pie sweep angle in degrees.</param>
    public Pie(PointF center, SizeF radius, float startAngle, float sweepAngle)
        : this(center, radius, 0F, startAngle, sweepAngle)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Pie"/> class.
    /// </summary>
    /// <param name="x">The x-coordinate of the pie center.</param>
    /// <param name="y">The y-coordinate of the pie center.</param>
    /// <param name="radiusX">The x-radius of the pie ellipse.</param>
    /// <param name="radiusY">The y-radius of the pie ellipse.</param>
    /// <param name="rotation">The ellipse rotation in degrees.</param>
    /// <param name="startAngle">The pie start angle in degrees.</param>
    /// <param name="sweepAngle">The pie sweep angle in degrees.</param>
    public Pie(float x, float y, float radiusX, float radiusY, float rotation, float startAngle, float sweepAngle)
        : this(new PointF(x, y), new SizeF(radiusX, radiusY), rotation, startAngle, sweepAngle)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Pie"/> class.
    /// </summary>
    /// <param name="x">The x-coordinate of the pie center.</param>
    /// <param name="y">The y-coordinate of the pie center.</param>
    /// <param name="radiusX">The x-radius of the pie ellipse.</param>
    /// <param name="radiusY">The y-radius of the pie ellipse.</param>
    /// <param name="startAngle">The pie start angle in degrees.</param>
    /// <param name="sweepAngle">The pie sweep angle in degrees.</param>
    public Pie(float x, float y, float radiusX, float radiusY, float startAngle, float sweepAngle)
        : this(x, y, radiusX, radiusY, 0F, startAngle, sweepAngle)
    {
    }

    private Pie(ILineSegment[] segments)
        : base(segments, true)
    {
    }

    /// <inheritdoc />
    public override IPath Transform(Matrix4x4 matrix)
    {
        if (matrix.IsIdentity)
        {
            return this;
        }

        ILineSegment[] segments = new ILineSegment[this.LineSegments.Count];

        for (int i = 0; i < segments.Length; i++)
        {
            segments[i] = this.LineSegments[i].Transform(matrix);
        }

        return new Pie(segments);
    }

    private static ILineSegment[] CreateSegments(PointF center, SizeF radius, float rotation, float startAngle, float sweepAngle)
    {
        Guard.MustBeGreaterThan(radius.Width, 0, "radiusX");
        Guard.MustBeGreaterThan(radius.Height, 0, "radiusY");

        PointF arcStart = GetArcPoint(center, radius, rotation, startAngle);
        ArcLineSegment arc = new(center, radius, rotation, startAngle, sweepAngle);

        return
        [
            new LinearLineSegment(center, arcStart),
            arc,
            new LinearLineSegment(arc.EndPoint, center)
        ];
    }

    private static PointF GetArcPoint(PointF center, SizeF radius, float rotation, float angle)
    {
        float rotationRadians = rotation * (MathF.PI / 180F);
        float angleRadians = angle * (MathF.PI / 180F);
        float cosRotation = MathF.Cos(rotationRadians);
        float sinRotation = MathF.Sin(rotationRadians);
        float cosAngle = MathF.Cos(angleRadians);
        float sinAngle = MathF.Sin(angleRadians);

        return new PointF(
            center.X + (radius.Width * cosRotation * cosAngle) - (radius.Height * sinRotation * sinAngle),
            center.Y + (radius.Width * sinRotation * cosAngle) + (radius.Height * cosRotation * sinAngle));
    }
}
