// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing;

/// <summary>
/// Extensions methods fpor the <see cref="GraphicsOptions"/> class.
/// </summary>
internal static class GraphicsOptionsExtensions
{
    /// <summary>
    /// Evaluates if a given SOURCE color can completely replace a BACKDROP color given the current blending and composition settings.
    /// </summary>
    /// <param name="options">The graphics options.</param>
    /// <param name="color">The source color.</param>
    /// <returns>true if the color can be considered opaque</returns>
    /// <remarks>
    /// Blending and composition is an expensive operation, in some cases, like
    /// filling with a solid color, the blending can be avoided by a plain color replacement.
    /// This method can be useful for such processors to select the fast path.
    /// </remarks>
    public static bool IsOpaqueColorWithoutBlending(this GraphicsOptions options, Color color)
    {
        if (options.ColorBlendingMode != PixelColorBlendingMode.Normal)
        {
            return false;
        }

        if (options.AlphaCompositionMode is not PixelAlphaCompositionMode.SrcOver and not PixelAlphaCompositionMode.Src)
        {
            return false;
        }

        const float opaque = 1f;

        if (options.BlendPercentage != opaque)
        {
            return false;
        }

        if (color.ToScaledVector4().W != opaque)
        {
            return false;
        }

        return true;
    }
}
