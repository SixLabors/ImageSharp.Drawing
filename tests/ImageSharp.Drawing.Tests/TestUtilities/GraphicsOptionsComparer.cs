// Copyright (c) Six Labors and contributors.
// Licensed under the Apache License, Version 2.0.

using System.Collections.Generic;
using SixLabors.ImageSharp.Drawing.Processing;

namespace SixLabors.ImageSharp.Drawing.Tests.TestUtilities
{
    public class GraphicsOptionsComparer : IEqualityComparer<GraphicsOptions>
    {
        public bool SkipClearOptions { get; set; } = false;
        public bool Equals(GraphicsOptions x, GraphicsOptions y)
        {
            if (this.SkipClearOptions)
            {
                return x.Antialias == y.Antialias
                    && x.AntialiasSubpixelDepth == y.AntialiasSubpixelDepth;
            }

            return x.AlphaCompositionMode == y.AlphaCompositionMode
                && x.Antialias == y.Antialias
                && x.AntialiasSubpixelDepth == y.AntialiasSubpixelDepth
                && x.BlendPercentage == y.BlendPercentage
                && x.ColorBlendingMode == y.ColorBlendingMode;
        }

        public int GetHashCode(GraphicsOptions obj) => obj.GetHashCode();
    }

}
