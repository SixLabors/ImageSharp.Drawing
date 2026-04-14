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
            this.Skip = "WebGPU compute is not available on this system.";
        }
    }
}

/// <summary>
/// A <see cref="TheoryAttribute"/> that skips when WebGPU compute is not available on the current system.
/// </summary>
public class WebGPUTheoryAttribute : TheoryAttribute
{
    public WebGPUTheoryAttribute()
    {
        if (!WebGPUProbe.IsComputeSupported)
        {
            this.Skip = "WebGPU compute is not available on this system.";
        }
    }
}

/// <summary>
/// Caches the result of the WebGPU compute pipeline probe.
/// </summary>
internal static class WebGPUProbe
{
    private static bool? computeSupported;

    internal static bool IsComputeSupported
        => computeSupported ??= WebGPUEnvironment.ProbeComputePipelineSupport() == WebGPUEnvironmentError.Success;
}
