// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Runtime.CompilerServices;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing.Processors.Convolution;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Stores one validated WGSL program shared by effect invocations using the same source and uniform layout.
/// </summary>
internal sealed class WebGPUShaderProgram
{
    /// <summary>
    /// The maximum UTF-8 byte length accepted for one authored WGSL program.
    /// </summary>
    public const int MaximumSourceByteLength = 8 * 1024 * 1024;

    // Layouts are normally static declarations owned by an effect type. Weak keys allow dynamically
    // created layouts and their cached programs to be collected together.
    private static readonly ConditionalWeakTable<WebGPUShaderUniformLayout, ProgramCache> ProgramsByLayout = [];

    private readonly object moduleSourceSync = new();
    private readonly Dictionary<
        (PixelAlphaRepresentation AlphaRepresentation, WebGPUTargetNumericEncoding NumericEncoding, BorderWrappingMode? XBorderMode, BorderWrappingMode? YBorderMode),
        WebGPUShaderModuleSource> moduleSources = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="WebGPUShaderProgram"/> class.
    /// </summary>
    /// <param name="source">The WGSL module fragment defining the layer effect.</param>
    /// <param name="uniformLayout">The named values exposed to the WGSL source.</param>
    public WebGPUShaderProgram(string source, WebGPUShaderUniformLayout uniformLayout)
    {
        Guard.NotNull(source, nameof(source));
        Guard.NotNull(uniformLayout, nameof(uniformLayout));

        WebGPUShaderSourceValidator.Validate(source, nameof(source));
        this.Source = source;
        this.UniformLayout = uniformLayout;
    }

    /// <summary>
    /// Gets the WGSL module fragment defining the layer effect.
    /// </summary>
    public string Source { get; }

    /// <summary>
    /// Gets the named values exposed to the WGSL source.
    /// </summary>
    public WebGPUShaderUniformLayout UniformLayout { get; }

    /// <summary>
    /// Gets the shared program for an exact source and layout contract.
    /// </summary>
    public static WebGPUShaderProgram GetOrCreate(string source, WebGPUShaderUniformLayout uniformLayout)
    {
        Guard.NotNull(source, nameof(source));
        Guard.NotNull(uniformLayout, nameof(uniformLayout));

        ProgramCache cache = ProgramsByLayout.GetValue(uniformLayout, static _ => new ProgramCache());
        return cache.GetOrCreate(source, uniformLayout);
    }

    /// <summary>
    /// Gets the generated module specialized for one source representation.
    /// </summary>
    /// <param name="sourceDescriptor">The physical and logical source texture representation.</param>
    /// <param name="xBorderMode">The horizontal border mode used by filtered samples.</param>
    /// <param name="yBorderMode">The vertical border mode used by filtered samples.</param>
    /// <returns>The generated module source.</returns>
    public WebGPUShaderModuleSource GetModuleSource(
        WebGPUTargetDescriptor sourceDescriptor,
        BorderWrappingMode? xBorderMode,
        BorderWrappingMode? yBorderMode)
    {
        (PixelAlphaRepresentation, WebGPUTargetNumericEncoding, BorderWrappingMode?, BorderWrappingMode?) key = (
            sourceDescriptor.AlphaRepresentation,
            sourceDescriptor.NumericEncoding,
            xBorderMode,
            yBorderMode);

        lock (this.moduleSourceSync)
        {
            if (!this.moduleSources.TryGetValue(key, out WebGPUShaderModuleSource? moduleSource))
            {
                // Wrapper generation includes both representation conversion and border-coordinate
                // mapping. The immutable result is reused by every pass with that exact contract.
                moduleSource = WebGPUShaderModuleSource.Create(this, sourceDescriptor, xBorderMode, yBorderMode);
                this.moduleSources.Add(key, moduleSource);
            }

            return moduleSource;
        }
    }

    /// <summary>
    /// Owns the source variants associated with one live uniform layout.
    /// </summary>
    private sealed class ProgramCache
    {
        private readonly List<WebGPUShaderProgram> programs = [];

        /// <summary>
        /// Gets or creates the program for one source string under the owning layout.
        /// </summary>
        public WebGPUShaderProgram GetOrCreate(string source, WebGPUShaderUniformLayout uniformLayout)
        {
            lock (this.programs)
            {
                for (int i = 0; i < this.programs.Count; i++)
                {
                    WebGPUShaderProgram candidate = this.programs[i];
                    if (StringComparer.Ordinal.Equals(candidate.Source, source))
                    {
                        return candidate;
                    }
                }

                // Public WGSL validation occurs once per distinct source/layout contract.
                WebGPUShaderProgram program = new(source, uniformLayout);
                this.programs.Add(program);
                return program;
            }
        }
    }
}
