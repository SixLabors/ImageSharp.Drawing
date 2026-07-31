// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Processing.Backends;

namespace SixLabors.ImageSharp.Drawing.Tests.TestUtilities.Attributes;

/// <summary>
/// A <see cref="FactAttribute"/> that skips when WebGPU compute is not available on the current system.
/// </summary>
public class WebGPUFactAttribute : FactAttribute
{
    public WebGPUFactAttribute()
    {
        if (!WebGPUProbe.IsComputeSupported)
        {
            this.Skip = WebGPUProbe.ComputeUnsupportedSkipMessage;
        }
    }
}

/// <summary>
/// Caches the result of the WebGPU compute pipeline probe.
/// </summary>
internal static class WebGPUProbe
{
    private static WebGPUEnvironmentError? computeProbeResult;

    /// <summary>
    /// Gets the cached WebGPU compute-pipeline probe result.
    /// </summary>
    internal static WebGPUEnvironmentError ComputeProbeResult
        => computeProbeResult ??= WebGPUEnvironment.ProbeComputePipelineSupport();

    /// <summary>
    /// Gets a value indicating whether WebGPU compute is supported on the current system.
    /// </summary>
    internal static bool IsComputeSupported
        => ComputeProbeResult == WebGPUEnvironmentError.Success;

    /// <summary>
    /// Gets the skip message used when WebGPU compute is unavailable.
    /// </summary>
    internal static string ComputeUnsupportedSkipMessage
        => $"WebGPU compute is not available on this system: {ComputeProbeResult}.";
}
