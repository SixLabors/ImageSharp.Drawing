// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// The exception thrown when a WebGPU layer-effect shader cannot be compiled.
/// </summary>
public sealed class WebGPUShaderCompilationException : Exception
{
    private readonly WebGPUShaderDiagnostic[] diagnostics;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebGPUShaderCompilationException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="diagnostics">The compiler diagnostics associated with the failure.</param>
    internal WebGPUShaderCompilationException(string message, WebGPUShaderDiagnostic[] diagnostics)
        : base(message)
        => this.diagnostics = diagnostics;

    /// <summary>
    /// Gets the compiler diagnostics associated with the failure.
    /// </summary>
    public IReadOnlyList<WebGPUShaderDiagnostic> Diagnostics => this.diagnostics;
}
