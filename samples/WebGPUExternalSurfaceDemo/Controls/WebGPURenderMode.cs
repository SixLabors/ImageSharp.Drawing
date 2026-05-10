// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace WebGPUExternalSurfaceDemo.Controls;

/// <summary>
/// Defines how a <see cref="WebGPURenderControl"/> schedules frames.
/// </summary>
public enum WebGPURenderMode
{
    /// <summary>
    /// Renders only when WinForms asks the control to paint.
    /// </summary>
    OnDemand,

    /// <summary>
    /// Renders continuously while the WinForms message queue is idle.
    /// </summary>
    Continuous
}
