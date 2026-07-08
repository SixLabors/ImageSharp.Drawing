// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Supported texture format identifiers for native WebGPU targets.
/// </summary>
/// <remarks>
/// The compute pipeline writes its result through a storage texture binding, so a target format
/// must be storage-bindable. Most formats here are storage-capable in core WebGPU;
/// <see cref="Bgra8Unorm"/> is the exception and requires the optional <c>bgra8unorm-storage</c>
/// device feature, which the backend enables when the adapter reports it.
/// </remarks>
public enum WebGPUTextureFormat
{
    /// <summary>
    /// Four-channel 8-bit normalized unsigned RGBA format, mapped to <see cref="Rgba32"/>.
    /// </summary>
    Rgba8Unorm,

    /// <summary>
    /// Four-channel 8-bit normalized signed RGBA format, mapped to <see cref="NormalizedByte4"/>.
    /// </summary>
    Rgba8Snorm,

    /// <summary>
    /// Four-channel 8-bit normalized unsigned BGRA format, mapped to <see cref="Bgra32"/>.
    /// Storage binding for this format requires the optional <c>bgra8unorm-storage</c> device feature.
    /// </summary>
    Bgra8Unorm,

    /// <summary>
    /// Four-channel 16-bit floating-point RGBA format, mapped to <see cref="HalfVector4"/>.
    /// </summary>
    Rgba16Float
}
