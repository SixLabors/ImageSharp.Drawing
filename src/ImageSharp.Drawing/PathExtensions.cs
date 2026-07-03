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
    /// Creates a path collection rotated by the specified radians around its center.
    /// </summary>
    /// <param name="path">The path collection to rotate.</param>
    /// <param name="radians">The radians to rotate the paths.</param>
    /// <returns>An <see cref="IPathCollection"/> with a rotate transform applied.</returns>
    public static IPathCollection Rotate(this IPathCollection path, float radians)
        => path.Transform(new Matrix4x4(Matrix3x2.CreateRotation(radians, RectangleF.Center(path.Bounds))));

    /// <summary>
    /// Creates a path collection rotated by the specified degrees around its center.
    /// </summary>
    /// <param name="shape">The path collection to rotate.</param>
    /// <param name="degree">The degree to rotate the paths.</param>
    /// <returns>An <see cref="IPathCollection"/> with a rotate transform applied.</returns>
    public static IPathCollection RotateDegree(this IPathCollection shape, float degree)
        => shape.Rotate(GeometryUtilities.DegreeToRadian(degree));

    /// <summary>
    /// Creates a path collection translated by the supplied position.
    /// </summary>
    /// <param name="path">The path collection to translate.</param>
    /// <param name="position">The translation position.</param>
    /// <returns>An <see cref="IPathCollection"/> with a translate transform applied.</returns>
    public static IPathCollection Translate(this IPathCollection path, PointF position)
        => path.Transform(Matrix4x4.CreateTranslation(position.X, position.Y, 0));

    /// <summary>
    /// Creates a path collection translated by the supplied position.
    /// </summary>
    /// <param name="path">The path collection to translate.</param>
    /// <param name="x">The amount to translate along the X axis.</param>
    /// <param name="y">The amount to translate along the Y axis.</param>
    /// <returns>An <see cref="IPathCollection"/> with a translate transform applied.</returns>
    public static IPathCollection Translate(this IPathCollection path, float x, float y)
        => path.Translate(new PointF(x, y));

    /// <summary>
    /// Creates a path collection scaled by the supplied amounts around its center.
    /// </summary>
    /// <param name="path">The path collection to scale.</param>
    /// <param name="scaleX">The amount to scale along the X axis.</param>
    /// <param name="scaleY">The amount to scale along the Y axis.</param>
    /// <returns>An <see cref="IPathCollection"/> with a scale transform applied.</returns>
    public static IPathCollection Scale(this IPathCollection path, float scaleX, float scaleY)
        => path.Transform(Matrix4x4.CreateScale(scaleX, scaleY, 1, new Vector3(RectangleF.Center(path.Bounds), 0)));

    /// <summary>
    /// Creates a path collection scaled by the supplied amount around its center.
    /// </summary>
    /// <param name="path">The path collection to scale.</param>
    /// <param name="scale">The amount to scale along both the x and y axis.</param>
    /// <returns>An <see cref="IPathCollection"/> with a scale transform applied.</returns>
    public static IPathCollection Scale(this IPathCollection path, float scale)
        => path.Transform(Matrix4x4.CreateScale(scale, scale, 1, new Vector3(RectangleF.Center(path.Bounds), 0)));

    /// <summary>
    /// Creates a path rotated by the specified radians around its center.
    /// </summary>
    /// <param name="path">The path to rotate.</param>
    /// <param name="radians">The radians to rotate the path.</param>
    /// <returns>An <see cref="IPath"/> with a rotate transform applied.</returns>
    public static IPath Rotate(this IPath path, float radians)
        => path.Transform(new Matrix4x4(Matrix3x2.CreateRotation(radians, RectangleF.Center(path.Bounds))));

    /// <summary>
    /// Creates a path rotated by the specified degrees around its center.
    /// </summary>
    /// <param name="shape">The path to rotate.</param>
    /// <param name="degree">The degree to rotate the path.</param>
    /// <returns>An <see cref="IPath"/> with a rotate transform applied.</returns>
    public static IPath RotateDegree(this IPath shape, float degree)
        => shape.Rotate(GeometryUtilities.DegreeToRadian(degree));

    /// <summary>
    /// Creates a path translated by the supplied position.
    /// </summary>
    /// <param name="path">The path to translate.</param>
    /// <param name="position">The translation position.</param>
    /// <returns>An <see cref="IPath"/> with a translate transform applied.</returns>
    public static IPath Translate(this IPath path, PointF position)
        => path.Transform(Matrix4x4.CreateTranslation(position.X, position.Y, 0));

    /// <summary>
    /// Creates a path translated by the supplied position.
    /// </summary>
    /// <param name="path">The path to translate.</param>
    /// <param name="x">The amount to translate along the X axis.</param>
    /// <param name="y">The amount to translate along the Y axis.</param>
    /// <returns>An <see cref="IPath"/> with a translate transform applied.</returns>
    public static IPath Translate(this IPath path, float x, float y)
        => path.Translate(new Vector2(x, y));

    /// <summary>
    /// Creates a path scaled by the supplied amounts around its center.
    /// </summary>
    /// <param name="path">The path to scale.</param>
    /// <param name="scaleX">The amount to scale along the X axis.</param>
    /// <param name="scaleY">The amount to scale along the Y axis.</param>
    /// <returns>An <see cref="IPath"/> with a scale transform applied.</returns>
    public static IPath Scale(this IPath path, float scaleX, float scaleY)
        => path.Transform(Matrix4x4.CreateScale(scaleX, scaleY, 1, new Vector3(RectangleF.Center(path.Bounds), 0)));

    /// <summary>
    /// Creates a path scaled by the supplied amount around its center.
    /// </summary>
    /// <param name="path">The path to scale.</param>
    /// <param name="scale">The amount to scale along both the x and y axis.</param>
    /// <returns>An <see cref="IPath"/> with a scale transform applied.</returns>
    public static IPath Scale(this IPath path, float scale)
        => path.Transform(Matrix4x4.CreateScale(scale, scale, 1, new Vector3(RectangleF.Center(path.Bounds), 0)));

    /// <summary>
    /// Returns whether the supplied point is inside the path.
    /// </summary>
    /// <param name="path">The path to test.</param>
    /// <param name="point">The point to test.</param>
    /// <param name="intersectionRule">The fill rule used for containment.</param>
    /// <returns><see langword="true"/> when the point is inside or on the path boundary.</returns>
    public static bool Contains(this IPath path, PointF point, IntersectionRule intersectionRule)
        => path.Contains(point, intersectionRule, Vector2.One);

    /// <summary>
    /// Gets path information at the specified distance along the path.
    /// </summary>
    /// <param name="path">The path to measure.</param>
    /// <param name="distance">The distance along the path.</param>
    /// <param name="pathPoint">When this method returns, contains the path information at <paramref name="distance"/> if the distance resolves to a point on the path; otherwise, the default value.</param>
    /// <returns>
    /// <see langword="true"/> if <paramref name="distance"/> resolves to a point on the path;
    /// otherwise, <see langword="false"/>.
    /// </returns>
    public static bool TryGetPathPointAtDistance(this IPath path, float distance, out PathPoint pathPoint)
        => path.TryGetPathPointAtDistance(distance, Vector2.One, out pathPoint);

    /// <summary>
    /// Gets path information at the specified distance along the path, extrapolating along the
    /// boundary tangents when the distance falls before the start or beyond the end of an open path.
    /// </summary>
    /// <remarks>
    /// Unlike <see cref="TryGetPathPointAtDistance(IPath, float, out PathPoint)"/>, out-of-range
    /// distances resolve to a point on the virtual straight-line continuation of the path's first
    /// or last segment. Text layout uses this to place aligned glyphs that overflow their layout
    /// path. The method returns <see langword="false"/> only when the distance is not a finite
    /// number or the path has no measurable length.
    /// </remarks>
    /// <param name="path">The path to measure.</param>
    /// <param name="distance">The distance along the path.</param>
    /// <param name="pathPoint">When this method returns, contains the path information at <paramref name="distance"/> if the path is measurable; otherwise, the default value.</param>
    /// <returns><see langword="true"/> if the point could be resolved; otherwise, <see langword="false"/>.</returns>
    public static bool TryGetPathPointAtDistanceUnbounded(this IPath path, float distance, out PathPoint pathPoint)
        => path.TryGetPathPointAtDistanceUnbounded(distance, Vector2.One, out pathPoint);

    /// <summary>
    /// Creates a path segment between two distances along the path.
    /// </summary>
    /// <param name="path">The path to measure.</param>
    /// <param name="startDistance">The segment start distance.</param>
    /// <param name="stopDistance">The segment stop distance.</param>
    /// <param name="startOnBeginFigure">Whether the returned segment starts a new figure at the first segment point.</param>
    /// <param name="segment">When this method returns, contains the segment path if one was created; otherwise, an empty path.</param>
    /// <returns><see langword="true"/> when a segment path was created.</returns>
    public static bool TryGetSegment(this IPath path, float startDistance, float stopDistance, bool startOnBeginFigure, out IPath segment)
        => path.TryGetSegment(startDistance, stopDistance, startOnBeginFigure, Vector2.One, out segment);

    /// <summary>
    /// Calculates the path length after flattening curves at local-space precision.
    /// </summary>
    /// <param name="path">The path to compute the length for.</param>
    /// <returns>The path length.</returns>
    public static float ComputeLength(this IPath path)
        => path.ComputeLength(Vector2.One);

    /// <summary>
    /// Calculates the total area of all paths in the specified collection.
    /// </summary>
    /// <param name="paths">A collection of paths for which to compute the combined area. Cannot be null.</param>
    /// <returns>The total area, in square units, represented by all paths in the collection.</returns>
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
    /// Calculates the total area represented by the specified path.
    /// </summary>
    /// <param name="path">The path for which to compute the area.</param>
    /// <returns>The total path area, in square units.</returns>
    public static float ComputeArea(this IPath path)
        => path.ComputeArea(Vector2.One);
}
