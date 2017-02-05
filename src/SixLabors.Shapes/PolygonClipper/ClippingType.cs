// <copyright file="ClippingType.cs" company="Scott Williams">
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
    /// Poly Type
    /// </summary>
    public enum ClippingType
    {
        /// <summary>
        /// Represent a main shape to act as a main subject whoes path will be clipped or merged.
        /// </summary>
        Subject,

        /// <summary>
        /// Represents a shape to act as a clipped path.
        /// </summary>
        Clip
    }
}