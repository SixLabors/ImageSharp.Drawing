// Copyright (c) Six Labors and contributors.
// Licensed under the GNU Affero General Public License, Version 3.

using System;
using System.Runtime.CompilerServices;

namespace SixLabors.ImageSharp.Drawing
{
    /// <summary>
    /// Optimized quick sort implementation for Span{float} input
    /// </summary>
    internal static class QuickSort
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
        /// Sorts the elements of <paramref name="data"/> in ascending order
        /// </summary>
        /// <param name="sortable">The items to sort on</param>
        /// <param name="data">The items to sort</param>
        public static void Sort<T>(Span<float> sortable, Span<T> data)
        {
            if (sortable.Length != data.Length)
            {
                throw new Exception("both spans must be the same length");
            }

            if (sortable.Length < 2)
            {
                return;
            }

            if (sortable.Length == 2)
            {
                if (sortable[0] > sortable[1])
                {
                    Swap(ref sortable[0], ref sortable[1]);
                    Swap(ref data[0], ref data[1]);
                }

                return;
            }

            Sort(ref sortable[0], 0, sortable.Length - 1, ref data[0]);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void Swap<T>(ref T left, ref T right)
        {
            T tmp = left;
            left = right;
            right = tmp;
        }

        private static void Sort<T>(ref float data0, int lo, int hi, ref T dataToSort)
        {
            if (lo < hi)
            {
                int p = Partition(ref data0, lo, hi, ref dataToSort);
                Sort(ref data0, lo, p, ref dataToSort);
                Sort(ref data0, p + 1, hi, ref dataToSort);
            }
        }

        private static int Partition<T>(ref float data0, int lo, int hi, ref T dataToSort)
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
                Swap(ref Unsafe.Add(ref dataToSort, i), ref Unsafe.Add(ref dataToSort, j));
            }
        }

        /// <summary>
        /// Sorts the elements of <paramref name="sortable"/> in ascending order, and swapping items in <paramref name="data1"/> and <paramref name="data2"/> in sequance with them.
        /// </summary>
        /// <param name="sortable">The items to sort on</param>
        /// <param name="data1">The set of items to sort</param>
        /// <param name="data2">The 2nd set of items to sort</param>
        public static void Sort<T1, T2>(Span<float> sortable, Span<T1> data1, Span<T2> data2)
        {
            if (sortable.Length != data1.Length)
            {
                throw new Exception("both spans must be the same length");
            }

            if (sortable.Length != data2.Length)
            {
                throw new Exception("both spans must be the same length");
            }

            if (sortable.Length < 2)
            {
                return;
            }

            if (sortable.Length == 2)
            {
                if (sortable[0] > sortable[1])
                {
                    Swap(ref sortable[0], ref sortable[1]);
                    Swap(ref data1[0], ref data1[1]);
                    Swap(ref data2[0], ref data2[1]);
                }

                return;
            }

            Sort(ref sortable[0], 0, sortable.Length - 1, ref data1[0], ref data2[0]);
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
}
