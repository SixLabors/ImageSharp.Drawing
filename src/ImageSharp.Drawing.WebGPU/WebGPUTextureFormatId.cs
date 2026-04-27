// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Supported texture format identifiers for native WebGPU targets.
/// </summary>
/// <remarks>
/// Only formats with storage texture binding support are included.
/// Numeric values match the WebGPU texture-format constants.
/// </remarks>
public enum WebGPUTextureFormatId
{
    /// <summary>
    /// Four-channel 8-bit normalized unsigned RGBA format.
    /// </summary>
    Rgba8Unorm = 0x12,

    /// <summary>
    /// Four-channel 8-bit normalized signed format.
    /// </summary>
    Rgba8Snorm = 0x14,

    /// <summary>
    /// Four-channel 8-bit normalized unsigned BGRA format.
    /// </summary>
    Bgra8Unorm = 0x17,

    /// <summary>
    /// Four-channel 16-bit floating-point format.
    /// </summary>
    Rgba16Float = 0x22
}
