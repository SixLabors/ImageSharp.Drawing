// <copyright file="Maxima.cs" company="Scott Williams">
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
    /// Represents the maxima
    /// </summary>
    internal class Maxima
    {
        /// <summary>
        /// Gets or sets the x
        /// </summary>
        internal float X { get; set; }

        /// <summary>
        /// Gets or sets the next <see cref="Maxima"/>
        /// </summary>
        internal Maxima Next { get; set; }

        /// <summary>
        /// Gets or sets the previous <see cref="Maxima"/>
        /// </summary>
        internal Maxima Previous { get; set; }
    }
}