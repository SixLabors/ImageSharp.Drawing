// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Buffers.Binary;
using System.Numerics;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Builds a set of named values for a <see cref="WebGPUShaderUniformLayout"/>.
/// </summary>
public sealed class WebGPUShaderUniformBuilder
{
    private readonly WebGPUShaderUniformLayout layout;
    private readonly byte[] data;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebGPUShaderUniformBuilder"/> class.
    /// </summary>
    /// <param name="layout">The layout that defines the accepted names and value types.</param>
    internal WebGPUShaderUniformBuilder(WebGPUShaderUniformLayout layout)
    {
        this.layout = layout;
        this.data = new byte[layout.ByteLength];
    }

    /// <summary>
    /// Sets a named 32-bit floating-point value.
    /// </summary>
    /// <param name="name">The declared member name.</param>
    /// <param name="value">The value to set.</param>
    public void SetFloat32(string name, float value)
    {
        WebGPUShaderUniformMember member = this.layout.GetMember(name, WebGPUShaderUniformType.Float32, isArray: false);
        WriteFloat32(this.data, member.Offset, value);
    }

    /// <summary>
    /// Sets a named signed 32-bit integer value.
    /// </summary>
    /// <param name="name">The declared member name.</param>
    /// <param name="value">The value to set.</param>
    public void SetInt32(string name, int value)
    {
        WebGPUShaderUniformMember member = this.layout.GetMember(name, WebGPUShaderUniformType.Int32, isArray: false);
        BinaryPrimitives.WriteInt32LittleEndian(this.data.AsSpan(member.Offset, sizeof(int)), value);
    }

    /// <summary>
    /// Sets a named unsigned 32-bit integer value.
    /// </summary>
    /// <param name="name">The declared member name.</param>
    /// <param name="value">The value to set.</param>
    public void SetUInt32(string name, uint value)
    {
        WebGPUShaderUniformMember member = this.layout.GetMember(name, WebGPUShaderUniformType.UInt32, isArray: false);
        BinaryPrimitives.WriteUInt32LittleEndian(this.data.AsSpan(member.Offset, sizeof(uint)), value);
    }

    /// <summary>
    /// Sets a named two-component floating-point vector.
    /// </summary>
    /// <param name="name">The declared member name.</param>
    /// <param name="value">The value to set.</param>
    public void SetVector2(string name, Vector2 value)
    {
        WebGPUShaderUniformMember member = this.layout.GetMember(name, WebGPUShaderUniformType.Vector2, isArray: false);
        WriteVector2(this.data, member.Offset, value);
    }

    /// <summary>
    /// Sets a named three-component floating-point vector.
    /// </summary>
    /// <param name="name">The declared member name.</param>
    /// <param name="value">The value to set.</param>
    public void SetVector3(string name, Vector3 value)
    {
        WebGPUShaderUniformMember member = this.layout.GetMember(name, WebGPUShaderUniformType.Vector3, isArray: false);
        WriteVector3(this.data, member.Offset, value);
    }

    /// <summary>
    /// Sets a named four-component floating-point vector.
    /// </summary>
    /// <param name="name">The declared member name.</param>
    /// <param name="value">The value to set.</param>
    public void SetVector4(string name, Vector4 value)
    {
        WebGPUShaderUniformMember member = this.layout.GetMember(name, WebGPUShaderUniformType.Vector4, isArray: false);
        WriteVector4(this.data, member.Offset, value);
    }

    /// <summary>
    /// Sets a named four-by-four floating-point matrix.
    /// </summary>
    /// <param name="name">The declared member name.</param>
    /// <param name="value">The value to set.</param>
    public void SetMatrix4x4(string name, Matrix4x4 value)
    {
        WebGPUShaderUniformMember member = this.layout.GetMember(name, WebGPUShaderUniformType.Matrix4x4, isArray: false);
        WriteMatrix4x4(this.data, member.Offset, value);
    }

    /// <summary>
    /// Sets a named fixed-size array of 32-bit floating-point values.
    /// </summary>
    /// <param name="name">The declared member name.</param>
    /// <param name="values">The values to set.</param>
    public void SetFloat32Array(string name, ReadOnlySpan<float> values)
    {
        WebGPUShaderUniformMember member = this.GetArrayMember(name, WebGPUShaderUniformType.Float32, values.Length);
        for (int i = 0; i < values.Length; i++)
        {
            WriteFloat32(this.data, member.Offset + (i * member.Stride), values[i]);
        }
    }

    /// <summary>
    /// Sets a named fixed-size array of signed 32-bit integer values.
    /// </summary>
    /// <param name="name">The declared member name.</param>
    /// <param name="values">The values to set.</param>
    public void SetInt32Array(string name, ReadOnlySpan<int> values)
    {
        WebGPUShaderUniformMember member = this.GetArrayMember(name, WebGPUShaderUniformType.Int32, values.Length);
        for (int i = 0; i < values.Length; i++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(this.data.AsSpan(member.Offset + (i * member.Stride), sizeof(int)), values[i]);
        }
    }

    /// <summary>
    /// Sets a named fixed-size array of unsigned 32-bit integer values.
    /// </summary>
    /// <param name="name">The declared member name.</param>
    /// <param name="values">The values to set.</param>
    public void SetUInt32Array(string name, ReadOnlySpan<uint> values)
    {
        WebGPUShaderUniformMember member = this.GetArrayMember(name, WebGPUShaderUniformType.UInt32, values.Length);
        for (int i = 0; i < values.Length; i++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(this.data.AsSpan(member.Offset + (i * member.Stride), sizeof(uint)), values[i]);
        }
    }

