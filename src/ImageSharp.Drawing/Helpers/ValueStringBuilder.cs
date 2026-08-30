// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Buffers;
using System.Runtime.CompilerServices;

namespace SixLabors.ImageSharp.Drawing.Helpers;

/// <summary>
/// Builds a string in a buffer the caller owns. The builder starts in that buffer, which is small enough
/// to stack allocate, and moves to a pooled array only when the text outgrows it, so the common case
/// allocates nothing and the worst case rents rather than allocates.
/// <para>
/// The builder owns the rented array, so every instance must be disposed. <see cref="ToString"/> disposes
/// it, so a builder finished with <see cref="ToString"/> needs no further call.
/// </para>
/// </summary>
internal ref struct ValueStringBuilder
{
    private char[]? pooled;
    private Span<char> characters;
    private int position;

    /// <summary>
    /// Initializes a new instance of the <see cref="ValueStringBuilder"/> struct.
    /// </summary>
    /// <param name="initialBuffer">The buffer to build in until the text outgrows it.</param>
    public ValueStringBuilder(Span<char> initialBuffer)
    {
        this.pooled = null;
        this.characters = initialBuffer;
        this.position = 0;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ValueStringBuilder"/> struct that builds in a pooled
    /// array from the start.
    /// </summary>
    /// <param name="initialCapacity">The number of characters to rent room for.</param>
    public ValueStringBuilder(int initialCapacity)
    {
        this.pooled = ArrayPool<char>.Shared.Rent(initialCapacity);
        this.characters = this.pooled;
        this.position = 0;
    }

    /// <summary>
    /// Gets the number of characters written so far.
    /// </summary>
    public int Length => this.position;

    /// <summary>
    /// Gets the number of characters the builder holds before it has to grow.
    /// </summary>
    public int Capacity => this.characters.Length;

    /// <summary>
    /// Gets a reference to the character at the given index.
    /// </summary>
    /// <param name="index">The index of the character.</param>
    /// <returns>A reference to the character.</returns>
    public ref char this[int index] => ref this.characters[index];

    /// <summary>
    /// Appends a single character.
    /// </summary>
    /// <param name="value">The character to append.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Append(char value)
    {
        int pos = this.position;
        Span<char> destination = this.characters;
        if ((uint)pos < (uint)destination.Length)
        {
            destination[pos] = value;
            this.position = pos + 1;
            return;
        }

        this.Grow(1);
        this.characters[this.position++] = value;
    }

    /// <summary>
    /// Appends a run of characters.
    /// </summary>
    /// <param name="value">The characters to append.</param>
    public void Append(scoped ReadOnlySpan<char> value)
    {
        if (this.position > this.characters.Length - value.Length)
        {
            this.Grow(value.Length);
        }

        value.CopyTo(this.characters[this.position..]);
        this.position += value.Length;
    }

    /// <summary>
    /// Reserves a run of characters at the end of the text and returns it for the caller to write into.
    /// </summary>
    /// <param name="length">The number of characters to reserve.</param>
    /// <returns>The reserved characters, whose contents are undefined until the caller writes them.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Span<char> AppendSpan(int length)
    {
        int start = this.position;
        if (start > this.characters.Length - length)
        {
            this.Grow(length);
        }

        this.position = start + length;
        return this.characters.Slice(start, length);
    }

    /// <summary>
    /// Grows the buffer so it holds at least the given number of characters.
    /// </summary>
    /// <param name="capacity">The number of characters the buffer must hold.</param>
    public void EnsureCapacity(int capacity)
    {
        if ((uint)capacity > (uint)this.characters.Length)
        {
            this.Grow(capacity - this.position);
        }
    }

    /// <summary>
    /// Returns the text written so far, without copying it.
    /// </summary>
    /// <returns>The text written so far.</returns>
    public readonly ReadOnlySpan<char> AsSpan() => this.characters[..this.position];

    /// <summary>
    /// Returns the text written so far as a string and disposes the builder.
    /// </summary>
    /// <returns>The text written so far.</returns>
    public override string ToString()
    {
        string result = this.characters[..this.position].ToString();
        this.Dispose();
        return result;
    }

    /// <summary>
    /// Returns the pooled array, if the builder rented one.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Dispose()
    {
        char[]? toReturn = this.pooled;

        // Clearing the whole builder stops a caller who keeps using a disposed instance from writing into
        // an array another caller now owns.
        this = default;
        if (toReturn is not null)
        {
            ArrayPool<char>.Shared.Return(toReturn);
        }
    }

    /// <summary>
    /// Moves the text into a pooled array large enough for the characters already written plus the ones
    /// about to be, doubling where that is larger so a run of appends does not grow every time.
    /// </summary>
    /// <param name="additionalCapacity">The number of characters needed beyond those already written.</param>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private void Grow(int additionalCapacity)
    {
        int newCapacity = (int)Math.Max(
            (uint)(this.position + additionalCapacity),
            Math.Min((uint)this.characters.Length * 2, (uint)Array.MaxLength));

        char[] rented = ArrayPool<char>.Shared.Rent(newCapacity);
        this.characters[..this.position].CopyTo(rented);

        char[]? toReturn = this.pooled;
        this.characters = this.pooled = rented;
        if (toReturn is not null)
        {
            ArrayPool<char>.Shared.Return(toReturn);
        }
    }
}
