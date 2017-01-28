// <copyright file="Join.cs" company="Scott Williams">
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
    /// Joins 2 points.
    /// </summary>
    internal class Join
    {
        /// <summary>
        /// Gets or sets the out Point 1
        /// </summary>
        internal OutPoint OutPoint1 { get; set; }

        /// <summary>
        /// Gets or sets the out point 2
        /// </summary>
        internal OutPoint OutPoint2 { get; set; }

        /// <summary>
        /// Gets or sets the off point.
        /// </summary>
        internal System.Numerics.Vector2 OffPoint { get; set; }
    }
}