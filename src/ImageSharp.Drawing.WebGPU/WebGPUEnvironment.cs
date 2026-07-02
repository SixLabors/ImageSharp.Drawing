// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Runtime.InteropServices;
using Silk.NET.WebGPU.Extensions.WGPU;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Provides explicit support probes for the library-managed WebGPU environment.
/// Use this type when you want to check availability or compute pipeline support before constructing WebGPU objects.
/// </summary>
public static unsafe class WebGPUEnvironment
{
    // Rooted so the native wgpu log callback is not collected while wgpu holds it.
    private static PfnLogCallback nativeLogCallback;
    private static Action<string>? nativeLogSink;

    /// <summary>
    /// Gets or sets the options used when the library-managed WebGPU environment is initialized.
    /// </summary>
    /// <remarks>
    /// Assign this before constructing WebGPU objects or calling support probes. The shared WebGPU runtime reads the
    /// current options during first initialization; changing them later does not reconfigure an existing device.
    /// </remarks>
    public static WebGPUEnvironmentOptions Options { get; set; } = WebGPUEnvironmentOptions.Default;

    /// <summary>
    /// Gets or sets the callback invoked when the native WebGPU runtime reports an uncaptured device error.
    /// </summary>
    /// <remarks>
    /// The callback can be raised from a native WebGPU callback thread. Keep handlers short and non-blocking.
    /// Exceptions thrown by the handler are not propagated back through the native callback.
    /// </remarks>
    public static Action<WebGPUErrorType, string>? UncapturedError { get; set; }

    /// <summary>
    /// Routes the native wgpu-native log stream (including the validation errors that precede a
    /// lost device) to a managed sink. Unlike <see cref="UncapturedError"/>, this surfaces errors
    /// that abort the process through the binding's submit panic before the uncaptured callback runs.
    /// </summary>
    /// <param name="sink">The managed sink invoked for each native log line, or <see langword="null"/> to disable.</param>
    public static void EnableNativeLogging(Action<string>? sink)
    {
        nativeLogSink = sink;
        if (sink is null)
        {
            return;
        }

        Wgpu wgpu = WebGPURuntime.GetWgpuExtension();
        nativeLogCallback = PfnLogCallback.From(HandleNativeLog);
        wgpu.SetLogCallback(nativeLogCallback, null);
        wgpu.SetLogLevel(LogLevel.Warn);
    }

    private static void HandleNativeLog(LogLevel level, byte* message, void* userData)
    {
        Action<string>? sink = nativeLogSink;
        if (sink is null)
        {
            return;
        }

        string text = message is null ? string.Empty : Marshal.PtrToStringUTF8((nint)message) ?? string.Empty;
        try
        {
            sink($"[wgpu {level}] {text}");
        }
        catch
        {
            // The native wgpu callback has no managed caller to receive exceptions.
        }
    }

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
