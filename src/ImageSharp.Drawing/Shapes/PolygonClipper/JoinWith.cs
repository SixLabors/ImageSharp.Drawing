// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Shapes.PolygonClipper;

internal enum JoinWith
{
    None,
    Left,
    Right
}

internal enum HorzPosition
{
    Bottom,
    Middle,
    Top
}

// Vertex: a pre-clipping data structure. It is used to separate polygons
// into ascending and descending 'bounds' (or sides) that start at local
// minima and ascend to a local maxima, before descending again.
[Flags]
internal enum PointInPolygonResult
{
    IsOn = 0,
    IsInside = 1,
    IsOutside = 2
}
