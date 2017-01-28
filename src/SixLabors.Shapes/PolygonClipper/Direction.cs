// <copyright file="Direction.cs" company="Scott Williams">
// Copyright (c) Scott Williams and contributors.
// Licensed under the Apache License, Version 2.0.
// </copyright>

namespace SixLabors.Shapes.PolygonClipper
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Numerics;
    using System.Runtime.CompilerServices;

    /// <summary>
    /// the direction
    /// </summary>
    internal enum Direction
    {
        /// <summary>
        /// The right to left
        /// </summary>
        RightToLeft,

        /// <summary>
        /// The left to right
        /// </summary>
        LeftToRight
    }
}