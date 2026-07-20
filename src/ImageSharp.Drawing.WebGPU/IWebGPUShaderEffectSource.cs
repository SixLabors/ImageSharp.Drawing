// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Exposes the internal pass sequence owned by a public shader-effect base class.
/// </summary>
internal interface IWebGPUShaderEffectSource
{
    /// <summary>
    /// Gets the ordered shader passes.
    /// </summary>
    /// <returns>The passes in execution order.</returns>
    public ReadOnlySpan<WebGPUShaderPass> GetShaderPasses();
}
