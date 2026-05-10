// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.PolygonGeometry;
using SixLabors.ImageSharp.Drawing.Processing;

namespace SixLabors.ImageSharp.Drawing;

/// <summary>
/// Provides extension methods to <see cref="IPath"/> that allow the clipping of shapes.
/// </summary>
public static class ClipPathExtensions
{
    private static readonly ShapeOptions DefaultOptions = new();

    /// <summary>
    /// Clips the specified subject path with the provided clipping paths.
    /// </summary>
    /// <param name="subjectPath">The subject path.</param>
    /// <param name="clipPaths">The clipping paths.</param>
    /// <returns>The clipped <see cref="IPath"/>.</returns>
    public static IPath Clip(this IPath subjectPath, params IPath[] clipPaths)
        => subjectPath.Clip(DefaultOptions, clipPaths);

    /// <summary>
    /// Clips the specified subject path with the provided clipping paths.
    /// </summary>
    /// <param name="subjectPath">The subject path.</param>
    /// <param name="options">The shape options.</param>
    /// <param name="clipPaths">The clipping paths.</param>
    /// <returns>The clipped <see cref="IPath"/>.</returns>
    public static IPath Clip(
        this IPath subjectPath,
        ShapeOptions options,
        params IPath[] clipPaths)
        => ClippedShapeGenerator.GenerateClippedShapes(options.BooleanOperation, subjectPath, clipPaths);

    /// <summary>
    /// Clips the specified subject path with the provided clipping paths.
    /// </summary>
    /// <param name="subjectPath">The subject path.</param>
    /// <param name="clipPaths">The clipping paths.</param>
    /// <returns>The clipped <see cref="IPath"/>.</returns>
    public static IPath Clip(this IPath subjectPath, IEnumerable<IPath> clipPaths)
        => subjectPath.Clip(DefaultOptions, clipPaths);

    /// <summary>
    /// Clips the specified subject path with the provided clipping paths.
    /// </summary>
    /// <param name="subjectPath">The subject path.</param>
    /// <param name="options">The shape options.</param>
    /// <param name="clipPaths">The clipping paths.</param>
    /// <returns>The clipped <see cref="IPath"/>.</returns>
    public static IPath Clip(
        this IPath subjectPath,
        ShapeOptions options,
        IEnumerable<IPath> clipPaths)
        => ClippedShapeGenerator.GenerateClippedShapes(options.BooleanOperation, subjectPath, clipPaths);
}
