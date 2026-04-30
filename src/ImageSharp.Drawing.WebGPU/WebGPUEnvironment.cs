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
    /// Gets or sets the callback invoked when the native WebGPU runtime reports an uncaptured device error.
    /// </summary>
    /// <remarks>
    /// The callback can be raised from a native WebGPU callback thread. Keep handlers short and non-blocking.
    /// Exceptions thrown by the handler are not propagated back through the native callback.
    /// </remarks>
    public static Action<WebGPUErrorType, string>? UncapturedError { get; set; }

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

    /// <summary>
    /// Reports one uncaptured native WebGPU error to the configured public callback.
    /// </summary>
    internal static void ReportUncapturedError(WebGPUErrorType errorType, string message)
    {
        Action<WebGPUErrorType, string>? callback = UncapturedError;
        if (callback is null)
        {
            return;
        }

        try
        {
            callback(errorType, message);
        }
        catch
        {
            // The native WebGPU callback has no managed caller to receive exceptions.
            // Letting them escape through the unmanaged callback boundary can terminate the process.
        }
    }
}
