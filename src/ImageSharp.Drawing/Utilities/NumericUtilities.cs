// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace SixLabors.ImageSharp.Drawing.Utilities;

internal static class NumericUtilities
{
    public static void AddToAllElements(this Span<float> span, float value)
    {
        ref float current = ref MemoryMarshal.GetReference(span);
        ref float max = ref Unsafe.Add(ref current, span.Length);

        if (Vector.IsHardwareAccelerated)
        {
            int n = span.Length / Vector<float>.Count;
            ref Vector<float> currentVec = ref Unsafe.As<float, Vector<float>>(ref current);
            ref Vector<float> maxVec = ref Unsafe.Add(ref currentVec, n);

            Vector<float> vecVal = new(value);
            while (Unsafe.IsAddressLessThan(ref currentVec, ref maxVec))
            {
                currentVec += vecVal;
                currentVec = ref Unsafe.Add(ref currentVec, 1);
            }

            // current = ref Unsafe.Add(ref current, n * Vector<float>.Count);
            current = ref Unsafe.As<Vector<float>, float>(ref currentVec);
        }

        while (Unsafe.IsAddressLessThan(ref current, ref max))
        {
            current += value;
            current = ref Unsafe.Add(ref current, 1);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float ClampFloat(float value, float min, float max)
    {
        if (value >= max)
        {
            return max;
        }

        if (value <= min)
        {
            return min;
        }

        return value;
    }
}
