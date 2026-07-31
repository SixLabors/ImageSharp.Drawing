// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Describes the CPU access requested when mapping a WebGPU buffer.
/// </summary>
[Flags]
internal enum MapMode : ulong
{
    /// <summary>
    /// No CPU mapping access is requested.
    /// </summary>
    None = 0,

    /// <summary>
    /// CPU read access is requested.
    /// </summary>
    Read = 1,

    /// <summary>
    /// CPU write access is requested.
    /// </summary>
    Write = 2
}
