// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Identifies a value type that can be supplied to a WebGPU layer-effect shader.
/// </summary>
public enum WebGPUShaderUniformType
{
    /// <summary>
    /// A 32-bit floating-point value.
    /// </summary>
    Float32,

    /// <summary>
    /// A signed 32-bit integer value.
    /// </summary>
    Int32,

    /// <summary>
    /// An unsigned 32-bit integer value.
    /// </summary>
    UInt32,

    /// <summary>
    /// A two-component 32-bit floating-point vector.
    /// </summary>
    Vector2,

    /// <summary>
    /// A three-component 32-bit floating-point vector.
    /// </summary>
    Vector3,

    /// <summary>
    /// A four-component 32-bit floating-point vector.
    /// </summary>
    Vector4,

    /// <summary>
    /// A four-by-four 32-bit floating-point matrix.
    /// </summary>
    Matrix4x4
}
