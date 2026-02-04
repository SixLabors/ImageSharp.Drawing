// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;

namespace SixLabors.ImageSharp.Drawing;

/// <summary>
/// A shape made up of a single path made up of one of more <see cref="ILineSegment"/>s
/// </summary>
public class RegularPolygon : Polygon
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RegularPolygon" /> class.
    /// </summary>
    /// <param name="location">The location the center of the polygon will be placed.</param>
    /// <param name="vertices">The number of vertices the <see cref="RegularPolygon"/> should have.</param>
    /// <param name="radius">The radius of the circle that would touch all vertices.</param>
    /// <param name="angle">The angle of rotation in Radians</param>
    public RegularPolygon(PointF location, int vertices, float radius, float angle)
        : base(CreateSegment(location, radius, vertices, angle))
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RegularPolygon" /> class.
    /// </summary>
    /// <param name="location">The location the center of the polygon will be placed.</param>
    /// <param name="vertices">The number of vertices the <see cref="RegularPolygon"/> should have.</param>
    /// <param name="radius">The radius of the circle that would touch all vertices.</param>
    public RegularPolygon(PointF location, int vertices, float radius)
        : this(location, vertices, radius, 0)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RegularPolygon" /> class.
    /// </summary>
    /// <param name="x">The x-coordinate of the center of the polygon.</param>
    /// <param name="y">The y-coordinate of the center of the polygon.</param>
    /// <param name="vertices">The number of vertices the <see cref="RegularPolygon" /> should have.</param>
    /// <param name="radius">The radius of the circle that would touch all vertices.</param>
    /// <param name="angle">The angle of rotation in Radians</param>
    public RegularPolygon(float x, float y, int vertices, float radius, float angle)
        : this(new PointF(x, y), vertices, radius, angle)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RegularPolygon" /> class.
    /// </summary>
    /// <param name="x">The x-coordinate of the center of the polygon.</param>
    /// <param name="y">The y-coordinate of the center of the polygon.</param>
    /// <param name="vertices">The number of vertices the <see cref="RegularPolygon"/> should have.</param>
    /// <param name="radius">The radius of the circle that would touch all vertices.</param>
    public RegularPolygon(float x, float y, int vertices, float radius)
        : this(new PointF(x, y), vertices, radius)
    {
    }

    private static LinearLineSegment CreateSegment(PointF location, float radius, int vertices, float angle)
    {
        Guard.MustBeGreaterThan(vertices, 2, nameof(vertices));
        Guard.MustBeGreaterThan(radius, 0, nameof(radius));

        PointF distanceVector = new(0, radius);

        float anglePerSegments = (float)(2 * Math.PI / vertices);
        float current = angle;
        PointF[] points = new PointF[vertices];
        for (int i = 0; i < vertices; i++)
        {
            PointF rotated = PointF.Transform(distanceVector, Matrix3x2.CreateRotation(current));

            points[i] = rotated + location;

            current += anglePerSegments;
        }

        return new LinearLineSegment(points);
    }
}
