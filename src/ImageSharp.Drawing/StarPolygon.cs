// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;

namespace SixLabors.ImageSharp.Drawing;

/// <summary>
/// A star-shaped polygon defined by alternating inner and outer radii.
/// </summary>
public sealed class StarPolygon : Polygon
{
    /// <summary>
    /// Initializes a new instance of the <see cref="StarPolygon" /> class.
    /// </summary>
    /// <param name="location">The center point of the star.</param>
    /// <param name="prongs">The number of star prongs.</param>
    /// <param name="innerRadii">The inner star radius.</param>
    /// <param name="outerRadii">The outer star radius.</param>
    /// <param name="angle">The angle of rotation in degrees.</param>
    public StarPolygon(PointF location, int prongs, float innerRadii, float outerRadii, float angle)
        : base(CreateSegment(location, innerRadii, outerRadii, prongs, angle))
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="StarPolygon" /> class.
    /// </summary>
    /// <param name="location">The center point of the star.</param>
    /// <param name="prongs">The number of star prongs.</param>
    /// <param name="innerRadii">The inner star radius.</param>
    /// <param name="outerRadii">The outer star radius.</param>
    public StarPolygon(PointF location, int prongs, float innerRadii, float outerRadii)
        : this(location, prongs, innerRadii, outerRadii, 0)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="StarPolygon" /> class.
    /// </summary>
    /// <param name="x">The x-coordinate of the star center.</param>
    /// <param name="y">The y-coordinate of the star center.</param>
    /// <param name="prongs">The number of star prongs.</param>
    /// <param name="innerRadii">The inner star radius.</param>
    /// <param name="outerRadii">The outer star radius.</param>
    /// <param name="angle">The angle of rotation in degrees.</param>
    public StarPolygon(float x, float y, int prongs, float innerRadii, float outerRadii, float angle)
        : this(new PointF(x, y), prongs, innerRadii, outerRadii, angle)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="StarPolygon" /> class.
    /// </summary>
    /// <param name="x">The x-coordinate of the star center.</param>
    /// <param name="y">The y-coordinate of the star center.</param>
    /// <param name="prongs">The number of star prongs.</param>
    /// <param name="innerRadii">The inner star radius.</param>
    /// <param name="outerRadii">The outer star radius.</param>
    public StarPolygon(float x, float y, int prongs, float innerRadii, float outerRadii)
        : this(new PointF(x, y), prongs, innerRadii, outerRadii)
    {
    }

    private static LinearLineSegment CreateSegment(Vector2 location, float innerRadii, float outerRadii, int prongs, float angle)
    {
        Guard.MustBeGreaterThan(prongs, 2, nameof(prongs));
        Guard.MustBeGreaterThan(innerRadii, 0, nameof(innerRadii));
        Guard.MustBeGreaterThan(outerRadii, 0, nameof(outerRadii));

        Vector2 distanceVectorInner = new(0, innerRadii);
        Vector2 distanceVectorOuter = new(0, outerRadii);

        int vertices = prongs * 2;
        float anglePerSegments = (float)(2 * Math.PI / vertices);
        float current = GeometryUtilities.DegreeToRadian(angle);
        PointF[] points = new PointF[vertices];
        Vector2 distance = distanceVectorInner;
        for (int i = 0; i < vertices; i++)
        {
            if (distance == distanceVectorInner)
            {
                distance = distanceVectorOuter;
            }
            else
            {
                distance = distanceVectorInner;
            }

            Vector2 rotated = PointF.Transform(distance, Matrix4x4.CreateRotationZ(current));

            points[i] = rotated + location;

            current += anglePerSegments;
        }

        return new LinearLineSegment(points);
    }
}
