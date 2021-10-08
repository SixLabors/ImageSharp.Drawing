// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Runtime.CompilerServices;

namespace SixLabors.ImageSharp.Drawing.Utilities
{
    internal static partial class SortUtility
    {
        // Adapted from:
        // https://github.com/dotnet/runtime/blob/master/src/libraries/System.Private.CoreLib/src/System/Collections/Generic/ArraySortHelper.cs
        // If targeting .NET 5, we can call span based sort, but probably not worth it only for that API.
        private static class KeyValueSort<TValue>
        {
            public static void Sort(Span<float> keys, Span<TValue> values) => IntrospectiveSort(keys, values);

            private static void SwapIfGreaterWithValues(Span<float> keys, Span<TValue> values, int i, int j)
            {
                if (keys[i] > keys[j])
                {
                    float key = keys[i];
                    keys[i] = keys[j];
                    keys[j] = key;

                    TValue value = values[i];
                    values[i] = values[j];
                    values[j] = value;
                }
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private static void Swap(Span<float> keys, Span<TValue> values, int i, int j)
            {
                float k = keys[i];
                keys[i] = keys[j];
                keys[j] = k;

                TValue v = values[i];
                values[i] = values[j];
                values[j] = v;
            }

            private static void IntrospectiveSort(Span<float> keys, Span<TValue> values)
            {
                if (keys.Length > 1)
                {
                    IntroSort(keys, values, 2 * (NumericUtilities.Log2((uint)keys.Length) + 1));
                }
            }

            private static void IntroSort(Span<float> keys, Span<TValue> values, int depthLimit)
            {
                int partitionSize = keys.Length;
                while (partitionSize > 1)
                {
                    if (partitionSize <= 16)
                    {
                        if (partitionSize == 2)
                        {
                            SwapIfGreaterWithValues(keys, values, 0, 1);
                            return;
                        }

                        if (partitionSize == 3)
                        {
                            SwapIfGreaterWithValues(keys, values, 0, 1);
                            SwapIfGreaterWithValues(keys, values, 0, 2);
                            SwapIfGreaterWithValues(keys, values, 1, 2);
                            return;
                        }

                        InsertionSort(keys.Slice(0, partitionSize), values.Slice(0, partitionSize));
                        return;
                    }

                    if (depthLimit == 0)
                    {
                        HeapSort(keys.Slice(0, partitionSize), values.Slice(0, partitionSize));
                        return;
                    }

                    depthLimit--;

                    int p = PickPivotAndPartition(keys.Slice(0, partitionSize), values.Slice(0, partitionSize));

                    // Note we've already partitioned around the pivot and do not have to move the pivot again.
                    int s = p + 1;
                    int l = partitionSize - s;

                    // IntroSort(keys[(p + 1) .. partitionSize], values[(p + 1) .. partitionSize], depthLimit);
                    IntroSort(keys.Slice(s, l), values.Slice(s, l), depthLimit);
                    partitionSize = p;
                }
            }

            private static int PickPivotAndPartition(Span<float> keys, Span<TValue> values)
            {
                int hi = keys.Length - 1;

                // Compute median-of-three.  But also partition them, since we've done the comparison.
                int middle = hi >> 1;

                // Sort lo, mid and hi appropriately, then pick mid as the pivot.
                SwapIfGreaterWithValues(keys, values, 0, middle);  // swap the low with the mid point
                SwapIfGreaterWithValues(keys, values, 0, hi);   // swap the low with the high
                SwapIfGreaterWithValues(keys, values, middle, hi); // swap the middle with the high

                float pivot = keys[middle];
                Swap(keys, values, middle, hi - 1);
                int left = 0, right = hi - 1;  // We already partitioned lo and hi and put the pivot in hi - 1.  And we pre-increment & decrement below.

                while (left < right)
                {
#pragma warning disable SA1503, SA1106
                    while (keys[++left] < pivot)
                    {
                        ;
                    }

                    while (pivot < keys[--right])
                    {
                        ;
                    }
#pragma warning restore SA1503, SA1106

                    if (left >= right)
                    {
                        break;
                    }

                    Swap(keys, values, left, right);
                }

                // Put pivot in the right location.
                if (left != hi - 1)
                {
                    Swap(keys, values, left, hi - 1);
                }

                return left;
            }

            private static void HeapSort(Span<float> keys, Span<TValue> values)
            {
                int n = keys.Length;
                for (int i = n >> 1; i >= 1; i--)
                {
                    DownHeap(keys, values, i, n, 0);
                }

                for (int i = n; i > 1; i--)
                {
                    Swap(keys, values, 0, i - 1);
                    DownHeap(keys, values, 1, i - 1, 0);
                }
            }

            private static void DownHeap(Span<float> keys, Span<TValue> values, int i, int n, int lo)
            {
                float d = keys[lo + i - 1];
                TValue dValue = values[lo + i - 1];

                while (i <= n >> 1)
                {
                    int child = 2 * i;
                    if (child < n && keys[lo + child - 1] < keys[lo + child])
                    {
                        child++;
                    }

                    if (!(d < keys[lo + child - 1]))
                    {
                        break;
                    }

                    keys[lo + i - 1] = keys[lo + child - 1];
                    values[lo + i - 1] = values[lo + child - 1];
                    i = child;
                }

                keys[lo + i - 1] = d;
                values[lo + i - 1] = dValue;
            }

            private static void InsertionSort(Span<float> keys, Span<TValue> values)
            {
                for (int i = 0; i < keys.Length - 1; i++)
                {
                    float t = keys[i + 1];
                    TValue tValue = values[i + 1];

                    int j = i;
                    while (j >= 0 && t < keys[j])
                    {
                        keys[j + 1] = keys[j];
                        values[j + 1] = values[j];
                        j--;
                    }

                    keys[j + 1] = t;
                    values[j + 1] = tValue;
                }
            }
        }
    }
}
