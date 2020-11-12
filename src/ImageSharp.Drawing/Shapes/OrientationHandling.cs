// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

namespace SixLabors.ImageSharp.Drawing.Shapes
{
    /// <summary>
    /// Defines polygon orientation handling mode when creating <see cref="TessellatedMultipolygon"/> from <see cref="IPath"/>.
    /// </summary>
    internal enum OrientationHandling
    {
        KeepOriginal,
        ForcePositiveOrientationOnSimplePolygons,
        FirstRingIsContourFollowedByHoles
    }
}