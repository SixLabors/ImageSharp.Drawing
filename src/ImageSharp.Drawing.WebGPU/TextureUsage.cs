// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Describes how a WebGPU texture may be used.
/// </summary>
[Flags]
internal enum TextureUsage : ulong
{
    /// <summary>
    /// The texture has no permitted usage.
    /// </summary>
    None = 0,

    /// <summary>
    /// The texture can be the source of a copy operation.
    /// </summary>
    CopySrc = 1,

    /// <summary>
    /// The texture can be the destination of a copy operation.
    /// </summary>
    CopyDst = 2,

    /// <summary>
    /// The texture can be sampled by a shader.
    /// </summary>
    TextureBinding = 4,

    /// <summary>
    /// The texture can be bound for shader storage access.
    /// </summary>
    StorageBinding = 8,

    /// <summary>
    /// The texture can be used as a render-pass attachment.
    /// </summary>
    RenderAttachment = 16,

    /// <summary>
    /// The texture can be used as a transient attachment.
    /// </summary>
    TransientAttachment = 32
}
