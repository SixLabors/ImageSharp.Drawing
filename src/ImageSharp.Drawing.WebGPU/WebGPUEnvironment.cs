// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Diagnostics.CodeAnalysis;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Provides explicit support probes for the library-managed WebGPU environment.
/// Use this type when you want to check availability or compute pipeline support before constructing WebGPU objects.
/// </summary>
public static class WebGPUEnvironment
{
    /// <summary>
    /// Tries to acquire the library-managed WebGPU device and queue.
    /// </summary>
    /// <param name="error">Receives the failure reason when the probe fails.</param>
    /// <returns><see langword="true"/> when the library-managed WebGPU device and queue are available; otherwise, <see langword="false"/>.</returns>
    public static bool TryProbeAvailability([NotNullWhen(false)] out string? error)
        => WebGPURuntime.TryProbeAvailability(out error);

    /// <summary>
    /// Tries to create a trivial compute pipeline using the library-managed WebGPU device.
    /// </summary>
    /// <param name="error">Receives the failure reason when the probe fails.</param>
    /// <returns><see langword="true"/> when compute pipeline creation succeeds; otherwise, <see langword="false"/>.</returns>
    public static bool TryProbeComputePipelineSupport([NotNullWhen(false)] out string? error)
        => WebGPURuntime.TryProbeComputePipelineSupport(out error);
}
