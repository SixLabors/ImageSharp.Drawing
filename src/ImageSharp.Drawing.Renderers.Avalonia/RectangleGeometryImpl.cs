// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using Avalonia;
using SixLabors.ImageSharp.Drawing;

namespace SixLabors.ImageSharp.Drawing.Renderers.Avalonia;

/// <summary>
/// Avalonia rectangle geometry implementation backed by an ImageSharp rectangle path.
/// </summary>
internal sealed class RectangleGeometryImpl : GeometryImpl
{
    /// <summary>
    /// Initializes a new rectangle geometry.
    /// </summary>
    /// <param name="rect">The rectangle bounds.</param>
    public RectangleGeometryImpl(Rect rect)
        : this(new RectanglePolygon(rect.ToRectangleF()))
    {
    }

    private RectangleGeometryImpl(IPath path)
        : base(path, path, IntersectionRule.EvenOdd)
    {
    }
}
