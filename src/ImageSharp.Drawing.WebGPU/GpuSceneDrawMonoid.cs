// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Additive monoid scanned over the draw-tag stream to derive scene offsets for later stages.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal readonly struct GpuSceneDrawMonoid
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GpuSceneDrawMonoid"/> struct.
    /// </summary>
    /// <param name="pathIndex">The path index increment.</param>
    /// <param name="clipIndex">The clip index increment.</param>
    /// <param name="sceneOffset">The scene-data offset increment.</param>
    /// <param name="infoOffset">The info-data offset increment.</param>
    public GpuSceneDrawMonoid(uint pathIndex, uint clipIndex, uint sceneOffset, uint infoOffset)
    {
        this.PathIndex = pathIndex;
        this.ClipIndex = clipIndex;
        this.SceneOffset = sceneOffset;
        this.InfoOffset = infoOffset;
    }

    /// <summary>
    /// Gets the path index increment.
    /// </summary>
    public uint PathIndex { get; }

    /// <summary>
    /// Gets the clip index increment.
    /// </summary>
    public uint ClipIndex { get; }

    /// <summary>
    /// Gets the scene-data offset increment.
    /// </summary>
    public uint SceneOffset { get; }

    /// <summary>
    /// Gets the info-data offset increment.
    /// </summary>
    public uint InfoOffset { get; }

    /// <summary>
    /// Combines two draw monoids by adding each offset/count component independently.
    /// </summary>
    /// <param name="a">The first draw monoid.</param>
    /// <param name="b">The second draw monoid.</param>
    /// <returns>The combined draw monoid.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static GpuSceneDrawMonoid Combine(in GpuSceneDrawMonoid a, in GpuSceneDrawMonoid b)
        => new(
            a.PathIndex + b.PathIndex,
            a.ClipIndex + b.ClipIndex,
            a.SceneOffset + b.SceneOffset,
            a.InfoOffset + b.InfoOffset);
}
