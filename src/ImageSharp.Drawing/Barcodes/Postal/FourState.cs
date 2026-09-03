// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The four states of a height modulated postal bar. Every bar has the tracker in the middle. An ascender adds the part above it, a descender the part
/// below it, and a full bar both.
/// </summary>
internal enum FourState : byte
{
    /// <summary>
    /// The tracker alone.
    /// </summary>
    Tracker = 0,

    /// <summary>
    /// The tracker and the descender below it.
    /// </summary>
    Descender = 1,

    /// <summary>
    /// The tracker and the ascender above it.
    /// </summary>
    Ascender = 2,

    /// <summary>
    /// The ascender, the tracker and the descender.
    /// </summary>
    Full = 3,
}
