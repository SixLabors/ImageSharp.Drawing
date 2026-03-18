// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Runtime.CompilerServices;
using Silk.NET.WebGPU;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

internal static class SceneShaderBindingLayoutHelper
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static BindGroupLayoutEntry CreateStorageEntry(
        uint binding,
        BufferBindingType type,
        nuint minBindingSize = 0)
        => new()
        {
            Binding = binding,
            Visibility = ShaderStage.Compute,
            Buffer = new BufferBindingLayout
            {
                Type = type,
                HasDynamicOffset = false,
                MinBindingSize = minBindingSize
            }
        };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static BindGroupLayoutEntry CreateUniformEntry(uint binding, nuint minBindingSize)
        => new()
        {
            Binding = binding,
            Visibility = ShaderStage.Compute,
            Buffer = new BufferBindingLayout
            {
                Type = BufferBindingType.Uniform,
                HasDynamicOffset = false,
                MinBindingSize = minBindingSize
            }
        };
}
