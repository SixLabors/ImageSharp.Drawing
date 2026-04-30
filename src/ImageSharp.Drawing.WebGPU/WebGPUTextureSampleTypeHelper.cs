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
    /// <summary>
    /// Resolves the sampled texture type used when a composite shader reads the specified format.
    /// </summary>
    public static TextureSampleType GetInputSampleType(TextureFormat textureFormat)
        => WebGPUDrawingBackend.GetCompositeTextureSampleType(textureFormat);
}
