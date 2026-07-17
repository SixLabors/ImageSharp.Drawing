// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Describes how a WebGPU buffer may be used.
/// </summary>
[Flags]
internal enum BufferUsage : ulong
{
    /// <summary>
    /// The buffer has no permitted usage.
    /// </summary>
    None = 0,

    /// <summary>
    /// The buffer can be mapped for CPU reads.
    /// </summary>
    MapRead = 1,

    /// <summary>
    /// The buffer can be mapped for CPU writes.
    /// </summary>
    MapWrite = 2,

    /// <summary>
    /// The buffer can be the source of a copy operation.
    /// </summary>
    CopySrc = 4,

    /// <summary>
    /// The buffer can be the destination of a copy operation.
    /// </summary>
    CopyDst = 8,

    /// <summary>
    /// The buffer can supply index data.
    /// </summary>
    Index = 16,

    /// <summary>
    /// The buffer can supply vertex data.
    /// </summary>
    Vertex = 32,

    /// <summary>
    /// The buffer can supply uniform data.
    /// </summary>
    Uniform = 64,

    /// <summary>
    /// The buffer can be bound as storage.
    /// </summary>
    Storage = 128,

    /// <summary>
    /// The buffer can supply indirect command arguments.
    /// </summary>
    Indirect = 256,

    /// <summary>
    /// The buffer can receive resolved query results.
    /// </summary>
    QueryResolve = 512
}
