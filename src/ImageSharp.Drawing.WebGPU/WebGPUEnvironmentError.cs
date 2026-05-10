// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Error codes returned by <see cref="WebGPUEnvironment"/> probe methods.
/// </summary>
/// <remarks>
/// <see cref="Success"/> is the only successful result. All other values describe one stable
/// failure case that callers can branch on without parsing diagnostic strings.
/// </remarks>
public enum WebGPUEnvironmentError
{
    /// <summary>
    /// The probe succeeded.
    /// </summary>
    Success = 0,

    /// <summary>
    /// The shared WebGPU API loader or required extension could not be initialized.
    /// </summary>
    ApiInitializationFailed,

    /// <summary>
    /// The runtime could not create a WebGPU instance.
    /// </summary>
    InstanceCreationFailed,

    /// <summary>
    /// The adapter request callback did not complete before the timeout expired.
    /// </summary>
    AdapterRequestTimedOut,

    /// <summary>
    /// The runtime failed to acquire a WebGPU adapter.
    /// </summary>
    AdapterRequestFailed,

    /// <summary>
    /// The device request callback did not complete before the timeout expired.
    /// </summary>
    DeviceRequestTimedOut,

    /// <summary>
    /// The runtime failed to acquire a WebGPU device.
    /// </summary>
    DeviceRequestFailed,

    /// <summary>
    /// The runtime acquired a device but could not retrieve its default queue.
    /// </summary>
    QueueAcquisitionFailed,

    /// <summary>
    /// The runtime could not provision the shared device/queue pair for an unspecified reason.
    /// </summary>
    DeviceAcquisitionFailed,

    /// <summary>
    /// The isolated compute-pipeline probe ran to completion and reported that compute pipeline creation failed.
    /// </summary>
    ComputePipelineCreationFailed,

    /// <summary>
    /// The isolated compute-pipeline probe process terminated before it could report a normal result.
    /// </summary>
    ComputePipelineProbeProcessFailed,
}
