// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing;

/// <inheritdoc cref="PolygonClipper.LineJoin" />
public enum LineJoin
{
    /// <inheritdoc cref="PolygonClipper.LineJoin.Miter" />
    Miter = 0,

    /// <inheritdoc cref="PolygonClipper.LineJoin.MiterRevert" />
    MiterRevert = 1,

    /// <inheritdoc cref="PolygonClipper.LineJoin.Round" />
    Round = 2,

    /// <inheritdoc cref="PolygonClipper.LineJoin.Bevel" />
    Bevel = 3,

    /// <inheritdoc cref="PolygonClipper.LineJoin.MiterRound" />
    MiterRound = 4
}
