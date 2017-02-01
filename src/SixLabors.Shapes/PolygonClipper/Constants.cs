// <copyright file="Constants.cs" company="Scott Williams">
// Copyright (c) Scott Williams and contributors.
// Licensed under the Apache License, Version 2.0.
// </copyright>

namespace SixLabors.Shapes.PolygonClipper
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;

    /// <summary>
    /// Clipper contants
    /// </summary>
    internal static class Constants
    {
        /// <summary>
        /// The unassigned
        /// </summary>
        public const int Unassigned = -1; // InitOptions that can be passed to the constructor ...

        /// <summary>
        /// The skip
        /// </summary>
        public const int Skip = -2;

        /// <summary>
        /// The horizontal delta limit
        /// </summary>
        public const double HorizontalDeltaLimit = -3.4E+38;
    }
}
