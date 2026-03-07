// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Supported WebGPU texture format identifiers used by <see cref="WebGPUSurfaceCapability"/>.
/// </summary>
/// <remarks>
/// Only formats with storage texture binding support are included.
/// Numeric values match the WebGPU <c>WGPUTextureFormat</c> constants.
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
    /// Four-channel 8-bit unsigned integer format.
    /// </summary>
    Rgba8Uint = 0x15,

    /// <summary>
    /// Four-channel 8-bit normalized unsigned BGRA format.
    /// </summary>
    Bgra8Unorm = 0x17,

    /// <summary>
    /// Four-channel 16-bit unsigned integer format.
    /// </summary>
    Rgba16Uint = 0x20,

    /// <summary>
    /// Four-channel 16-bit signed integer format.
    /// </summary>
    Rgba16Sint = 0x21,

    /// <summary>
    /// Four-channel 16-bit floating-point format.
    /// </summary>
    Rgba16Float = 0x22,

    /// <summary>
    /// Four-channel 32-bit floating-point format.
    /// </summary>
    Rgba32Float = 0x23,
}
