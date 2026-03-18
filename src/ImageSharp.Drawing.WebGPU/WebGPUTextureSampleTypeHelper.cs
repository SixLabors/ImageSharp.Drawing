// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using Silk.NET.WebGPU;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Resolves the sampled texture type for formats explicitly supported by
/// <see cref="WebGPUDrawingBackend"/> composite registrations.
/// </summary>
internal static class WebGPUTextureSampleTypeHelper
{
    public static bool TryGetInputSampleType(TextureFormat textureFormat, out TextureSampleType sampleType)
        => WebGPUDrawingBackend.TryGetCompositeTextureSampleType(textureFormat, out sampleType);
}
