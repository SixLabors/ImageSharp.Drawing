// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Supported texture format identifiers for native WebGPU targets.
/// </summary>
/// <remarks>
/// Only formats with storage texture binding support are included.
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
    /// </summary>
    Bgra8Unorm,

    /// <summary>
    /// Four-channel 16-bit floating-point RGBA format, mapped to <see cref="HalfVector4"/>.
    /// </summary>
    Rgba16Float
}
