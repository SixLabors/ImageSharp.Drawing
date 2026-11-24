// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.PolygonGeometry;

namespace SixLabors.ImageSharp.Drawing;

/// <summary>
/// Provides extension methods to <see cref="IPath"/> that allow the clipping of shapes.
/// </summary>
/// <remarks>
/// Closed figures define filled areas. Open figures retain their line segments and points.
/// Results can contain closed figures, open lines, and isolated points.
/// </remarks>
public static class ClipPathExtensions
{
    /// <summary>
    /// Clips the specified subject path with the provided clipping paths using
    /// <see cref="BooleanOperation.Intersection"/> and <see cref="IntersectionRule.NonZero"/>.
    /// </summary>
    /// <param name="subjectPath">The subject path.</param>
    /// <param name="clipPaths">The clipping paths.</param>
    /// <returns>The clipped <see cref="IPath"/>.</returns>
    public static IPath Clip(this IPath subjectPath, params IPath[] clipPaths)
        => subjectPath.Clip(BooleanOperation.Intersection, IntersectionRule.NonZero, clipPaths);

    /// <summary>
    /// Clips the specified subject path with the provided clipping paths using
    /// <see cref="BooleanOperation.Intersection"/> and <see cref="IntersectionRule.NonZero"/>.
    /// </summary>
    /// <param name="subjectPath">The subject path.</param>
    /// <param name="clipPaths">The clipping paths.</param>
    /// <returns>The clipped <see cref="IPath"/>.</returns>
    public static IPath Clip(this IPath subjectPath, IEnumerable<IPath> clipPaths)
        => subjectPath.Clip(BooleanOperation.Intersection, IntersectionRule.NonZero, clipPaths);

    /// <summary>
    /// Clips the specified subject path with the provided clipping paths using
    /// <see cref="IntersectionRule.NonZero"/>.
    /// </summary>
    /// <param name="subjectPath">The subject path.</param>
    /// <param name="operation">The boolean operation to perform.</param>
    /// <param name="clipPaths">The clipping paths.</param>
    /// <returns>The clipped <see cref="IPath"/>.</returns>
    public static IPath Clip(this IPath subjectPath, BooleanOperation operation, params IPath[] clipPaths)
        => subjectPath.Clip(operation, IntersectionRule.NonZero, clipPaths);

    /// <summary>
    /// Clips the specified subject path with the provided clipping paths using
    /// <see cref="IntersectionRule.NonZero"/>.
    /// </summary>
    /// <param name="subjectPath">The subject path.</param>
    /// <param name="operation">The boolean operation to perform.</param>
    /// <param name="clipPaths">The clipping paths.</param>
    /// <returns>The clipped <see cref="IPath"/>.</returns>
    public static IPath Clip(this IPath subjectPath, BooleanOperation operation, IEnumerable<IPath> clipPaths)
        => subjectPath.Clip(operation, IntersectionRule.NonZero, clipPaths);

    /// <summary>
    /// Clips the specified subject path with the provided clipping paths.
    /// </summary>
    /// <param name="subjectPath">The subject path.</param>
    /// <param name="operation">The boolean operation to perform.</param>
    /// <param name="intersectionRule">The fill rule for closed figures in both operands.</param>
    /// <param name="clipPaths">The clipping paths.</param>
    /// <returns>The clipped <see cref="IPath"/>.</returns>
    public static IPath Clip(
        this IPath subjectPath,
        BooleanOperation operation,
        IntersectionRule intersectionRule,
        params IPath[] clipPaths)
        => ClippedShapeGenerator.GenerateClippedShapes(operation, intersectionRule, subjectPath, clipPaths);

    /// <summary>
    /// Clips the specified subject path with the provided clipping paths.
    /// </summary>
    /// <param name="subjectPath">The subject path.</param>
    /// <param name="operation">The boolean operation to perform.</param>
    /// <param name="intersectionRule">The fill rule for closed figures in both operands.</param>
    /// <param name="clipPaths">The clipping paths.</param>
    /// <returns>The clipped <see cref="IPath"/>.</returns>
    public static IPath Clip(
        this IPath subjectPath,
        BooleanOperation operation,
        IntersectionRule intersectionRule,
        IEnumerable<IPath> clipPaths)
        => ClippedShapeGenerator.GenerateClippedShapes(operation, intersectionRule, subjectPath, clipPaths);
}
