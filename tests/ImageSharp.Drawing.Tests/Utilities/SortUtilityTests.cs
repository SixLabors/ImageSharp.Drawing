// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Linq;
using SixLabors.ImageSharp.Drawing.Utilities;
using Xunit;

namespace SixLabors.ImageSharp.Drawing.Tests.Utilities
{
    public class SortUtilityTests
    {
        public static TheoryData<float[]> GenerateTestData()
        {
            TheoryData<float[]> result = new TheoryData<float[]>();

            Random rnd = new Random(42);

            int[] sizes = {1, 2, 3, 5, 10, 16, 42, 500, 1000};

            foreach (int size in sizes)
            {
                float[] keys = new float[size];
                for (int i = 0; i < keys.Length; i++)
                {
                    keys[i] = (float) (rnd.NextDouble() * 10);
                }
                result.Add(keys);
            }
            
            return result;
        }

        [Theory]
        [MemberData(nameof(GenerateTestData))]
        public void Sort(float[] data)
        {
            float[] expected = data.ToArray();

            Array.Sort(expected);

            SortUtility.Sort(data);

            Assert.Equal(expected, data);
        }

        [Fact]
        public void SortSlice()
        {
            float[] data = { 3, 2, 1, 0, -1 };

            Span<float> slice = data.AsSpan(1, 3);
            SortUtility.Sort(slice);
            float[] actual = slice.ToArray();
            float[] expected = { 0, 1, 2 };

            Assert.Equal(actual, expected);
        }

        [Theory]
        [MemberData(nameof(GenerateTestData))]
        public void KeyValueSort(float[] keys)
        {
            int[] values = keys.Select(k => (int) (k * 1000)).ToArray();
            
            SortUtility.Sort(keys, values.AsSpan());
            VerifySorted(keys, values);
        }

        private static void VerifySorted(float[] keys, int[] values)
        {
            for (int i = 0; i < keys.Length; i++)
            {
                float current = keys[i];
                float next = i < keys.Length - 1 ? keys[i + 1] : float.MaxValue;
                Assert.True(current <= next);
                Assert.Equal((int)(current*1000), values[i]);
            }
        }
    }
}
