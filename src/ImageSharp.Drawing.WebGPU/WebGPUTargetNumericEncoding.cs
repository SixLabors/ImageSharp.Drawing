// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Describes how a WebGPU target's native channel values represent ImageSharp's unit color values.
/// </summary>
internal enum WebGPUTargetNumericEncoding
{
    /// <summary>
    /// Native channel values already use ImageSharp's unit color range.
    /// </summary>
    Unit,

    /// <summary>
    /// Native channel values map ImageSharp's unit color range onto the signed unit range.
    /// </summary>
    SignedUnit
}
