// <copyright file="PolyType.cs" company="Scott Williams">
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
    /// Poly Type
    /// </summary>
    internal enum PolyType
    {
        /// <summary>
        /// The subject
        /// </summary>
        Subject,

        /// <summary>
        /// The clip
        /// </summary>
        Clip
    }
}