    /// <summary>
    /// Sets a named fixed-size array of two-component floating-point vectors.
    /// </summary>
    /// <param name="name">The declared member name.</param>
    /// <param name="values">The values to set.</param>
    public void SetVector2Array(string name, ReadOnlySpan<Vector2> values)
    {
        WebGPUShaderUniformMember member = this.GetArrayMember(name, WebGPUShaderUniformType.Vector2, values.Length);
        for (int i = 0; i < values.Length; i++)
        {
            WriteVector2(this.data, member.Offset + (i * member.Stride), values[i]);
        }
    }

    /// <summary>
    /// Sets a named fixed-size array of three-component floating-point vectors.
    /// </summary>
    /// <param name="name">The declared member name.</param>
    /// <param name="values">The values to set.</param>
    public void SetVector3Array(string name, ReadOnlySpan<Vector3> values)
    {
        WebGPUShaderUniformMember member = this.GetArrayMember(name, WebGPUShaderUniformType.Vector3, values.Length);
        for (int i = 0; i < values.Length; i++)
        {
            WriteVector3(this.data, member.Offset + (i * member.Stride), values[i]);
        }
    }

    /// <summary>
    /// Sets a named fixed-size array of four-component floating-point vectors.
    /// </summary>
    /// <param name="name">The declared member name.</param>
    /// <param name="values">The values to set.</param>
    public void SetVector4Array(string name, ReadOnlySpan<Vector4> values)
    {
        WebGPUShaderUniformMember member = this.GetArrayMember(name, WebGPUShaderUniformType.Vector4, values.Length);
        for (int i = 0; i < values.Length; i++)
        {
            WriteVector4(this.data, member.Offset + (i * member.Stride), values[i]);
        }
    }

    /// <summary>
    /// Sets a named fixed-size array of four-by-four floating-point matrices.
    /// </summary>
    /// <param name="name">The declared member name.</param>
    /// <param name="values">The values to set.</param>
    public void SetMatrix4x4Array(string name, ReadOnlySpan<Matrix4x4> values)
    {
        WebGPUShaderUniformMember member = this.GetArrayMember(name, WebGPUShaderUniformType.Matrix4x4, values.Length);
        for (int i = 0; i < values.Length; i++)
        {
            WriteMatrix4x4(this.data, member.Offset + (i * member.Stride), values[i]);
        }
    }

    /// <summary>
    /// Creates an immutable snapshot of the current values.
    /// </summary>
    /// <returns>The immutable uniform values.</returns>
    /// <remarks>The snapshot copies the packed bytes so subsequent builder changes cannot mutate a retained scene.</remarks>
    internal WebGPUShaderUniforms Build() => new(this.layout, (byte[])this.data.Clone());

    /// <summary>
    /// Resolves an array member and verifies that the caller supplied its complete fixed-size value.
    /// </summary>
    /// <param name="name">The declared member name.</param>
    /// <param name="type">The required element type.</param>
    /// <param name="elementCount">The supplied element count.</param>
    /// <returns>The matching packed member.</returns>
    private WebGPUShaderUniformMember GetArrayMember(string name, WebGPUShaderUniformType type, int elementCount)
    {
        WebGPUShaderUniformMember member = this.layout.GetMember(name, type, isArray: true);
        if (member.Uniform.ElementCount != elementCount)
        {
            throw new ArgumentException($"The uniform '{name}' requires exactly {member.Uniform.ElementCount} elements.", nameof(elementCount));
        }

        return member;
    }

    /// <summary>
    /// Writes one floating-point value using WGSL's little-endian host-shareable representation.
    /// </summary>
    private static void WriteFloat32(Span<byte> destination, int offset, float value)
        => BinaryPrimitives.WriteInt32LittleEndian(destination.Slice(offset, sizeof(float)), BitConverter.SingleToInt32Bits(value));

    /// <summary>
    /// Writes one two-component vector at its calculated member offset.
    /// </summary>
    private static void WriteVector2(Span<byte> destination, int offset, Vector2 value)
    {
        WriteFloat32(destination, offset, value.X);
        WriteFloat32(destination, offset + 4, value.Y);
    }

    /// <summary>
    /// Writes one three-component vector without overwriting its trailing layout padding.
    /// </summary>
    private static void WriteVector3(Span<byte> destination, int offset, Vector3 value)
    {
        WriteFloat32(destination, offset, value.X);
        WriteFloat32(destination, offset + 4, value.Y);
        WriteFloat32(destination, offset + 8, value.Z);
    }

    /// <summary>
    /// Writes one four-component vector at its calculated member offset.
    /// </summary>
    private static void WriteVector4(Span<byte> destination, int offset, Vector4 value)
    {
        WriteFloat32(destination, offset, value.X);
        WriteFloat32(destination, offset + 4, value.Y);
        WriteFloat32(destination, offset + 8, value.Z);
        WriteFloat32(destination, offset + 12, value.W);
    }

    /// <summary>
    /// Writes a row-addressed <see cref="Matrix4x4"/> into WGSL's column-major matrix representation.
    /// </summary>
    private static void WriteMatrix4x4(Span<byte> destination, int offset, Matrix4x4 value)
    {
        // WGSL matrices are stored column-major. Writing each System.Numerics row/column element
        // explicitly preserves the matrix's M[row][column] values across the representation change.
        WriteVector4(destination, offset, new Vector4(value.M11, value.M21, value.M31, value.M41));
        WriteVector4(destination, offset + 16, new Vector4(value.M12, value.M22, value.M32, value.M42));
        WriteVector4(destination, offset + 32, new Vector4(value.M13, value.M23, value.M33, value.M43));
        WriteVector4(destination, offset + 48, new Vector4(value.M14, value.M24, value.M34, value.M44));
    }
}
