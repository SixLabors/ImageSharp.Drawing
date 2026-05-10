// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Error categories reported by the native WebGPU runtime.
/// </summary>
public enum WebGPUErrorType
{
    /// <summary>
    /// No error was reported.
    /// </summary>
    NoError = 0,

    /// <summary>
    /// The native runtime rejected a command or resource because it violated WebGPU validation rules.
    /// </summary>
    Validation = 1,

    /// <summary>
    /// The native runtime could not allocate the requested GPU resource.
    /// </summary>
    OutOfMemory = 2,

    /// <summary>
    /// The native runtime reported an internal implementation failure.
    /// </summary>
    Internal = 3,

    /// <summary>
    /// The native runtime reported an uncategorized failure.
    /// </summary>
    Unknown = 4,

    /// <summary>
    /// The WebGPU device was lost.
    /// </summary>
    DeviceLost = 5
}
