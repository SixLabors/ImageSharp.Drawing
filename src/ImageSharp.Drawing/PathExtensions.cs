// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;

namespace SixLabors.ImageSharp.Drawing;

/// <summary>
/// Convenience methods that can be applied to shapes and paths.
/// </summary>
public static partial class PathExtensions
{
    /// <summary>
    /// Creates a path rotated by the specified radians around its center.
    /// </summary>
    /// <param name="path">The path to rotate.</param>
    /// <param name="radians">The radians to rotate the path.</param>
    /// <returns>A <see cref="IPath"/> with a rotate transform applied.</returns>
    public static IPathCollection Rotate(this IPathCollection path, float radians)
        => path.Transform(Matrix3x2Extensions.CreateRotation(radians, RectangleF.Center(path.Bounds)));

    /// <summary>
    /// Creates a path rotated by the specified degrees around its center.
    /// </summary>
    /// <param name="shape">The path to rotate.</param>
    /// <param name="degree">The degree to rotate the path.</param>
    /// <returns>A <see cref="IPath"/> with a rotate transform applied.</returns>
    public static IPathCollection RotateDegree(this IPathCollection shape, float degree)
        => shape.Rotate(GeometryUtilities.DegreeToRadian(degree));

    /// <summary>
    /// Creates a path translated by the supplied position
    /// </summary>
    /// <param name="path">The path to translate.</param>
    /// <param name="position">The translation position.</param>
    /// <returns>A <see cref="IPath"/> with a translate transform applied.</returns>
    public static IPathCollection Translate(this IPathCollection path, PointF position)
        => path.Transform(Matrix3x2.CreateTranslation(position));

    /// <summary>
    /// Creates a path translated by the supplied position
    /// </summary>
    /// <param name="path">The path to translate.</param>
    /// <param name="x">The amount to translate along the X axis.</param>
    /// <param name="y">The amount to translate along the Y axis.</param>
    /// <returns>A <see cref="IPath"/> with a translate transform applied.</returns>
    public static IPathCollection Translate(this IPathCollection path, float x, float y)
        => path.Translate(new PointF(x, y));

    /// <summary>
    /// Creates a path translated by the supplied position
    /// </summary>
    /// <param name="path">The path to translate.</param>
    /// <param name="scaleX">The amount to scale along the X axis.</param>
    /// <param name="scaleY">The amount to scale along the Y axis.</param>
    /// <returns>A <see cref="IPath"/> with a translate transform applied.</returns>
    public static IPathCollection Scale(this IPathCollection path, float scaleX, float scaleY)
        => path.Transform(Matrix3x2.CreateScale(scaleX, scaleY, RectangleF.Center(path.Bounds)));

    /// <summary>
    /// Creates a path translated by the supplied position
    /// </summary>
    /// <param name="path">The path to translate.</param>
    /// <param name="scale">The amount to scale along both the x and y axis.</param>
    /// <returns>A <see cref="IPath"/> with a translate transform applied.</returns>
    public static IPathCollection Scale(this IPathCollection path, float scale)
        => path.Transform(Matrix3x2.CreateScale(scale, RectangleF.Center(path.Bounds)));

    /// <summary>
    /// Creates a path rotated by the specified radians around its center.
    /// </summary>
    /// <param name="path">The path to rotate.</param>
    /// <param name="radians">The radians to rotate the path.</param>
    /// <returns>A <see cref="IPath"/> with a rotate transform applied.</returns>
    public static IPath Rotate(this IPath path, float radians)
        => path.Transform(Matrix3x2.CreateRotation(radians, RectangleF.Center(path.Bounds)));

    /// <summary>
    /// Creates a path rotated by the specified degrees around its center.
    /// </summary>
    /// <param name="shape">The path to rotate.</param>
    /// <param name="degree">The degree to rotate the path.</param>
    /// <returns>A <see cref="IPath"/> with a rotate transform applied.</returns>
    public static IPath RotateDegree(this IPath shape, float degree)
        => shape.Rotate(GeometryUtilities.DegreeToRadian(degree));

    /// <summary>
    /// Creates a path translated by the supplied position
    /// </summary>
    /// <param name="path">The path to translate.</param>
    /// <param name="position">The translation position.</param>
    /// <returns>A <see cref="IPath"/> with a translate transform applied.</returns>
    public static IPath Translate(this IPath path, PointF position)
        => path.Transform(Matrix3x2.CreateTranslation(position));

