// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Options for the library-managed WebGPU environment.
/// </summary>
/// <remarks>
/// Assign these options before constructing WebGPU objects or calling support probes. The shared WebGPU runtime reads
/// the current options during first initialization; changing them later does not reconfigure an existing device.
/// </remarks>
public sealed class WebGPUEnvironmentOptions
{
    /// <summary>
    /// Gets the default WebGPU environment options.
    /// </summary>
    public static WebGPUEnvironmentOptions Default { get; } = new();

    /// <summary>
    /// Gets or sets the adapter power preference requested for the library-managed device.
    /// </summary>
    public WebGPUPowerPreference PowerPreference { get; set; } = WebGPUPowerPreference.HighPerformance;
}
