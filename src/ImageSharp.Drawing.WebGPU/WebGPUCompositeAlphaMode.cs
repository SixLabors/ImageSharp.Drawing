// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Specifies how the native compositor interprets alpha in a WebGPU surface.
/// </summary>
public enum WebGPUCompositeAlphaMode
{
    /// <summary>
    /// Allows WebGPU to select the surface composition mode.
    /// </summary>
    Auto,

    /// <summary>
    /// Ignores the surface alpha channel and presents every pixel as opaque.
    /// </summary>
    Opaque,

    /// <summary>
    /// Presents color components that have already been multiplied by alpha.
    /// </summary>
    Premultiplied = 2,
}
