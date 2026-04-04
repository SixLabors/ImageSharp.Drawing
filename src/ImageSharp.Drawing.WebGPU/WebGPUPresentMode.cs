// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Presentation mode used by <see cref="WebGPUWindow{TPixel}"/>.
/// </summary>
/// <remarks>
/// Presentation mode controls how completed frames wait for the display. The choice affects tearing, latency,
/// and whether older queued frames can be skipped in favor of newer ones.
/// </remarks>
public enum WebGPUPresentMode
{
    /// <summary>
    /// Presents frames in first-in, first-out order and waits for the display's vertical refresh.
    /// </summary>
    /// <remarks>
    /// This is the usual v-synced mode. It avoids tearing and is the safest default for general apps, but frames
    /// can spend longer waiting in the presentation queue than in lower-latency modes.
    /// </remarks>
    Fifo = 0,

    /// <summary>
    /// Presents the newest completed frame as soon as possible without waiting for the next vertical refresh.
    /// </summary>
    /// <remarks>
    /// This minimizes presentation queueing latency, but it can show partial frame updates on screen, which most
    /// people experience as tearing.
    /// </remarks>
    Immediate,

    /// <summary>
    /// Queues frames like v-synced presentation, but keeps only the most recent queued frame when the display is not
    /// ready yet.
    /// </summary>
    /// <remarks>
    /// Use this when you want lower latency than FIFO without the visible tearing of immediate presentation. Older
    /// queued frames may be dropped in favor of newer ones, and support depends on the backend and platform.
    /// </remarks>
    Mailbox,
}
