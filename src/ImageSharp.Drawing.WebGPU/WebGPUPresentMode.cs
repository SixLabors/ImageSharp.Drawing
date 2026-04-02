// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Presentation mode used by <see cref="WebGPUWindow{TPixel}"/>.
/// </summary>
public enum WebGPUPresentMode
{
    /// <summary>
    /// Vertical-sync FIFO presentation.
    /// </summary>
    Fifo = 0,

    /// <summary>
    /// Immediate presentation without waiting for vertical sync.
    /// </summary>
    Immediate,

    /// <summary>
    /// Mailbox presentation when supported by the backend.
    /// </summary>
    Mailbox,
}
