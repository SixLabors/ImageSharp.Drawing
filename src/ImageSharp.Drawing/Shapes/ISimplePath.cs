// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices.ComTypes;
using SixLabors.ImageSharp.Drawing.Shapes.PolygonClipper;
using SixLabors.PolygonClipper;

namespace SixLabors.ImageSharp.Drawing;

/// <summary>
/// Represents a simple (non-composite) path defined by a series of points.
/// </summary>
public interface ISimplePath
{
    /// <summary>
    /// Gets a value indicating whether this instance is a closed path.
    /// </summary>
    bool IsClosed { get; }

    /// <summary>
    /// Gets the points that make this up as a simple linear path.
    /// </summary>
    ReadOnlyMemory<PointF> Points { get; }

    /// <summary>
    /// Converts to <see cref="SixLabors.PolygonClipper.Polygon"/>
    /// </summary>
    /// <returns>The converted polygon.</returns>
    internal SixLabors.PolygonClipper.Polygon ToPolygon()
    {
        SixLabors.PolygonClipper.Polygon polygon = [];
        Contour contour = new();
        polygon.Add(contour);

        foreach (PointF point in this.Points.Span)
        {
            contour.AddVertex(new Vertex(point.X, point.Y));
        }

        return polygon;
    }
}
