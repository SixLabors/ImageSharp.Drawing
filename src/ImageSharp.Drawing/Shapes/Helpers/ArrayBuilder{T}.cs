// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace SixLabors.ImageSharp.Drawing.Shapes.Helpers;

/// <summary>
/// A helper type for avoiding allocations while building arrays.
/// </summary>
/// <typeparam name="T">The type of item contained in the array.</typeparam>
internal struct ArrayBuilder<T>
    where T : struct
{
    private const int DefaultCapacity = 4;

    // Starts out null, initialized on first Add.
    private T[]? data;
    private int size;

    /// <summary>
    /// Initializes a new instance of the <see cref="ArrayBuilder{T}"/> struct.
    /// </summary>
    /// <param name="capacity">The initial capacity of the array.</param>
    public ArrayBuilder(int capacity)
        : this()
    {
        if (capacity > 0)
        {
            this.data = new T[capacity];
        }
    }

    /// <summary>
    /// Gets or sets the number of items in the array.
    /// </summary>
    public int Length
    {
        readonly get => this.size;

        set
        {
            if (value > 0)
            {
                this.EnsureCapacity(value);
                this.size = value;
            }
            else
            {
                this.size = 0;
            }
        }
    }

    /// <summary>
    /// Returns a reference to specified element of the array.
    /// </summary>
    /// <param name="index">The index of the element to return.</param>
    /// <returns>The <typeparamref name="T"/>.</returns>
    /// <exception cref="IndexOutOfRangeException">
    /// Thrown when index less than 0 or index greater than or equal to <see cref="Length"/>.
    /// </exception>
    public readonly ref T this[int index]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            DebugGuard.MustBeBetweenOrEqualTo(index, 0, this.size, nameof(index));
            return ref this.data![index];
        }
    }

    /// <summary>
    /// Adds the given item to the array.
    /// </summary>
    /// <param name="item">The item to add.</param>
    public void Add(T item)
    {
        int position = this.size;
        T[]? array = this.data;

        if (array != null && (uint)position < (uint)array.Length)
        {
            this.size = position + 1;
            array[position] = item;
        }
        else
        {
            this.AddWithResize(item);
        }
    }

    // Non-inline from Add to improve its code quality as uncommon path
    [MethodImpl(MethodImplOptions.NoInlining)]
    private void AddWithResize(T item)
    {
        int size = this.size;
        this.Grow(size + 1);
        this.size = size + 1;
        this.data[size] = item;
    }

    /// <summary>
    /// Remove the last item from the array.
    /// </summary>
    public void RemoveLast()
    {
        DebugGuard.MustBeGreaterThan(this.size, 0, nameof(this.size));
        this.size--;
    }

    /// <summary>
    /// Clears the array.
    /// Allocated memory is left intact for future usage.
    /// </summary>
    public void Clear() =>

        // No need to actually clear since we're not allowing reference types.
        this.size = 0;

    private void EnsureCapacity(int min)
    {
        int length = this.data?.Length ?? 0;
        if (length < min)
        {
            this.Grow(min);
        }
    }

    [MemberNotNull(nameof(this.data))]
    private void Grow(int capacity)
    {
        // Same expansion algorithm as List<T>.
        int length = this.data?.Length ?? 0;
        int newCapacity = length == 0 ? DefaultCapacity : length * 2;
        if ((uint)newCapacity > Array.MaxLength)
        {
            newCapacity = Array.MaxLength;
        }

        if (newCapacity < capacity)
        {
            newCapacity = capacity;
        }

        T[] array = new T[newCapacity];

        if (this.size > 0)
        {
            Array.Copy(this.data!, array, this.size);
        }

        this.data = array;
    }
}
