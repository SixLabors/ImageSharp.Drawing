// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Runtime.InteropServices;
using System.Text;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends.Native;

internal unsafe partial struct WGPUStringView
{
    /// <summary>
    /// Initializes a new instance of the <see cref="WGPUStringView"/> struct over a caller-owned UTF-8 byte sequence.
    /// </summary>
    /// <param name="data">The first UTF-8 byte, or <see langword="null"/> for a null string view.</param>
    /// <param name="length">The byte length, or <see cref="nuint.MaxValue"/> when <paramref name="data"/> is null-terminated.</param>
    public WGPUStringView(byte* data, nuint length)
    {
        this.data = (sbyte*)data;
        this.length = length;
    }

    /// <summary>
    /// Creates a null-terminated string view over caller-owned UTF-8 bytes.
    /// </summary>
    /// <param name="data">The first UTF-8 byte, or <see langword="null"/> for a null string view.</param>
    public static implicit operator WGPUStringView(byte* data)
        => new(data, nuint.MaxValue);

    /// <summary>
    /// Decodes the UTF-8 bytes represented by this view.
    /// </summary>
    /// <returns>The decoded text, or an empty string for a null view.</returns>
    public readonly string ToManagedString()
    {
        if (this.data is null)
        {
            return string.Empty;
        }

        // WebGPU uses SIZE_MAX to distinguish null-terminated input from an explicitly sized view.
        return this.length == nuint.MaxValue
            ? Marshal.PtrToStringUTF8((nint)this.data) ?? string.Empty
            : Encoding.UTF8.GetString((byte*)this.data, checked((int)this.length));
    }
}