    /// <summary>
    /// Creates a path translated by the supplied position
    /// </summary>
    /// <param name="path">The path to translate.</param>
    /// <param name="x">The amount to translate along the X axis.</param>
    /// <param name="y">The amount to translate along the Y axis.</param>
    /// <returns>A <see cref="IPath"/> with a translate transform applied.</returns>
    public static IPath Translate(this IPath path, float x, float y)
        => path.Translate(new Vector2(x, y));

    /// <summary>
    /// Creates a path translated by the supplied position
    /// </summary>
    /// <param name="path">The path to translate.</param>
    /// <param name="scaleX">The amount to scale along the X axis.</param>
    /// <param name="scaleY">The amount to scale along the Y axis.</param>
    /// <returns>A <see cref="IPath"/> with a translate transform applied.</returns>
    public static IPath Scale(this IPath path, float scaleX, float scaleY)
        => path.Transform(Matrix3x2.CreateScale(scaleX, scaleY, RectangleF.Center(path.Bounds)));

    /// <summary>
    /// Creates a path translated by the supplied position
    /// </summary>
    /// <param name="path">The path to translate.</param>
    /// <param name="scale">The amount to scale along both the x and y axis.</param>
    /// <returns>A <see cref="IPath"/> with a translate transform applied.</returns>
    public static IPath Scale(this IPath path, float scale)
        => path.Transform(Matrix3x2.CreateScale(scale, RectangleF.Center(path.Bounds)));

    /// <summary>
    /// Calculates the approximate length of the path as though each segment were unrolled into a line.
    /// </summary>
    /// <param name="path">The path to compute the length for.</param>
    /// <returns>
    /// The <see cref="float"/> representing the unrolled length.
    /// For closed paths, the length includes an implicit closing segment.
    /// </returns>
    public static float ComputeLength(this IPath path)
    {
        float dist = 0;
        foreach (ISimplePath s in path.Flatten())
        {
            ReadOnlySpan<PointF> points = s.Points.Span;
            if (points.Length < 2)
            {
                // Only a single point
                continue;
            }

            for (int i = 1; i < points.Length; i++)
            {
                dist += Vector2.Distance(points[i - 1], points[i]);
            }

            if (s.IsClosed)
            {
                dist += Vector2.Distance(points[0], points[^1]);
            }
        }

        return dist;
    }

    /// <summary>
    /// Calculates the total area of all paths in the specified collection.
    /// </summary>
    /// <param name="paths">A collection of paths for which to compute the combined area. Cannot be null.</param>
    /// <returns>
    /// The total area, in square units, enclosed by all paths in the collection.
    /// </returns>
    public static float ComputeArea(this IPathCollection paths)
    {
        float area = 0;
        foreach (IPath path in paths)
        {
            area += path.ComputeArea();
        }

        return area;
    }

    /// <summary>
    /// Calculates the total area enclosed by the specified path.
    /// </summary>
    /// <remarks>
    /// This method sums the areas of all subpaths within the path. Subpaths with fewer than three
    /// points are ignored, as they do not form a closed region. The result is always non-negative, regardless of the
    /// winding direction of the subpaths.
    /// </remarks>
    /// <param name="path">
    /// The path for which to compute the enclosed area. Must contain at least one subpath with three or more points to
    /// contribute to the area calculation.
    /// </param>
    /// <returns>
    /// The total area, in square units, enclosed by all subpaths of the path. Returns 0 if the path does not contain
    /// any subpaths with at least three points.
    /// </returns>
    public static float ComputeArea(this IPath path)
    {
        float area = 0;
        foreach (ISimplePath s in path.Flatten())
        {
            ReadOnlySpan<PointF> points = s.Points.Span;
            if (points.Length < 3)
            {
                // Not enough points to form an area
                continue;
            }

            float subArea = 0;
            for (int i = 0; i < points.Length; i++)
            {
                PointF p1 = points[i];
                PointF p2 = points[(i + 1) % points.Length];
                subArea += (p1.X * p2.Y) - (p2.X * p1.Y);
            }

            area += MathF.Abs(subArea) * .5F;
        }

        return area;
    }
}
