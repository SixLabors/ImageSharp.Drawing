// <copyright file="LocalMinima.cs" company="Scott Williams">
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
    /// ??
    /// </summary>
    internal class LocalMinima
    {
#pragma warning disable SA1401 // Field must be private
        /// <summary>
        /// The y
        /// </summary>
        internal float Y;

        /// <summary>
        /// The left bound
        /// </summary>
        internal TEdge LeftBound;

        /// <summary>
        /// The right bound
        /// </summary>
        internal TEdge RightBound;

        /// <summary>
        /// The next
        /// </summary>
        internal LocalMinima Next;

#pragma warning restore SA1401 // Field must be private
    }
}