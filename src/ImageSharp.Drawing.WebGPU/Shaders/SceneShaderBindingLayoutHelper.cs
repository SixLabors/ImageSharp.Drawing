// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Runtime.CompilerServices;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Shared factory helpers for the buffer binding entries used by the staged-scene compute
/// shader bind-group layouts.
/// </summary>
internal static class SceneShaderBindingLayoutHelper
{
    /// <summary>
    /// Creates one compute-stage storage-buffer binding entry.
    /// </summary>
    /// <param name="binding">The WGSL binding index.</param>
    /// <param name="type">The storage-buffer access mode.</param>
    /// <param name="minBindingSize">The minimum buffer binding size in bytes, or 0 to skip validation.</param>
    /// <returns>The populated binding entry.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static BindGroupLayoutEntry CreateStorageEntry(
        uint binding,
        BufferBindingType type,
        nuint minBindingSize = 0)
        => new()
        {
            binding = binding,
            visibility = (ulong)ShaderStage.Compute,
            buffer = new BufferBindingLayout
            {
                type = type,
                hasDynamicOffset = 0U,
                minBindingSize = minBindingSize
            }
        };

    /// <summary>
    /// Creates one compute-stage uniform-buffer binding entry.
    /// </summary>
    /// <param name="binding">The WGSL binding index.</param>
    /// <param name="minBindingSize">The minimum buffer binding size in bytes.</param>
    /// <returns>The populated binding entry.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static BindGroupLayoutEntry CreateUniformEntry(uint binding, nuint minBindingSize)
        => new()
        {
            binding = binding,
            visibility = (ulong)ShaderStage.Compute,
            buffer = new BufferBindingLayout
            {
                type = BufferBindingType.Uniform,
                hasDynamicOffset = 0U,
                minBindingSize = minBindingSize
            }
        };
}
