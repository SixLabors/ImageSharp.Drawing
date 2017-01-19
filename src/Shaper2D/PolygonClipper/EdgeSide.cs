// <copyright file="EdgeSide.cs" company="Scott Williams">
// Copyright (c) Scott Williams and contributors.
// Licensed under the Apache License, Version 2.0.
// </copyright>

namespace Shaper2D.PolygonClipper
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Numerics;
    using System.Runtime.CompilerServices;

    /// <summary>
    /// which side the edge represents.
    /// </summary>
    internal enum EdgeSide
    {
        /// <summary>
        /// The left
        /// </summary>
        Left,

        /// <summary>
        /// The right
        /// </summary>
        Right
    }
}