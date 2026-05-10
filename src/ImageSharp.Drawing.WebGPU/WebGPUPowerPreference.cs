// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Describes the adapter power preference requested for the library-managed WebGPU device.
/// </summary>
public enum WebGPUPowerPreference
{
    /// <summary>
    /// Uses the native WebGPU runtime default.
    /// </summary>
    Default,

    /// <summary>
    /// Prefers a lower-power adapter when the platform provides one.
    /// </summary>
    LowPower,

    /// <summary>
    /// Prefers a high-performance adapter when the platform provides one.
    /// </summary>
    HighPerformance
}
