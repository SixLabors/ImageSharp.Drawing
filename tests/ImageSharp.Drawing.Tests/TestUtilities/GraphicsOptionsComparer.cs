// Copyright (c) Six Labors and contributors.
// Licensed under the Apache License, Version 2.0.

using System.Collections.Generic;
using SixLabors.ImageSharp.Processing;

namespace SixLabors.ImageSharp.Drawing.Tests.TestUtilities
{
    public class GraphicsOptionsComparer : IEqualityComparer<GraphicsOptions>
    {
        public bool Equals(GraphicsOptions x, GraphicsOptions y)
        {
            return x.AlphaCompositionMode == y.AlphaCompositionMode
                && x.Antialias == y.Antialias
                && x.AntialiasSubpixelDepth == y.AntialiasSubpixelDepth
                && x.BlendPercentage == y.BlendPercentage
                && x.ColorBlendingMode == y.ColorBlendingMode;
        }

        public int GetHashCode(GraphicsOptions obj) => obj.GetHashCode();
    }

    public class ShapeGraphicsOptionsComparer : IEqualityComparer<ShapeGraphicsOptions>
    {
        public bool Equals(ShapeGraphicsOptions x, ShapeGraphicsOptions y)
        {
            return x.AlphaCompositionMode == y.AlphaCompositionMode
                && x.Antialias == y.Antialias
                && x.AntialiasSubpixelDepth == y.AntialiasSubpixelDepth
                && x.BlendPercentage == y.BlendPercentage
                && x.ColorBlendingMode == y.ColorBlendingMode
                && x.IntersectionRule  == y.IntersectionRule;
        }

        public int GetHashCode(ShapeGraphicsOptions obj) => obj.GetHashCode();
    }
}
