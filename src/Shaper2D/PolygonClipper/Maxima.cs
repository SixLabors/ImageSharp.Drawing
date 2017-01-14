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
    /// ??
    /// </summary>
    internal class Maxima
    {
#pragma warning disable SA1401 // Field must be private
        /// <summary>
        /// The x
        /// </summary>
        internal float X;

        /// <summary>
        /// The next
        /// </summary>
        internal Maxima Next;

        /// <summary>
        /// The previous
        /// </summary>
        internal Maxima Prev;
#pragma warning restore SA1401 // Field must be private
    }
}