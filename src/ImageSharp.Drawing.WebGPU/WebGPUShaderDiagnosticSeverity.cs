// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Identifies the severity of a WebGPU shader compiler message.
/// </summary>
public enum WebGPUShaderDiagnosticSeverity
{
    /// <summary>
    /// An informational compiler message.
    /// </summary>
    Information,

    /// <summary>
    /// A compiler warning.
    /// </summary>
    Warning,

    /// <summary>
    /// A compiler error that prevents execution.
    /// </summary>
    Error
}
