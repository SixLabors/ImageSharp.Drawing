// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Identifies a layer effect that supplies a native WebGPU implementation and a CPU fallback.
/// </summary>
/// <remarks>
/// Derive from <see cref="WebGPUShaderLayerEffect"/> or <see cref="WebGPUBackdropShaderLayerEffect"/> to implement
/// this contract. Pass an instance to <see cref="WebGPUDeviceContext.Precompile(IWebGPUShaderEffect)"/> to compile
/// its pipelines before first use.
/// </remarks>
public interface IWebGPUShaderEffect
{
}
