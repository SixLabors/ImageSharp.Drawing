// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Processing.Backends.Native;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Identifies one exact generated layer-effect pipeline without using user labels as cache keys.
/// </summary>
internal readonly struct WebGPUEffectPipelineKey : IEquatable<WebGPUEffectPipelineKey>
{
    private readonly string source;
    private readonly int uniformByteLength;
    private readonly WGPUTextureFormat outputFormat;
    private readonly int hashCode;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebGPUEffectPipelineKey"/> struct.
    /// </summary>
    /// <param name="moduleSource">The exact generated shader module.</param>
    /// <param name="uniformByteLength">The byte length of its user uniform binding.</param>
    /// <param name="outputFormat">The render target format written by the shader.</param>
    public WebGPUEffectPipelineKey(WebGPUShaderModuleSource moduleSource, int uniformByteLength, WGPUTextureFormat outputFormat)
    {
        this.source = moduleSource.Source;
        this.uniformByteLength = uniformByteLength;
        this.outputFormat = outputFormat;
        this.hashCode = HashCode.Combine(moduleSource.PrecomputedHashCode, uniformByteLength, outputFormat);
    }

    /// <inheritdoc />
    public bool Equals(WebGPUEffectPipelineKey other)
        => this.uniformByteLength == other.uniformByteLength &&
           this.outputFormat == other.outputFormat &&
           string.Equals(this.source, other.source, StringComparison.Ordinal);

    /// <inheritdoc />
    public override bool Equals(object? obj)
        => obj is WebGPUEffectPipelineKey other && this.Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => this.hashCode;
}
