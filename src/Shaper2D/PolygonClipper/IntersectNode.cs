// <copyright file="IntersectNode.cs" company="Scott Williams">
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
    internal class IntersectNode
    {
#pragma warning disable SA1401 // Field must be private
        /// <summary>
        /// The edge1
        /// </summary>
        internal Edge Edge1;

        /// <summary>
        /// The edge2
        /// </summary>
        internal Edge Edge2;

        /// <summary>
        /// The pt
        /// </summary>
        internal System.Numerics.Vector2 Pt;
#pragma warning restore SA1401 // Field must be private
    }
}