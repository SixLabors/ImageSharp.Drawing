// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace SixLabors.ImageSharp.Drawing.Utilities
{
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

                Vector<float> vecVal = new Vector<float>(value);
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

        // https://apisof.net/catalog/System.Numerics.BitOperations.Log2(UInt32)
        // BitOperations.Log2() has been introduced in .NET Core 3.0,
        // since we do target only 3.1+, we can detect it's presence by using SUPPORTS_RUNTIME_INTRINSICS
        // TODO: Ideally this should have a separate definition in Build.props, but that adaption should be done cross-repo. Using a workaround until then.
#if SUPPORTS_RUNTIME_INTRINSICS
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Log2(uint value) => System.Numerics.BitOperations.Log2(value);

#else

#pragma warning disable SA1515, SA1414, SA1114, SA1201
        private static ReadOnlySpan<byte> Log2DeBruijn => new byte[32]
        {
            00, 09, 01, 10, 13, 21, 02, 29,
            11, 14, 16, 18, 22, 25, 03, 30,
            08, 12, 20, 28, 15, 17, 24, 07,
            19, 27, 23, 06, 26, 05, 04, 31
        };

        // Adapted from:
        // https://github.com/dotnet/runtime/blob/5c65d891f203618245184fa54397ced0a8ca806c/src/libraries/System.Private.CoreLib/src/System/Numerics/BitOperations.cs#L205-L223
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Log2(uint value)
        {
            // No AggressiveInlining due to large method size
            // Has conventional contract 0->0 (Log(0) is undefined)

            // Fill trailing zeros with ones, eg 00010010 becomes 00011111
            value |= value >> 01;
            value |= value >> 02;
            value |= value >> 04;
            value |= value >> 08;
            value |= value >> 16;

            // uint.MaxValue >> 27 is always in range [0 - 31] so we use Unsafe.AddByteOffset to avoid bounds check
            return Unsafe.AddByteOffset(
                // Using deBruijn sequence, k=2, n=5 (2^5=32) : 0b_0000_0111_1100_0100_1010_1100_1101_1101u
                ref MemoryMarshal.GetReference(Log2DeBruijn),
                // uint|long -> IntPtr cast on 32-bit platforms does expensive overflow checks not needed here
                (IntPtr)(int)((value * 0x07C4ACDDu) >> 27));
        }

#pragma warning restore
#endif
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
}