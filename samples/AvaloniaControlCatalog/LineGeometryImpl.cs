// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using Avalonia;
using SixLabors.ImageSharp.Drawing;

namespace AvaloniaControlCatalog;

/// <summary>
/// Avalonia line geometry implementation backed by an ImageSharp open path.
/// </summary>
internal sealed class LineGeometryImpl : GeometryImpl
{
    /// <summary>
    /// Initializes a new line geometry.
    /// </summary>
    /// <param name="p1">The line start point.</param>
    /// <param name="p2">The line end point.</param>
    public LineGeometryImpl(Point p1, Point p2)
        : base(new PathBuilder().AddLine(p1.ToPointF(), p2.ToPointF()).Build(), null, IntersectionRule.EvenOdd)
    {
    }
}
