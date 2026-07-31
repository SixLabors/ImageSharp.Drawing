// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using Avalonia;
using SixLabors.ImageSharp.Drawing;

namespace AvaloniaControlCatalog;

/// <summary>
/// Avalonia ellipse geometry implementation backed by an ImageSharp ellipse path.
/// </summary>
internal sealed class EllipseGeometryImpl : GeometryImpl
{
    /// <summary>
    /// Initializes a new ellipse geometry.
    /// </summary>
    /// <param name="rect">The ellipse bounds.</param>
    public EllipseGeometryImpl(Rect rect)
        : this(CreatePath(rect))
    {
    }

    private EllipseGeometryImpl(IPath path)
        : base(path, path, IntersectionRule.EvenOdd)
    {
    }

    private static IPath CreatePath(Rect rect)
        => rect.Width <= 0 || rect.Height <= 0
            ? EmptyPath.ClosedPath
            : new EllipsePolygon(rect.Center.ToPointF(), rect.Size.ToSizeF());
}
