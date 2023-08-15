// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Tests.TestUtilities;

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
