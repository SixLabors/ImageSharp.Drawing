// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Identifies the shader stages that can access a binding.
/// </summary>
[Flags]
internal enum ShaderStage : ulong
{
    /// <summary>
    /// No shader stage can access the binding.
    /// </summary>
    None = 0,

    /// <summary>
    /// The vertex stage can access the binding.
    /// </summary>
    Vertex = 1,

    /// <summary>
    /// The fragment stage can access the binding.
    /// </summary>
    Fragment = 2,

    /// <summary>
    /// The compute stage can access the binding.
    /// </summary>
    Compute = 4
}
