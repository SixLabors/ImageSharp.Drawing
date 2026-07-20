// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Describes one message produced while compiling a WebGPU layer-effect shader.
/// </summary>
public readonly struct WebGPUShaderDiagnostic
{
    /// <summary>
    /// Initializes a new instance of the <see cref="WebGPUShaderDiagnostic"/> struct.
    /// </summary>
    /// <param name="severity">The diagnostic severity.</param>
    /// <param name="message">The compiler message.</param>
    /// <param name="line">The one-based user-source line, or zero when the message belongs to generated framework code.</param>
    /// <param name="column">The one-based source column, or zero when the compiler does not provide one.</param>
    public WebGPUShaderDiagnostic(WebGPUShaderDiagnosticSeverity severity, string message, int line, int column)
    {
        this.Severity = severity;
        this.Message = message;
        this.Line = line;
        this.Column = column;
    }

    /// <summary>
    /// Gets the diagnostic severity.
    /// </summary>
    public WebGPUShaderDiagnosticSeverity Severity { get; }

    /// <summary>
    /// Gets the compiler message.
    /// </summary>
    public string Message { get; }

    /// <summary>
    /// Gets the one-based user-source line, or zero when the message belongs to generated framework code.
    /// </summary>
    public int Line { get; }

    /// <summary>
    /// Gets the one-based source column, or zero when the compiler does not provide one.
    /// </summary>
    public int Column { get; }
}
