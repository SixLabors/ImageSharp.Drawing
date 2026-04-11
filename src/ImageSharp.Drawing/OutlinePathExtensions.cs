// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.PolygonGeometry;
using SixLabors.ImageSharp.Drawing.Processing;

namespace SixLabors.ImageSharp.Drawing;

/// <summary>
/// Extensions to <see cref="IPath"/> that allow the generation of outlines.
/// </summary>
public static class OutlinePathExtensions
{
    private static readonly StrokeOptions DefaultOptions = new();

    /// <summary>
    /// Generates an outline of the path.
    /// </summary>
    /// <param name="path">The path to outline</param>
    /// <param name="width">The outline width.</param>
    /// <returns>A new <see cref="IPath"/> representing the outline.</returns>
    public static IPath GenerateOutline(this IPath path, float width)
        => GenerateOutline(path, width, DefaultOptions);

    /// <summary>
    /// Generates an outline of the path.
    /// </summary>
    /// <param name="path">The path to outline</param>
    /// <param name="width">The outline width.</param>
    /// <param name="strokeOptions">The stroke geometry options.</param>
    /// <returns>A new <see cref="IPath"/> representing the outline.</returns>
    public static IPath GenerateOutline(this IPath path, float width, StrokeOptions strokeOptions)
    {
        if (width <= 0)
        {
            return Path.Empty;
        }

        return StrokedShapeGenerator.GenerateStrokedShapes(path, width, strokeOptions);
    }

    /// <summary>
    /// Generates an outline of the path with alternating on and off segments based on the pattern.
    /// </summary>
    /// <param name="path">The path to outline</param>
    /// <param name="width">The outline width.</param>
    /// <param name="pattern">The pattern made of multiples of the width.</param>
    /// <returns>A new <see cref="IPath"/> representing the outline.</returns>
    public static IPath GenerateOutline(this IPath path, float width, ReadOnlySpan<float> pattern)
        => path.GenerateOutline(width, pattern, false);

    /// <summary>
    /// Generates an outline of the path with alternating on and off segments based on the pattern.
    /// </summary>
    /// <param name="path">The path to outline</param>
    /// <param name="width">The outline width.</param>
    /// <param name="pattern">The pattern made of multiples of the width.</param>
    /// <param name="strokeOptions">The stroke geometry options.</param>
    /// <returns>A new <see cref="IPath"/> representing the outline.</returns>
    public static IPath GenerateOutline(this IPath path, float width, ReadOnlySpan<float> pattern, StrokeOptions strokeOptions)
        => GenerateOutline(path, width, pattern, false, strokeOptions);

    /// <summary>
    /// Generates an outline of the path with alternating on and off segments based on the pattern.
    /// </summary>
    /// <param name="path">The path to outline</param>
    /// <param name="width">The outline width.</param>
    /// <param name="pattern">The pattern made of multiples of the width.</param>
    /// <param name="startOff">Whether the first item in the pattern is on or off.</param>
    /// <returns>A new <see cref="IPath"/> representing the outline.</returns>
    public static IPath GenerateOutline(this IPath path, float width, ReadOnlySpan<float> pattern, bool startOff)
        => GenerateOutline(path, width, pattern, startOff, DefaultOptions);

    /// <summary>
    /// Generates an outline of the path with alternating on and off segments based on the pattern.
    /// </summary>
    /// <param name="path">The path to outline</param>
    /// <param name="width">The outline width.</param>
    /// <param name="pattern">The pattern made of multiples of the width.</param>
    /// <param name="startOff">Whether the first item in the pattern is on or off.</param>
    /// <param name="strokeOptions">The stroke geometry options.</param>
    /// <returns>A new <see cref="IPath"/> representing the outline.</returns>
    public static IPath GenerateOutline(
        this IPath path,
        float width,
        ReadOnlySpan<float> pattern,
        bool startOff,
        StrokeOptions strokeOptions)
    {
        if (width <= 0)
        {
            return Path.Empty;
        }

        if (pattern.Length < 2)
        {
            return path.GenerateOutline(width, strokeOptions);
        }

        IPath dashed = path.GenerateDashes(width, pattern, startOff);

        // GenerateDashes returns the original path when the pattern is degenerate
        // or when segmentation would exceed safety limits; stroke it as solid.
        if (ReferenceEquals(dashed, path))
        {
            return path.GenerateOutline(width, strokeOptions);
        }

        if (dashed == Path.Empty)
        {
            return Path.Empty;
        }

        // Each dash segment is an open sub-path; stroke expansion and boolean merge
        // are handled by the generator.
        return StrokedShapeGenerator.GenerateStrokedShapes(dashed, width, strokeOptions);
    }
}
