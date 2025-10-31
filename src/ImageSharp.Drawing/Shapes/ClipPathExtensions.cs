// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Shapes.PolygonClipper;

namespace SixLabors.ImageSharp.Drawing;

/// <summary>
/// Provides extension methods to <see cref="IPath"/> that allow the clipping of shapes.
/// </summary>
public static class ClipPathExtensions
{
    /// <summary>
    /// Clips the specified subject path with the provided clipping paths.
    /// </summary>
    /// <param name="subjectPath">The subject path.</param>
    /// <param name="clipPaths">The clipping paths.</param>
    /// <returns>The clipped <see cref="IPath"/>.</returns>
    public static IPath Clip(this IPath subjectPath, params IPath[] clipPaths)
        => subjectPath.Clip((IEnumerable<IPath>)clipPaths);

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
        => subjectPath.Clip(options, (IEnumerable<IPath>)clipPaths);

    /// <summary>
    /// Clips the specified subject path with the provided clipping paths.
    /// </summary>
    /// <param name="subjectPath">The subject path.</param>
    /// <param name="clipPaths">The clipping paths.</param>
    /// <returns>The clipped <see cref="IPath"/>.</returns>
    public static IPath Clip(this IPath subjectPath, IEnumerable<IPath> clipPaths)
        => subjectPath.Clip(new ShapeOptions(), clipPaths);

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
    {
        ClippedShapeGenerator clipper = new(options.IntersectionRule);

        clipper.AddPath(subjectPath, ClippingType.Subject);
        clipper.AddPaths(clipPaths, ClippingType.Clip);

        IPath[] result = clipper.GenerateClippedShapes(options.ClippingOperation);

        return new ComplexPolygon(result);
    }
}
