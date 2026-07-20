// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Processing.Processors.Convolution;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Defines a backdrop effect with complete WGSL shader passes and an equivalent ImageSharp CPU fallback.
/// </summary>
/// <remarks>
/// Instances work with both drawing backends. WebGPU executes the configured WGSL passes, while CPU rendering
/// executes the required fallback effect. Source follows the same complete-shader contract as
/// <see cref="WebGPUShaderLayerEffect"/>.
/// </remarks>
public abstract class WebGPUBackdropShaderLayerEffect : BackdropLayerEffect, IWebGPUShaderEffect, IWebGPUShaderEffectSource
{
    private readonly string shaderSource;
    private readonly WebGPUShaderUniformLayout uniformLayout;
    private WebGPUShaderProgram? program;
    private WebGPUShaderPass[] shaderPasses = [];
    private int shaderPassCount;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebGPUBackdropShaderLayerEffect"/> class.
    /// </summary>
    /// <param name="shaderSource">The complete WGSL source for each pass added by this effect.</param>
    /// <param name="uniformLayout">The named uniform values available to the shader.</param>
    /// <param name="fallback">The equivalent operation used by CPU rendering.</param>
    protected WebGPUBackdropShaderLayerEffect(
        string shaderSource,
        WebGPUShaderUniformLayout uniformLayout,
        Action<IImageProcessingContext> fallback)
        : base(fallback)
    {
        this.shaderSource = shaderSource;
        this.uniformLayout = uniformLayout;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="WebGPUBackdropShaderLayerEffect"/> class.
    /// </summary>
    /// <param name="fallbackEffect">The equivalent backdrop effect used by CPU rendering.</param>
    /// <param name="shaderSource">The complete WGSL source for each pass added by this effect.</param>
    /// <param name="uniformLayout">The named uniform values available to the shader.</param>
    protected WebGPUBackdropShaderLayerEffect(
        BackdropLayerEffect fallbackEffect,
        string shaderSource,
        WebGPUShaderUniformLayout uniformLayout)
        : base(fallbackEffect)
    {
        this.shaderSource = shaderSource;
        this.uniformLayout = uniformLayout;
    }

    /// <summary>
    /// Gets the shared primary program when the first pass is configured.
    /// </summary>
    private WebGPUShaderProgram Program
        => this.program ??= WebGPUShaderProgram.GetOrCreate(this.shaderSource, this.uniformLayout);

    /// <summary>
    /// Configures and adds an invocation of the complete shader source to this effect's ordered pass sequence.
    /// </summary>
    /// <param name="configureUniforms">The action that assigns this pass's named uniform values.</param>
    protected void AddShaderPass(Action<WebGPUShaderUniformBuilder> configureUniforms)
    {
        Guard.NotNull(configureUniforms, nameof(configureUniforms));

        // The base type owns layout, program, and immutable snapshot plumbing. Derived effects only
        // assign the values their WGSL declares; the action executes synchronously and is not retained.
        WebGPUShaderUniformBuilder uniforms = this.uniformLayout.CreateUniforms();
        configureUniforms(uniforms);
        this.AddShaderPass(new WebGPUShaderPass(this.Program, uniforms.Build()));
    }

    /// <summary>
    /// Configures and adds an invocation whose filtered samples use the supplied border modes.
    /// </summary>
    /// <param name="xBorderMode">The wrapping mode applied beyond the horizontal input borders.</param>
    /// <param name="yBorderMode">The wrapping mode applied beyond the vertical input borders.</param>
    /// <param name="configureUniforms">The action that assigns this pass's named uniform values.</param>
    protected void AddShaderPass(
        BorderWrappingMode xBorderMode,
        BorderWrappingMode yBorderMode,
        Action<WebGPUShaderUniformBuilder> configureUniforms)
    {
        Guard.NotNull(configureUniforms, nameof(configureUniforms));

        // Border behavior belongs to an individual pass because one effect may combine shaders with
        // different sampling contracts. Module specialization removes the selection from the pixel loop.
        WebGPUShaderUniformBuilder uniforms = this.uniformLayout.CreateUniforms();
        configureUniforms(uniforms);
        this.AddShaderPass(new WebGPUShaderPass(this.Program, uniforms.Build(), xBorderMode, yBorderMode));
    }

    /// <summary>
    /// Adds internal passes owned by another built-in effect.
    /// </summary>
    private protected void AddShaderPasses(ReadOnlySpan<WebGPUShaderPass> shaderPasses)
    {
        this.EnsureShaderPassCapacity(shaderPasses.Length);
        shaderPasses.CopyTo(this.shaderPasses.AsSpan(this.shaderPassCount));
        this.shaderPassCount += shaderPasses.Length;
    }

    /// <summary>
    /// Gets the ordered internal pass sequence.
    /// </summary>
    internal ReadOnlySpan<WebGPUShaderPass> GetShaderPasses()
        => this.shaderPasses.AsSpan(0, this.shaderPassCount);

    /// <inheritdoc/>
    ReadOnlySpan<WebGPUShaderPass> IWebGPUShaderEffectSource.GetShaderPasses()
        => this.GetShaderPasses();

    /// <summary>
    /// Adds one internal pass to the sequence.
    /// </summary>
    private void AddShaderPass(WebGPUShaderPass shaderPass)
    {
        this.EnsureShaderPassCapacity(1);
        this.shaderPasses[this.shaderPassCount++] = shaderPass;
    }

    /// <summary>
    /// Ensures that the pass storage can append the requested number of entries.
    /// </summary>
    private void EnsureShaderPassCapacity(int additionalCount)
    {
        int requiredCapacity = checked(this.shaderPassCount + additionalCount);
        if (requiredCapacity <= this.shaderPasses.Length)
        {
            return;
        }

        // Four entries cover every built-in sequence in one allocation. Larger internal
        // sequences double geometrically so repeated additions remain amortized O(1).
        int grownCapacity = this.shaderPasses.Length == 0 ? 4 : checked(this.shaderPasses.Length * 2);
        Array.Resize(ref this.shaderPasses, Math.Max(requiredCapacity, grownCapacity));
    }
}
