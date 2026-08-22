// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using Avalonia.Media;
using Avalonia.Platform;
using SixLabors.ImageSharp.Drawing;

namespace SixLabors.ImageSharp.Drawing.Renderers.Avalonia;

/// <summary>
/// Avalonia geometry group implementation backed by combined ImageSharp paths.
/// </summary>
internal sealed class GeometryGroupImpl : GeometryImpl
{
    /// <summary>
    /// Initializes a new geometry group.
    /// </summary>
    /// <param name="fillRule">The fill rule for the group fill path.</param>
    /// <param name="children">The child geometries in the group.</param>
    public GeometryGroupImpl(FillRule fillRule, IReadOnlyList<IGeometryImpl> children)
        : this(CreatePaths(fillRule, children))
    {
    }

    private GeometryGroupImpl((IPath StrokePath, IPath? FillPath, IntersectionRule FillRule) paths)
        : base(paths.StrokePath, paths.FillPath, paths.FillRule)
    {
    }

    private static (IPath StrokePath, IPath? FillPath, IntersectionRule FillRule) CreatePaths(FillRule fillRule, IReadOnlyList<IGeometryImpl> children)
    {
        List<IPath> strokePaths = new(children.Count);
        List<IPath> fillPaths = new(children.Count);

        for (int i = 0; i < children.Count; i++)
        {
            GeometryImpl geometry = (GeometryImpl)children[i];
            strokePaths.Add(geometry.StrokePath);

            if (geometry.FillPath is not null)
            {
                fillPaths.Add(geometry.FillPath);
            }
        }

        return (
            new ComplexPolygon(strokePaths),
            fillPaths.Count == 0 ? null : new ComplexPolygon(fillPaths),
            fillRule.ToIntersectionRule());
    }
}
