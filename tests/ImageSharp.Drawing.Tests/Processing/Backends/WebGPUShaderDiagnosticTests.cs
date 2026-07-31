// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Processing.Backends;

namespace SixLabors.ImageSharp.Drawing.Tests.Processing.Backends;

public class WebGPUShaderDiagnosticTests
{
    [Fact]
    public void Constructor_StoresAllValues()
    {
        WebGPUShaderDiagnostic diagnostic = new(WebGPUShaderDiagnosticSeverity.Error, "bad shader", 4, 7);

        Assert.Equal(WebGPUShaderDiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal("bad shader", diagnostic.Message);
        Assert.Equal(4, diagnostic.Line);
        Assert.Equal(7, diagnostic.Column);
    }

    [Fact]
    public void CompilationException_ExposesMessageAndDiagnostics()
    {
        WebGPUShaderDiagnostic[] diagnostics =
        [
            new(WebGPUShaderDiagnosticSeverity.Warning, "first", 1, 2),
            new(WebGPUShaderDiagnosticSeverity.Error, "second", 3, 4),
        ];

        WebGPUShaderCompilationException exception = new("compilation failed", diagnostics);

        Assert.Equal("compilation failed", exception.Message);
        Assert.Equal(2, exception.Diagnostics.Count);
        Assert.Equal("second", exception.Diagnostics[1].Message);
    }
}
