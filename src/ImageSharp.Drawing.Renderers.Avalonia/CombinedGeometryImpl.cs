// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using Avalonia.Media;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;

namespace SixLabors.ImageSharp.Drawing.Renderers.Avalonia;

/// <summary>
/// Avalonia combined geometry implementation backed by ImageSharp path clipping.
/// </summary>
internal sealed class CombinedGeometryImpl : GeometryImpl
{
    /// <summary>
    /// Creates a combined geometry from two ImageSharp-backed geometries.
    /// </summary>
    /// <param name="combineMode">The geometry combine mode.</param>
    /// <param name="first">The first geometry.</param>
    /// <param name="second">The second geometry.</param>
    /// <returns>The combined geometry.</returns>
    public static CombinedGeometryImpl Create(GeometryCombineMode combineMode, GeometryImpl first, GeometryImpl second)
        => new(CreatePaths(combineMode, first, second));

    private CombinedGeometryImpl((IPath StrokePath, IPath? FillPath, IntersectionRule FillRule) paths)
        : base(paths.StrokePath, paths.FillPath, paths.FillRule)
    {
    }

    private static (IPath StrokePath, IPath? FillPath, IntersectionRule FillRule) CreatePaths(
        GeometryCombineMode combineMode,
        GeometryImpl first,
        GeometryImpl second)
    {
        BooleanOperation operation = combineMode.ToBooleanOperation();

        IPath strokePath = first.StrokePath.Clip(operation, first.FillRule, second.StrokePath);
        IPath? fillPath = null;

        if (first.FillPath is not null && second.FillPath is not null)
        {
            fillPath = ReferenceEquals(first.FillPath, first.StrokePath) && ReferenceEquals(second.FillPath, second.StrokePath)
                ? strokePath
                : first.FillPath.Clip(operation, first.FillRule, second.FillPath);
        }

        return (strokePath, fillPath, first.FillRule);
    }
}
