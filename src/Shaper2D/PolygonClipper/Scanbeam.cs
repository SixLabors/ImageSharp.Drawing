// <copyright file="Scanbeam.cs" company="Scott Williams">
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
    /// Scanbeam
    /// </summary>
    internal class Scanbeam // would this work as a struct?
    {
        /// <summary>
        /// Gets or sets the y
        /// </summary>
        public float Y { get; set; }

        /// <summary>
        /// Gets or sets the next
        /// </summary>
        public Scanbeam Next { get; set; }
    }
}