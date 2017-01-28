// <copyright file="LocalMinima.cs" company="Scott Williams">
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
    /// Represents a local minima
    /// </summary>
    internal class LocalMinima
    {
        /// <summary>
        /// Gets or sets the y
        /// </summary>
        internal float Y { get; set; }

        /// <summary>
        /// Gets or sets the left bound
        /// </summary>
        internal Edge LeftBound { get; set; }

        /// <summary>
        /// Gets or sets the right bound
        /// </summary>
        internal Edge RightBound { get; set; }

        /// <summary>
        /// Gets or sets the next
        /// </summary>
        internal LocalMinima Next { get; set; }
    }
}