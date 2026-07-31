// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Runtime.CompilerServices;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Encoded draw-tag constants matching the staged-scene shader contract.
/// </summary>
internal static class GpuSceneDrawTag
{
    // These values are not a plain enum because each word also encodes the path-count, clip-count,
    // scene-word-count, and info-word-count increments consumed by the scan/reduction stages.
    // Visible-fill tags carry five extra info words: coverage threshold plus raster interest. Must match drawtag.wgsl.
    public const uint Nop = 0U;
    public const uint FillColor = 0x188U;
    public const uint FillRecolor = 0x184U;
    public const uint FillLinGradient = 0x254U;
    public const uint FillRadGradient = 0x3DCU;
    public const uint FillEllipticGradient = 0x35CU;
    public const uint FillSweepGradient = 0x394U;
    public const uint FillPathGradient = 0x190U;
    public const uint FillImage = 0x3D4U;
    public const uint BeginClip = 0x49U;
    public const uint EndClip = 0x21U;
    public const uint FillInfoFlagsFillRuleBit = 1U;
    public const uint FillInfoFlagsAliasedBit = 0x40000000U;

    /// <summary>
    /// Decodes one packed draw tag into the additive monoid scanned by later scheduling passes.
    /// </summary>
    /// <param name="tagWord">The packed draw tag word.</param>
    /// <returns>The decoded draw monoid.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static GpuSceneDrawMonoid Map(uint tagWord)
        => new(
            tagWord != Nop ? 1U : 0U,
            tagWord & 1U,
            (tagWord >> 2) & 0x07U,
            (tagWord >> 6) & 0x0FU);
}
