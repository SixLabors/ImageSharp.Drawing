// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace SixLabors.ImageSharp.Drawing.Helpers;

/// <summary>
/// A buffer of unmanaged values in an array rented from the shared <see cref="byte"/> pool. Every element type
/// rents from that one pool, so the pool caches one set of arrays for the library instead of one set for
/// each element type. The values start undefined: the pool hands out arrays it has used before, and a new
/// array is allocated without clearing.
/// <para>
/// The buffer stores the array and its length, not a span, so a struct can hold it as a field. A copy shares
/// the array, so exactly one owner disposes it.
/// </para>
/// </summary>
/// <typeparam name="T">The element type.</typeparam>
internal struct PooledBuffer<T> : IDisposable
    where T : unmanaged
{
    /// <summary>
    /// The rented array, or <see langword="null"/> once the buffer is returned or when it was never rented.
    /// </summary>
    private byte[]? rented;

    /// <summary>
    /// The number of elements.
    /// </summary>
    private int length;

    /// <summary>
    /// Initializes a new instance of the <see cref="PooledBuffer{T}"/> struct.
    /// </summary>
    /// <param name="length">The number of elements.</param>
    public PooledBuffer(int length)
    {
        this.rented = ArrayPool<byte>.Shared.Rent(length * Unsafe.SizeOf<T>());
        this.length = length;
    }

    /// <summary>
    /// Gets the elements. The span is empty when the buffer was never rented or is returned.
    /// </summary>
    public readonly Span<T> Span => MemoryMarshal.Cast<byte, T>(this.rented.AsSpan(0, this.length * Unsafe.SizeOf<T>()));

    /// <summary>
    /// Returns the array to the shared pool. The buffer is empty afterwards, so a caller that keeps it cannot
    /// write into an array that another caller now owns.
    /// </summary>
    public void Dispose()
    {
        byte[]? toReturn = this.rented;
        this = default;
        if (toReturn is not null)
        {
            ArrayPool<byte>.Shared.Return(toReturn);
        }
    }
}
