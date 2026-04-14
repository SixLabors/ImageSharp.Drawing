// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Provides explicit support probes for the library-managed WebGPU environment.
/// Use this type when you want to check availability or compute pipeline support before constructing WebGPU objects.
/// </summary>
public static class WebGPUEnvironment
{
    /// <summary>
    /// Probes whether the library-managed WebGPU device and queue are available.
    /// </summary>
    /// <returns>
    /// <see cref="WebGPUEnvironmentError.Success"/> when the library-managed WebGPU device and queue are available;
    /// otherwise, the stable failure code describing why the probe failed.
    /// </returns>
    public static WebGPUEnvironmentError ProbeAvailability()
        => WebGPURuntime.ProbeAvailability();

    /// <summary>
    /// Probes whether the library-managed WebGPU device can create a trivial compute pipeline.
    /// </summary>
    /// <returns>
    /// <see cref="WebGPUEnvironmentError.Success"/> when compute pipeline creation succeeds;
    /// otherwise, the stable failure code describing why the probe failed.
    /// </returns>
    public static WebGPUEnvironmentError ProbeComputePipelineSupport()
        => WebGPURuntime.ProbeComputePipelineSupport();
}
