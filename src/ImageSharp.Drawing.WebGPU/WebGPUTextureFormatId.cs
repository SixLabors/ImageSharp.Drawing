// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Public WebGPU texture format identifiers used by <see cref="WebGPUSurfaceCapability"/>.
/// </summary>
public enum WebGPUTextureFormatId
{
    // Numeric values intentionally match <c>WGPUTextureFormat</c>.

    /// <summary>
    /// Single-channel 8-bit normalized unsigned format.
    /// </summary>
    R8Unorm = 0x01,

    /// <summary>
    /// Two-channel 8-bit normalized unsigned format.
    /// </summary>
    RG8Unorm = 0x08,

    /// <summary>
    /// Two-channel 8-bit normalized signed format.
    /// </summary>
    RG8Snorm = 0x09,

    /// <summary>
    /// Four-channel 8-bit normalized signed format.
    /// </summary>
    Rgba8Snorm = 0x14,

    /// <summary>
    /// Single-channel 16-bit floating-point format.
    /// </summary>
    R16Float = 0x07,

    /// <summary>
    /// Two-channel 16-bit floating-point format.
    /// </summary>
    RG16Float = 0x11,

    /// <summary>
    /// Four-channel 16-bit floating-point format.
    /// </summary>
    Rgba16Float = 0x22,

    /// <summary>
    /// Two-channel 16-bit signed integer format.
    /// </summary>
    RG16Sint = 0x10,

    /// <summary>
    /// Four-channel 16-bit signed integer format.
    /// </summary>
    Rgba16Sint = 0x21,

    /// <summary>
    /// Packed 10:10:10:2 normalized unsigned format.
    /// </summary>
    Rgb10A2Unorm = 0x1A,

    /// <summary>
    /// Four-channel 8-bit normalized unsigned RGBA format.
    /// </summary>
    Rgba8Unorm = 0x12,

    /// <summary>
    /// Four-channel 8-bit normalized unsigned BGRA format.
    /// </summary>
    Bgra8Unorm = 0x17,

    /// <summary>
    /// Four-channel 32-bit floating-point format.
    /// </summary>
    Rgba32Float = 0x23,

    /// <summary>
    /// Single-channel 16-bit unsigned integer format.
    /// </summary>
    R16Uint = 0x05,

    /// <summary>
    /// Two-channel 16-bit unsigned integer format.
    /// </summary>
    RG16Uint = 0x0F,

    /// <summary>
    /// Four-channel 16-bit unsigned integer format.
    /// </summary>
    Rgba16Uint = 0x20,

    /// <summary>
    /// Four-channel 8-bit unsigned integer format.
    /// </summary>
    Rgba8Uint = 0x15
}
