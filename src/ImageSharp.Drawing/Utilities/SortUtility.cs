// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Runtime.CompilerServices;

namespace SixLabors.ImageSharp.Drawing.Utilities;

/// <summary>
/// Optimized quick sort implementation for Span{float} input
/// </summary>
internal static partial class SortUtility
{
    /// <summary>
    /// Sorts the elements of <paramref name="data"/> in ascending order
    /// </summary>
    /// <param name="data">The items to sort</param>
    public static void Sort(Span<float> data)
    {
        if (data.Length < 2)
        {
            return;
        }

        if (data.Length == 2)
        {
            if (data[0] > data[1])
            {
                Swap(ref data[0], ref data[1]);
            }

            return;
        }

        Sort(ref data[0], 0, data.Length - 1);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Swap(ref float left, ref float right)
    {
        float tmp = left;
        left = right;
        right = tmp;
    }

    private static void Sort(ref float data0, int lo, int hi)
    {
        if (lo < hi)
        {
            int p = Partition(ref data0, lo, hi);
            Sort(ref data0, lo, p);
            Sort(ref data0, p + 1, hi);
        }
    }

    private static int Partition(ref float data0, int lo, int hi)
    {
        float pivot = Unsafe.Add(ref data0, lo);
        int i = lo - 1;
        int j = hi + 1;
        while (true)
        {
            do
            {
                i = i + 1;
            }
            while (Unsafe.Add(ref data0, i) < pivot && i < hi);

            do
            {
                j = j - 1;
            }
            while (Unsafe.Add(ref data0, j) > pivot && j > lo);

            if (i >= j)
            {
                return j;
            }

            Swap(ref Unsafe.Add(ref data0, i), ref Unsafe.Add(ref data0, j));
        }
    }

    /// <summary>
    /// Sorts the elements of <paramref name="values"/> in ascending order
    /// </summary>
    /// <typeparam name="T">The type of element.</typeparam>
    /// <param name="keys">The items to sort on</param>
    /// <param name="values">The items to sort</param>
    /// <exception cref="ArgumentException">Both spans must be the same length.</exception>
    public static void Sort<T>(Span<float> keys, Span<T> values)
    {
        if (keys.Length != values.Length)
        {
            throw new ArgumentException("Both spans must be the same length.");
        }

        if (keys.Length < 2)
        {
            return;
        }

        if (keys.Length == 2)
        {
            if (keys[0] > keys[1])
            {
                Swap(ref keys[0], ref keys[1]);
                Swap(ref values[0], ref values[1]);
            }

            return;
        }

        KeyValueSort<T>.Sort(keys, values);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Swap<T>(ref T left, ref T right)
    {
        T tmp = left;
        left = right;
        right = tmp;
    }

    /// <summary>
    /// Sorts the elements of <paramref name="keys"/> in ascending order, and swapping items in <paramref name="values1"/> and <paramref name="values2"/> in sequence with them.
    /// </summary>
    /// <typeparam name="T1">The type of the first value elements.</typeparam>
    /// <typeparam name="T2">The type of the second value elements.</typeparam>
    /// <param name="keys">The items to sort on</param>
    /// <param name="values1">The set of items to sort</param>
    /// <param name="values2">The 2nd set of items to sort</param>
    /// <exception cref="ArgumentException">Both spans must be the same length.</exception>
    public static void Sort<T1, T2>(Span<float> keys, Span<T1> values1, Span<T2> values2)
    {
        if (keys.Length != values1.Length)
        {
            throw new ArgumentException("Both spans must be the same length.");
        }

        if (keys.Length != values2.Length)
        {
            throw new ArgumentException("Both spans must be the same length.");
        }

        if (keys.Length < 2)
        {
            return;
        }

        if (keys.Length == 2)
        {
            if (keys[0] > keys[1])
            {
                Swap(ref keys[0], ref keys[1]);
                Swap(ref values1[0], ref values1[1]);
                Swap(ref values2[0], ref values2[1]);
            }

            return;
        }

        Sort(ref keys[0], 0, keys.Length - 1, ref values1[0], ref values2[0]);
    }

    private static void Sort<T1, T2>(ref float data0, int lo, int hi, ref T1 dataToSort1, ref T2 dataToSort2)
    {
        if (lo < hi)
        {
            int p = Partition(ref data0, lo, hi, ref dataToSort1, ref dataToSort2);
            Sort(ref data0, lo, p, ref dataToSort1, ref dataToSort2);
            Sort(ref data0, p + 1, hi, ref dataToSort1, ref dataToSort2);
        }
    }

    private static int Partition<T1, T2>(ref float data0, int lo, int hi, ref T1 dataToSort1, ref T2 dataToSort2)
    {
        float pivot = Unsafe.Add(ref data0, lo);
        int i = lo - 1;
        int j = hi + 1;
        while (true)
        {
            do
            {
                i = i + 1;
            }
            while (Unsafe.Add(ref data0, i) < pivot && i < hi);

            do
            {
                j = j - 1;
            }
            while (Unsafe.Add(ref data0, j) > pivot && j > lo);

            if (i >= j)
            {
                return j;
            }

            Swap(ref Unsafe.Add(ref data0, i), ref Unsafe.Add(ref data0, j));
            Swap(ref Unsafe.Add(ref dataToSort1, i), ref Unsafe.Add(ref dataToSort1, j));
            Swap(ref Unsafe.Add(ref dataToSort2, i), ref Unsafe.Add(ref dataToSort2, j));
        }
    }
}
