// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;

namespace SixLabors.ImageSharp.Drawing.Shapes.PolygonClipper
{
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

    // Note: all clipping operations except for Difference are commutative.
    internal enum ClipType
    {
        None,
        Intersection,
        Union,
        Difference,
        Xor
    }
}
