// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Buffers;
using System.Runtime.CompilerServices;

namespace SixLabors.ImageSharp.Drawing.Helpers;

/// <summary>
/// Builds a list of values in a buffer the caller owns. The builder starts in that buffer, which is small
/// enough to stack allocate, and moves to a pooled array only when the list outgrows it, so the common case
/// allocates nothing and the worst case rents rather than allocates.
/// <para>
/// The builder owns the rented array, so every instance must be disposed.
/// </para>
/// </summary>
/// <typeparam name="T">The type of the values.</typeparam>
internal ref struct ValueListBuilder<T>
{
    private T[]? pooled;
    private Span<T> values;
    private int position;

    /// <summary>
    /// Initializes a new instance of the <see cref="ValueListBuilder{T}"/> struct.
    /// </summary>
    /// <param name="initialBuffer">The buffer to build in until the list outgrows it.</param>
    public ValueListBuilder(Span<T> initialBuffer)
    {
        this.pooled = null;
        this.values = initialBuffer;
        this.position = 0;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ValueListBuilder{T}"/> struct that builds in a pooled
    /// array from the start.
    /// </summary>
    /// <param name="initialCapacity">The number of values to rent room for.</param>
    public ValueListBuilder(int initialCapacity)
    {
        this.pooled = ArrayPool<T>.Shared.Rent(initialCapacity);
        this.values = this.pooled;
        this.position = 0;
    }

    /// <summary>
    /// Gets the number of values written so far.
    /// </summary>
    public int Length => this.position;

    /// <summary>
    /// Gets the number of values the builder holds before it has to grow.
    /// </summary>
    public int Capacity => this.values.Length;

    /// <summary>
    /// Gets a reference to the value at the given index.
    /// </summary>
    /// <param name="index">The index of the value, below <see cref="Length"/>.</param>
    /// <returns>A reference to the value.</returns>
    public ref T this[int index] => ref this.values[index];

    /// <summary>
    /// Appends a single value.
    /// </summary>
    /// <param name="value">The value to append.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Append(T value)
    {
        int pos = this.position;
        Span<T> destination = this.values;
        if ((uint)pos < (uint)destination.Length)
        {
            destination[pos] = value;
            this.position = pos + 1;
            return;
        }

        this.Grow(1);
        this.values[this.position++] = value;
    }

    /// <summary>
    /// Appends a run of values.
    /// </summary>
    /// <param name="source">The values to append.</param>
    public void Append(scoped ReadOnlySpan<T> source)
    {
        if (this.position > this.values.Length - source.Length)
        {
            this.Grow(source.Length);
        }

        source.CopyTo(this.values[this.position..]);
        this.position += source.Length;
    }

    /// <summary>
    /// Reserves a run of values at the end of the list and returns it for the caller to write into.
    /// </summary>
    /// <param name="length">The number of values to reserve.</param>
    /// <returns>The reserved values, whose contents are undefined until the caller writes them.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Span<T> AppendSpan(int length)
    {
        int start = this.position;
        if (start > this.values.Length - length)
        {
            this.Grow(length);
        }

        this.position = start + length;
        return this.values.Slice(start, length);
    }

    /// <summary>
    /// Returns the values written so far, without copying them.
    /// </summary>
    /// <returns>The values written so far.</returns>
    public readonly Span<T> AsSpan() => this.values[..this.position];

    /// <summary>
    /// Returns the pooled array, if the builder rented one. An array of references is cleared first so
    /// the pool does not keep them alive.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Dispose()
    {
        T[]? toReturn = this.pooled;

        // Clearing the whole builder stops a caller who keeps using a disposed instance from writing into
        // an array another caller now owns.
        this = default;
        if (toReturn is not null)
        {
            ArrayPool<T>.Shared.Return(toReturn, RuntimeHelpers.IsReferenceOrContainsReferences<T>());
        }
    }

    /// <summary>
    /// Moves the list into a pooled array large enough for the values already written plus the ones
    /// about to be, doubling where that is larger so a run of appends does not grow every time.
    /// </summary>
    /// <param name="additionalCapacity">The number of values needed beyond those already written.</param>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private void Grow(int additionalCapacity)
    {
        int newCapacity = (int)Math.Max(
            (uint)(this.position + additionalCapacity),
            Math.Min((uint)this.values.Length * 2, (uint)Array.MaxLength));

        T[] rented = ArrayPool<T>.Shared.Rent(newCapacity);
        this.values[..this.position].CopyTo(rented);

        T[]? toReturn = this.pooled;
        this.values = this.pooled = rented;
        if (toReturn is not null)
        {
            ArrayPool<T>.Shared.Return(toReturn, RuntimeHelpers.IsReferenceOrContainsReferences<T>());
        }
    }
}
